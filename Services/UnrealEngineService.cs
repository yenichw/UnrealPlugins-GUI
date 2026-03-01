using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public class UnrealEngineService : IUnrealEngineService
    {
        private readonly ILogger<UnrealEngineService> _logger;
        
        // Critical engine plugins that should never be disabled
        private readonly HashSet<string> _criticalEnginePlugins = new(StringComparer.OrdinalIgnoreCase)
        {
            "Slate",
            "UMG",
            "RenderCore",
            "RHI",
            "EditorStyle",
            "ToolMenus",
            "EditorWidgets",
            "UnrealEd",
            "CoreUObject",
            "Engine",
            "Projects",
            "SlateCore",
            "ApplicationCore",
            "InputCore",
            "HTTP",
            "Json",
            "NetCore",
            "PacketHandler",
            "Sockets",
            "SSL",
            "MixedReality",
            "AudioMixer",
            "CinematicCamera",
            "GameplayTags",
            "LevelSequence",
            "MovieScene",
            "TimeManagement",
            "GameplayTasks"
        };

        public UnrealEngineService(ILogger<UnrealEngineService> logger)
        {
            _logger = logger;
        }

        public async Task<List<UnrealEngine>> DetectEnginesAsync()
        {
            var engines = new List<UnrealEngine>();
            
            // Common Unreal Engine installation paths
            var searchPaths = GetCommonEnginePaths();

            foreach (var path in searchPaths)
            {
                if (Directory.Exists(path))
                {
                    var engineVersions = Directory.GetDirectories(path);
                    foreach (var enginePath in engineVersions)
                    {
                        try
                        {
                            var engine = await CreateEngineFromPathAsync(enginePath);
                            if (engine != null)
                            {
                                engines.Add(engine);
                                _logger.LogInformation($"Detected Unreal Engine: {engine.Version} at {engine.Path}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to parse engine at {enginePath}: {ex.Message}");
                        }
                    }
                }
            }

            // Also check registry-based installations
            await CheckRegistryInstallationsAsync(engines);

            return engines;
        }

        public async Task<UnrealEngine?> GetEngineByVersionAsync(string version)
        {
            var engines = await DetectEnginesAsync();
            return engines.Find(e => e.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> ValidateEnginePathAsync(string path)
        {
            try
            {
                var version = await GetEngineVersionFromPathAsync(path);
                return !string.IsNullOrEmpty(version);
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetEngineVersionFromPathAsync(string path)
        {
            var versionFile = Path.Combine(path, "Engine", "Build", "Build.version");
            
            if (!File.Exists(versionFile))
            {
                // Try alternative version file location
                versionFile = Path.Combine(path, "Engine", "Source", "Runtime", "Launch", "Resources", "Version.ini");
            }

            if (File.Exists(versionFile))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(versionFile);
                    
                    // Try parsing as JSON first
                    if (content.TrimStart().StartsWith("{"))
                    {
                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("MajorVersion", out var majorVersionProp) &&
                            root.TryGetProperty("MinorVersion", out var minorVersionProp))
                        {
                            var majorVersion = majorVersionProp.ValueKind == JsonValueKind.String ? 
                                majorVersionProp.GetString() : 
                                majorVersionProp.GetInt32().ToString();
                            var minorVersion = minorVersionProp.ValueKind == JsonValueKind.String ? 
                                minorVersionProp.GetString() : 
                                minorVersionProp.GetInt32().ToString();
                            
                            return $"{majorVersion}.{minorVersion}";
                        }
                    }
                    
                    // Try parsing as INI format
                    var lines = content.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("MajorVersion="))
                        {
                            return line.Substring("MajorVersion=".Length).Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to parse version file {versionFile}: {ex.Message}");
                }
            }

            // Fallback: try to extract version from path
            var pathParts = path.Split(Path.DirectorySeparatorChar);
            foreach (var part in pathParts)
            {
                if (part.StartsWith("UE") && part.Length > 2)
                {
                    var versionPart = part.Substring(2);
                    if (double.TryParse(versionPart, out _))
                    {
                        return versionPart;
                    }
                }
            }

            return "Unknown";
        }

        private List<string> GetCommonEnginePaths()
        {
            var paths = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games"),
                "C:\\Epic Games",
                "D:\\Epic Games",
                "E:\\Epic Games"
            };

            // Add user-specific paths
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            paths.Add(Path.Combine(localAppData, "Epic Games"));

            return paths;
        }

        private async Task CheckRegistryInstallationsAsync(List<UnrealEngine> engines)
        {
            try
            {
                // Check for Epic Games Launcher installations using manifests
                var manifestPath = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
                
                if (Directory.Exists(manifestPath))
                {
                    var manifestFiles = Directory.GetFiles(manifestPath, "*.item");
                    foreach (var manifestFile in manifestFiles)
                    {
                        try
                        {
                            var manifest = await File.ReadAllTextAsync(manifestFile);
                            using var doc = JsonDocument.Parse(manifest);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("AppName", out var appNameProp))
                            {
                                var appName = appNameProp.GetString();
                                
                                if (!string.IsNullOrEmpty(appName) && 
                                    (appName.Contains("UE_") || appName.StartsWith("UE")))
                                {
                                    if (root.TryGetProperty("InstallLocation", out var installLocationProp))
                                    {
                                        var installLocation = installLocationProp.GetString();
                                        if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                        {
                                            var engine = await CreateEngineFromPathAsync(installLocation);
                                            if (engine != null && !engines.Exists(e => e.Path == installLocation))
                                            {
                                                // Extract version from app name
                                                var version = appName.Contains("UE_") ? 
                                                    appName.Replace("UE_", "") : 
                                                    appName;
                                                engine.Version = version;
                                                engines.Add(engine);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to parse manifest {manifestFile}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check Epic Games Launcher manifests: {ex.Message}");
            }
        }

        private async Task<UnrealEngine?> CreateEngineFromPathAsync(string enginePath)
        {
            var version = await GetEngineVersionFromPathAsync(enginePath);
            if (string.IsNullOrEmpty(version) || version == "Unknown")
                return null;

            // Validate this is actually an Unreal Engine installation
            var engineExecutable = Path.Combine(enginePath, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
            if (!File.Exists(engineExecutable))
                return null;

            return new UnrealEngine
            {
                Path = enginePath,
                Version = version,
                FullVersion = version,
                IsInstalled = true,
                LastDetected = DateTime.Now
            };
        }

        public async Task<List<Plugin>> GetEnginePluginsAsync(UnrealEngine engine)
        {
            var plugins = new List<Plugin>();
            var enginePluginsPath = Path.Combine(engine.Path, "Engine", "Plugins");

            if (!Directory.Exists(enginePluginsPath))
            {
                _logger.LogWarning($"Engine plugins directory not found: {enginePluginsPath}");
                return plugins;
            }

            try
            {
                // Search all plugin directories recursively
                var pluginDirectories = Directory.GetDirectories(enginePluginsPath, "*", SearchOption.AllDirectories);
                
                foreach (var pluginDir in pluginDirectories)
                {
                    var plugin = await ParseEnginePluginAsync(pluginDir);
                    if (plugin != null)
                    {
                        plugins.Add(plugin);
                    }
                }

                _logger.LogInformation($"Found {plugins.Count} engine plugins for {engine.Version}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to scan engine plugins: {ex.Message}");
            }

            return plugins.OrderBy(p => p.Name).ToList();
        }

        public async Task<bool> SetEnginePluginDefaultStateAsync(UnrealEngine engine, string pluginId, bool enabled)
        {
            try
            {
                // Check if plugin is critical
                if (await IsEnginePluginCriticalAsync(pluginId))
                {
                    _logger.LogWarning($"Refusing to modify critical engine plugin: {pluginId}");
                    return false;
                }

                // Check permissions
                if (!await HasEngineModifyPermissionsAsync(engine))
                {
                    _logger.LogError($"Insufficient permissions to modify engine plugins at {engine.Path}");
                    return false;
                }

                // Find plugin directory
                var pluginDir = await FindEnginePluginDirectoryAsync(engine.Path, pluginId);
                if (string.IsNullOrEmpty(pluginDir))
                {
                    _logger.LogError($"Engine plugin directory not found: {pluginId}");
                    return false;
                }

                var upluginPath = Path.Combine(pluginDir, $"{pluginId}.uplugin");
                if (!File.Exists(upluginPath))
                {
                    _logger.LogError($"Plugin .uplugin file not found: {upluginPath}");
                    return false;
                }

                // Create backup
                var backupPath = upluginPath + ".backup." + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(upluginPath, backupPath, true);
                _logger.LogInformation($"Created backup: {backupPath}");

                // Read and modify the .uplugin file
                var content = await File.ReadAllTextAsync(upluginPath);
                using var doc = JsonDocument.Parse(content);
                var root = JsonNode.Parse(content);

                if (root == null)
                {
                    _logger.LogError($"Failed to parse plugin descriptor: {upluginPath}");
                    return false;
                }

                // Set EnabledByDefault
                root["EnabledByDefault"] = enabled;

                // Write back
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                var updatedContent = root.ToJsonString(jsonOptions);
                await File.WriteAllTextAsync(upluginPath, updatedContent);

                _logger.LogInformation($"{(enabled ? "Enabled" : "Disabled")} engine plugin {pluginId} in {engine.Version}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to set engine plugin state for {pluginId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsEnginePluginCriticalAsync(string pluginId)
        {
            // Check against our blacklist
            if (_criticalEnginePlugins.Contains(pluginId))
                return true;

            // Additional checks for plugins with critical naming patterns
            var criticalPatterns = new[] { "Core", "Editor", "Engine", "Render", "RHI" };
            return criticalPatterns.Any(pattern => 
                pluginId.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> HasEngineModifyPermissionsAsync(UnrealEngine engine)
        {
            try
            {
                // Check if running as administrator
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    _logger.LogWarning("Not running as administrator - may not have engine modify permissions");
                }

                // Test write permissions on engine directory
                var testFile = Path.Combine(engine.Path, "Engine", "Plugins", ".write_test");
                try
                {
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check engine permissions: {ex.Message}");
                return false;
            }
        }

        private async Task<Plugin?> ParseEnginePluginAsync(string pluginDir)
        {
            var pluginId = Path.GetFileName(pluginDir);
            var upluginFile = Path.Combine(pluginDir, $"{pluginId}.uplugin");

            if (!File.Exists(upluginFile))
                return null;

            try
            {
                var content = await File.ReadAllTextAsync(upluginFile);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var pluginName = pluginId;
                if (root.TryGetProperty("FriendlyName", out var friendlyNameProp))
                    pluginName = friendlyNameProp.GetString() ?? pluginName;

                var version = "1.0";
                if (root.TryGetProperty("VersionName", out var versionNameProp))
                {
                    version = versionNameProp.GetString() ?? version;
                }
                else if (root.TryGetProperty("Version", out var versionProp))
                {
                    if (versionProp.ValueKind == JsonValueKind.Number)
                        version = versionProp.GetInt32().ToString();
                    else if (versionProp.ValueKind == JsonValueKind.String)
                        version = versionProp.GetString() ?? version;
                }

                var enabledByDefault = true;
                if (root.TryGetProperty("EnabledByDefault", out var enabledProp))
                    enabledByDefault = enabledProp.ValueKind == JsonValueKind.True;

                var description = string.Empty;
                if (root.TryGetProperty("Description", out var descProp))
                    description = descProp.GetString() ?? string.Empty;

                var author = string.Empty;
                if (root.TryGetProperty("CreatedBy", out var authorProp))
                    author = authorProp.GetString() ?? string.Empty;

                var isCritical = await IsEnginePluginCriticalAsync(pluginId);

                return new Plugin
                {
                    Id = pluginId,
                    Name = pluginName,
                    Version = version,
                    EngineVersion = "Engine",
                    Path = pluginDir,
                    Type = PluginType.Engine,
                    IsEnabled = enabledByDefault,
                    Description = description,
                    Author = author,
                    LastModified = Directory.GetLastWriteTime(pluginDir)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to parse engine plugin {pluginId}: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> FindEnginePluginDirectoryAsync(string enginePath, string pluginId)
        {
            var enginePluginsPath = Path.Combine(enginePath, "Engine", "Plugins");
            if (!Directory.Exists(enginePluginsPath))
                return null;

            try
            {
                var pluginDirectories = Directory.GetDirectories(enginePluginsPath, "*", SearchOption.AllDirectories);
                return pluginDirectories.FirstOrDefault(dir => 
                    Path.GetFileName(dir).Equals(pluginId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to find engine plugin directory: {ex.Message}");
                return null;
            }
        }
    }

    // Helper classes for JSON parsing
    internal class EpicManifest
    {
        public string? AppName { get; set; }
        public string InstallLocation { get; set; } = string.Empty;
    }
}
