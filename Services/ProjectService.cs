using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public class ProjectService : IProjectService
    {
        private readonly ILogger<ProjectService> _logger;
        private readonly IPluginService _pluginService;

        private readonly string _projectsStorePath;

        public ProjectService(ILogger<ProjectService> logger, IPluginService pluginService)
        {
            _logger = logger;
            _pluginService = pluginService;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var storeDir = Path.Combine(appData, "UnrealPluginsGUI");
            Directory.CreateDirectory(storeDir);
            _projectsStorePath = Path.Combine(storeDir, "projects.json");
        }

        public async Task<List<UnrealProject>> DetectProjectsAsync()
        {
            var projects = new List<UnrealProject>();

            // Load persisted project paths (manually added)
            var persistedPaths = await LoadPersistedProjectPathsAsync();
            foreach (var path in persistedPaths)
            {
                var persistedProject = await LoadProjectAsync(path);
                if (persistedProject != null)
                    projects.Add(persistedProject);
            }

            // Common project search locations
            var searchPaths = GetCommonProjectPaths();

            foreach (var searchPath in searchPaths)
            {
                if (Directory.Exists(searchPath))
                {
                    await SearchDirectoryForProjectsAsync(searchPath, projects);
                }
            }

            // Also check user documents
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var documentsProjectsPath = Path.Combine(documentsPath, "Unreal Projects");
            if (Directory.Exists(documentsProjectsPath))
            {
                await SearchDirectoryForProjectsAsync(documentsProjectsPath, projects);
            }

            return projects.DistinctBy(p => p.ProjectFilePath).ToList();
        }

        public async Task<UnrealProject?> LoadProjectAsync(string projectPath)
        {
            try
            {
                if (!File.Exists(projectPath))
                    return null;

                var projectDir = Path.GetDirectoryName(projectPath);
                if (string.IsNullOrEmpty(projectDir))
                    return null;

                var content = await File.ReadAllTextAsync(projectPath);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var friendlyName = Path.GetFileNameWithoutExtension(projectPath);
                if (root.TryGetProperty("FriendlyName", out var friendlyNameProp))
                {
                    friendlyName = friendlyNameProp.GetString() ?? friendlyName;
                }

                var engineAssociation = string.Empty;
                if (root.TryGetProperty("EngineAssociation", out var engineAssociationProp))
                {
                    engineAssociation = engineAssociationProp.GetString() ?? string.Empty;
                }

                var project = new UnrealProject
                {
                    Name = friendlyName,
                    Path = projectDir,
                    ProjectFilePath = projectPath,
                    EngineAssociation = engineAssociation,
                    EngineVersion = await ExtractEngineVersionFromAssociation(engineAssociation),
                    LastAccessed = DateTime.Now
                };

                // Load installed plugins
                project.InstalledPlugins = await _pluginService.GetProjectPluginsAsync(project);

                return project;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load project from {projectPath}: {ex.Message}");
                return null;
            }
        }

        private async Task<string> ExtractEngineVersionFromAssociation(string? engineAssociation)
        {
            if (string.IsNullOrEmpty(engineAssociation))
                return "Unknown";

            // If it's a version string like "5.3", return as-is
            if (System.Text.RegularExpressions.Regex.IsMatch(engineAssociation, @"^\d+\.\d+"))
                return engineAssociation;

            // If it's a GUID, look up in registry
            if (engineAssociation.StartsWith("{") && engineAssociation.EndsWith("}"))
            {
                return await GetEngineVersionFromGuidAsync(engineAssociation);
            }

            // Try to extract version from path-like association
            var versionMatch = System.Text.RegularExpressions.Regex.Match(engineAssociation, @"(\d+\.\d+)");
            return versionMatch.Success ? versionMatch.Value : engineAssociation;
        }

        private async Task<string> GetEngineVersionFromGuidAsync(string guid)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Epic Games\Unreal Engine\Builds");
                if (key != null)
                {
                    var valueNames = key.GetValueNames();
                    foreach (var valueName in valueNames)
                    {
                        if (valueName.Equals(guid, StringComparison.OrdinalIgnoreCase))
                        {
                            var enginePath = key.GetValue(valueName) as string;
                            if (!string.IsNullOrEmpty(enginePath) && Directory.Exists(enginePath))
                            {
                                // Read the actual version from Build.version
                                var buildVersionFile = Path.Combine(enginePath, "Engine", "Build", "Build.version");
                                if (File.Exists(buildVersionFile))
                                {
                                    var content = await File.ReadAllTextAsync(buildVersionFile);
                                    using var doc = System.Text.Json.JsonDocument.Parse(content);
                                    var root = doc.RootElement;
                                    
                                    if (root.TryGetProperty("MajorVersion", out var majorVersionProp) &&
                                        root.TryGetProperty("MinorVersion", out var minorVersionProp))
                                    {
                                        var majorVersion = majorVersionProp.ValueKind == System.Text.Json.JsonValueKind.String ? 
                                            majorVersionProp.GetString() : 
                                            majorVersionProp.GetInt32().ToString();
                                        var minorVersion = minorVersionProp.ValueKind == System.Text.Json.JsonValueKind.String ? 
                                            minorVersionProp.GetString() : 
                                            minorVersionProp.GetInt32().ToString();
                                        
                                        return $"{majorVersion}.{minorVersion}";
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to lookup engine version from GUID {guid}: {ex.Message}");
            }

            return "Unknown";
        }

        public async Task<bool> InstallPluginToProjectAsync(Plugin plugin, UnrealProject project)
        {
            try
            {
                var pluginsPath = Path.Combine(project.Path, "Plugins");
                if (!Directory.Exists(pluginsPath))
                {
                    Directory.CreateDirectory(pluginsPath);
                }

                var pluginFolder = string.IsNullOrWhiteSpace(plugin.Id) ? plugin.Name : plugin.Id;
                var pluginDestinationPath = Path.Combine(pluginsPath, pluginFolder);

                // Check if plugin already exists
                if (Directory.Exists(pluginDestinationPath))
                {
                    _logger.LogWarning($"Plugin {plugin.Name} already exists in project {project.Name}");
                    return false;
                }

                // Check compatibility
                var isCompatible = await _pluginService.IsPluginCompatibleAsync(plugin, project.EngineVersion);
                if (!isCompatible)
                {
                    _logger.LogWarning($"Plugin {plugin.Name} is not compatible with engine version {project.EngineVersion}");
                    // Continue with installation but warn the user
                }

                if (plugin.Type == PluginType.Archive && !string.IsNullOrEmpty(plugin.ArchivePath))
                {
                    // Extract from archive into a dedicated plugin folder
                    var archiveService = new ArchiveService(NullLogger<ArchiveService>.Instance);
                    var success = await archiveService.ExtractPluginAsync(plugin.ArchivePath, pluginDestinationPath);
                    
                    if (!success)
                    {
                        _logger.LogError($"Failed to extract plugin {plugin.Name} from archive");
                        return false;
                    }
                }
                else if (Directory.Exists(plugin.Path))
                {
                    // Copy from existing plugin directory
                    await CopyDirectoryAsync(plugin.Path, pluginDestinationPath);
                }
                else
                {
                    _logger.LogError($"Invalid plugin source path for {plugin.Name}");
                    return false;
                }

                // Refresh project plugins
                project.InstalledPlugins = await _pluginService.GetProjectPluginsAsync(project);

                _logger.LogInformation($"Successfully installed plugin {plugin.Name} to project {project.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to install plugin {plugin.Name} to project {project.Name}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemovePluginFromProjectAsync(Plugin plugin, UnrealProject project)
        {
            try
            {
                var pluginFolder = string.IsNullOrWhiteSpace(plugin.Id) ? plugin.Name : plugin.Id;
                var pluginPath = Path.Combine(project.Path, "Plugins", pluginFolder);

                if (!Directory.Exists(pluginPath))
                {
                    _logger.LogWarning($"Plugin {plugin.Name} not found in project {project.Name}");
                    return false;
                }

                // Remove the plugin directory
                Directory.Delete(pluginPath, true);

                // Refresh project plugins
                project.InstalledPlugins = await _pluginService.GetProjectPluginsAsync(project);

                _logger.LogInformation($"Successfully removed plugin {plugin.Name} from project {project.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to remove plugin {plugin.Name} from project {project.Name}: {ex.Message}");
                return false;
            }
        }

        public async Task<UnrealProject?> CreateProjectEntryAsync(string uprojectFilePath)
        {
            var project = await LoadProjectAsync(uprojectFilePath);
            if (project != null)
            {
                await AddPersistedProjectPathAsync(project.ProjectFilePath);
            }
            return project;
        }

        private async Task<List<string>> LoadPersistedProjectPathsAsync()
        {
            try
            {
                if (!File.Exists(_projectsStorePath))
                    return new List<string>();

                var json = await File.ReadAllTextAsync(_projectsStorePath);
                var paths = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return paths
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load persisted projects list: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task AddPersistedProjectPathAsync(string projectFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectFilePath))
                    return;

                var current = await LoadPersistedProjectPathsAsync();
                if (!current.Contains(projectFilePath, StringComparer.OrdinalIgnoreCase))
                    current.Add(projectFilePath);

                var json = JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_projectsStorePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to persist project path {projectFilePath}: {ex.Message}");
            }
        }

        private List<string> GetCommonProjectPaths()
        {
            var paths = new List<string>
            {
                "C:\\UnrealProjects",
                "D:\\UnrealProjects",
                "E:\\UnrealProjects"
            };

            // Add user home directory
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            paths.Add(Path.Combine(homePath, "UnrealProjects"));

            return paths;
        }

        private async Task SearchDirectoryForProjectsAsync(string searchPath, List<UnrealProject> projects)
        {
            try
            {
                var uprojectFiles = Directory.GetFiles(searchPath, "*.uproject", SearchOption.AllDirectories);

                foreach (var uprojectFile in uprojectFiles)
                {
                    var project = await LoadProjectAsync(uprojectFile);
                    if (project != null)
                    {
                        projects.Add(project);
                        _logger.LogInformation($"Found project: {project.Name} at {project.Path}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to search directory {searchPath}: {ex.Message}");
            }
        }

        private async Task CopyDirectoryAsync(string sourcePath, string destinationPath)
        {
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            // Copy files
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var fileName = Path.GetFileName(file);
                var destinationFile = Path.Combine(destinationPath, fileName);
                await Task.Run(() => File.Copy(file, destinationFile, true));
            }

            // Copy subdirectories
            foreach (var directory in Directory.GetDirectories(sourcePath))
            {
                var dirName = Path.GetFileName(directory);
                var destinationDir = Path.Combine(destinationPath, dirName);
                await CopyDirectoryAsync(directory, destinationDir);
            }
        }
    }

    // Helper class for parsing .uproject files
    internal class ProjectFileData
    {
        public string?FileVersion { get; set; }
        public string?EngineAssociation { get; set; }
        public string?Category { get; set; }
        public string?Description { get; set; }
        public bool?DisableEnginePluginsByDefault { get; set; }
        public string?FriendlyName { get; set; }
        public List<string>?Modules { get; set; }
        public string?AdditionalPluginDirectories { get; set; }
        public List<string>?TargetPlatforms { get; set; }
        public bool?bTargetedTest { get; set; }
        public bool?bTestForEditableOnly { get; set; }
        public bool?bCanContainContent { get; set; }
        public bool?bIsBetaProject { get; set; }
        public bool?bIsHiddenProject { get; set; }
        public bool?bSupportsTargetPlatform { get; set; }
    }
}
