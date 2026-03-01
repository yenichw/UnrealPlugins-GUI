using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using UnrealPluginsGUI.Models;
using UnrealPluginsGUI.Services;

namespace UnrealPluginsGUI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IUnrealEngineService _engineService;
        private readonly IPluginService _pluginService;
        private readonly IProjectService _projectService;
        private readonly IArchiveService _archiveService;
        private readonly IPluginLibraryService _pluginLibraryService;
        private readonly ILogger<MainViewModel> _logger;

        private UnrealEngine? _selectedEngine;
        private UnrealProject? _selectedProject;
        private Plugin? _selectedPlugin;
        private Plugin? _selectedEnginePlugin;
        private UnrealProject? _selectedLibraryTargetProject;
        private PluginLibraryEntry? _selectedLibraryEntry;
        private string _statusMessage = "Ready";
        private bool _isLoading = false;

        public MainViewModel(
            IUnrealEngineService engineService,
            IPluginService pluginService,
            IProjectService projectService,
            IArchiveService archiveService,
            IPluginLibraryService pluginLibraryService,
            ILogger<MainViewModel> logger)
        {
            _engineService = engineService;
            _pluginService = pluginService;
            _projectService = projectService;
            _archiveService = archiveService;
            _pluginLibraryService = pluginLibraryService;
            _logger = logger;

            Engines = new ObservableCollection<UnrealEngine>();
            Projects = new ObservableCollection<UnrealProject>();
            Plugins = new ObservableCollection<Plugin>();
            EnginePlugins = new ObservableCollection<Plugin>();
            LibraryEntries = new ObservableCollection<PluginLibraryEntry>();

            // Initialize commands
            RefreshEnginesCommand = ReactiveCommand.CreateFromTask(RefreshEnginesAsync);
            RefreshProjectsCommand = ReactiveCommand.CreateFromTask(RefreshProjectsAsync);
            AddProjectCommand = ReactiveCommand.CreateFromTask(AddProjectAsync);
            InstallPluginCommand = ReactiveCommand.CreateFromTask(InstallPluginAsync);
            EnablePluginCommand = ReactiveCommand.CreateFromTask(EnablePluginAsync);
            DisablePluginCommand = ReactiveCommand.CreateFromTask(DisablePluginAsync);
            RefreshPluginsCommand = ReactiveCommand.CreateFromTask(RefreshPluginsAsync);

            RefreshLibraryCommand = ReactiveCommand.CreateFromTask(RefreshLibraryAsync);
            ImportToLibraryCommand = ReactiveCommand.CreateFromTask(ImportToLibraryAsync);
            RemoveFromLibraryCommand = ReactiveCommand.CreateFromTask(RemoveFromLibraryAsync);
            AddLibraryToProjectCommand = ReactiveCommand.CreateFromTask(AddLibraryToProjectAsync);

            // Engine plugin commands
            EnableEnginePluginCommand = ReactiveCommand.CreateFromTask(EnableEnginePluginAsync);
            DisableEnginePluginCommand = ReactiveCommand.CreateFromTask(DisableEnginePluginAsync);

            // Load initial data
            _ = Task.Run(LoadInitialDataAsync);
        }

        public ObservableCollection<UnrealEngine> Engines { get; }
        public ObservableCollection<UnrealProject> Projects { get; }
        public ObservableCollection<Plugin> Plugins { get; }
        public ObservableCollection<Plugin> EnginePlugins { get; }
        public ObservableCollection<PluginLibraryEntry> LibraryEntries { get; }

        public UnrealEngine? SelectedEngine
        {
            get => _selectedEngine;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedEngine, value);
                _ = Task.Run(LoadProjectPluginsForEngineAsync);
            }
        }

        public UnrealProject? SelectedProject
        {
            get => _selectedProject;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedProject, value);
                _ = Task.Run(LoadPluginsForProjectAsync);
            }
        }

        public Plugin? SelectedPlugin
        {
            get => _selectedPlugin;
            set => this.RaiseAndSetIfChanged(ref _selectedPlugin, value);
        }

        public Plugin? SelectedEnginePlugin
        {
            get => _selectedEnginePlugin;
            set => this.RaiseAndSetIfChanged(ref _selectedEnginePlugin, value);
        }

        public PluginLibraryEntry? SelectedLibraryEntry
        {
            get => _selectedLibraryEntry;
            set => this.RaiseAndSetIfChanged(ref _selectedLibraryEntry, value);
        }

        public UnrealProject? SelectedLibraryTargetProject
        {
            get => _selectedLibraryTargetProject;
            set => this.RaiseAndSetIfChanged(ref _selectedLibraryTargetProject, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public ReactiveCommand<Unit, Unit> RefreshEnginesCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshProjectsCommand { get; }
        public ReactiveCommand<Unit, Unit> AddProjectCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallPluginCommand { get; }
        public ReactiveCommand<Unit, Unit> EnablePluginCommand { get; }
        public ReactiveCommand<Unit, Unit> DisablePluginCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshPluginsCommand { get; }

        public ReactiveCommand<Unit, Unit> RefreshLibraryCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportToLibraryCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveFromLibraryCommand { get; }
        public ReactiveCommand<Unit, Unit> AddLibraryToProjectCommand { get; }

        public ReactiveCommand<Unit, Unit> EnableEnginePluginCommand { get; }
        public ReactiveCommand<Unit, Unit> DisableEnginePluginCommand { get; }

        private async Task LoadInitialDataAsync()
        {
            await RefreshEnginesAsync();
            await RefreshProjectsAsync();
            await RefreshLibraryAsync();
        }

        private async Task RefreshLibraryAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading plugin library...";

                var entries = await _pluginLibraryService.GetEntriesAsync();
                LibraryEntries.Clear();
                foreach (var e in entries)
                    LibraryEntries.Add(e);

                StatusMessage = $"Loaded {entries.Count} library entr{(entries.Count == 1 ? "y" : "ies")}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading library: {ex.Message}";
                _logger.LogError(ex, "Failed to load plugin library");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ImportToLibraryAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Select plugin archive to import...";

                var supported = await _archiveService.GetSupportedFormatsAsync();
                var extensions = supported
                    .Select(s => s.TrimStart('.'))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var openFileDialog = new OpenFileDialog
                {
                    Title = "Import Plugin Archive to Library",
                    AllowMultiple = false,
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter { Name = "Plugin Archives", Extensions = extensions }
                    }
                };

                var window = new Window();
                var result = await openFileDialog.ShowAsync(window);
                if (result == null || result.Length == 0)
                {
                    StatusMessage = "Import canceled";
                    return;
                }

                var archivePath = result[0];
                StatusMessage = "Importing to library...";

                var entry = await _pluginLibraryService.ImportArchiveAsync(archivePath);
                if (entry == null)
                {
                    StatusMessage = "Failed to import archive to library";
                    return;
                }

                await RefreshLibraryAsync();
                SelectedLibraryEntry = LibraryEntries.FirstOrDefault(e =>
                    string.Equals(e.PluginId, entry.PluginId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Version, entry.Version, StringComparison.OrdinalIgnoreCase));

                StatusMessage = $"Imported {entry.Name} ({entry.Version}) to library";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error importing to library: {ex.Message}";
                _logger.LogError(ex, "Failed to import plugin to library");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RemoveFromLibraryAsync()
        {
            if (SelectedLibraryEntry == null)
            {
                StatusMessage = "Select a library plugin first";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = $"Removing {SelectedLibraryEntry.Name} ({SelectedLibraryEntry.Version})...";

                var success = await _pluginLibraryService.RemoveEntryAsync(SelectedLibraryEntry);
                if (!success)
                {
                    StatusMessage = "Failed to remove library entry";
                    return;
                }

                SelectedLibraryEntry = null;
                await RefreshLibraryAsync();
                StatusMessage = "Removed from library";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error removing from library: {ex.Message}";
                _logger.LogError(ex, "Failed to remove library entry");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task AddLibraryToProjectAsync()
        {
            if (SelectedLibraryEntry == null)
            {
                StatusMessage = "Select a library plugin first";
                return;
            }

            if (SelectedLibraryTargetProject == null)
            {
                StatusMessage = "Select a target project";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = $"Installing {SelectedLibraryEntry.Name} ({SelectedLibraryEntry.Version})...";

                // Install using existing archive install path
                var plugin = new Plugin
                {
                    Id = SelectedLibraryEntry.PluginId,
                    Name = SelectedLibraryEntry.Name,
                    Version = SelectedLibraryEntry.Version,
                    EngineVersion = SelectedLibraryEntry.EngineVersion,
                    Description = SelectedLibraryEntry.Description,
                    Author = SelectedLibraryEntry.Author,
                    Type = PluginType.Archive,
                    IsEnabled = true,
                    ArchivePath = SelectedLibraryEntry.ArchivePath
                };

                var success = await _projectService.InstallPluginToProjectAsync(plugin, SelectedLibraryTargetProject);
                if (!success)
                {
                    StatusMessage = "Failed to install plugin to project";
                    return;
                }

                // If user currently has that project selected, refresh its plugin list
                if (SelectedProject != null &&
                    string.Equals(SelectedProject.ProjectFilePath, SelectedLibraryTargetProject.ProjectFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    await LoadPluginsForProjectAsync();
                }

                StatusMessage = $"Installed {SelectedLibraryEntry.Name} to {SelectedLibraryTargetProject.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding to project: {ex.Message}";
                _logger.LogError(ex, "Failed to add library plugin to project");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshEnginesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Detecting Unreal Engine installations...";

                var engines = await _engineService.DetectEnginesAsync();
                
                Engines.Clear();
                foreach (var engine in engines)
                {
                    Engines.Add(engine);
                }

                StatusMessage = $"Found {engines.Count} Unreal Engine installation(s)";
                _logger.LogInformation($"Detected {engines.Count} Unreal Engine installations");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error detecting engines: {ex.Message}";
                _logger.LogError(ex, "Failed to detect Unreal Engine installations");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshProjectsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Detecting Unreal projects...";

                var projects = await _projectService.DetectProjectsAsync();
                
                Projects.Clear();
                foreach (var project in projects)
                {
                    Projects.Add(project);
                }

                StatusMessage = $"Found {projects.Count} Unreal project(s)";
                _logger.LogInformation($"Detected {projects.Count} Unreal projects");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error detecting projects: {ex.Message}";
                _logger.LogError(ex, "Failed to detect Unreal projects");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadProjectPluginsForEngineAsync()
        {
            if (SelectedEngine == null)
            {
                Plugins.Clear();
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = $"Loading plugins for {SelectedEngine.Version}...";

                var plugins = await _pluginService.GetEnginePluginsAsync(SelectedEngine);
                
                Plugins.Clear();
                foreach (var plugin in plugins)
                {
                    Plugins.Add(plugin);
                }

                StatusMessage = $"Loaded {plugins.Count} plugin(s) for {SelectedEngine.Version}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading plugins: {ex.Message}";
                _logger.LogError(ex, $"Failed to load plugins for engine {SelectedEngine.Version}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadPluginsForProjectAsync()
        {
            if (SelectedProject == null)
            {
                Plugins.Clear();
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = $"Loading plugins for project {SelectedProject.Name}...";

                var plugins = await _pluginService.GetProjectPluginsAsync(SelectedProject);
                
                Plugins.Clear();
                foreach (var plugin in plugins)
                {
                    Plugins.Add(plugin);
                }

                StatusMessage = $"Loaded {plugins.Count} plugin(s) for project {SelectedProject.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading project plugins: {ex.Message}";
                _logger.LogError(ex, $"Failed to load plugins for project {SelectedProject.Name}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task InstallPluginAsync()
        {
            if (SelectedProject == null)
            {
                StatusMessage = "Please select a project first";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Select plugin archive...";

                var supported = await _archiveService.GetSupportedFormatsAsync();
                var extensions = supported
                    .Select(s => s.TrimStart('.'))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Plugin Archive",
                    AllowMultiple = false,
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter { Name = "Plugin Archives", Extensions = extensions }
                    }
                };

                var window = new Window();
                var result = await openFileDialog.ShowAsync(window);
                if (result == null || result.Length == 0)
                {
                    StatusMessage = "Plugin installation canceled";
                    return;
                }

                var archivePath = result[0];
                StatusMessage = $"Validating archive: {System.IO.Path.GetFileName(archivePath)}";

                var isValid = await _archiveService.ValidatePluginArchiveAsync(archivePath);
                if (!isValid)
                {
                    StatusMessage = "Selected file is not a valid plugin archive";
                    return;
                }

                StatusMessage = "Reading plugin metadata...";
                var plugin = await _archiveService.ParsePluginFromArchiveAsync(archivePath);
                if (plugin == null)
                {
                    StatusMessage = "Failed to read plugin metadata from archive";
                    return;
                }

                // Compatibility check (non-blocking; we warn but still allow install)
                var compatible = await _pluginService.IsPluginCompatibleAsync(plugin, SelectedProject.EngineVersion);
                if (!compatible)
                {
                    StatusMessage = $"Warning: {plugin.Name} may be incompatible with engine {SelectedProject.EngineVersion}. Installing anyway...";
                }
                else
                {
                    StatusMessage = $"Installing {plugin.Name} to project {SelectedProject.Name}...";
                }

                var success = await _projectService.InstallPluginToProjectAsync(plugin, SelectedProject);
                if (!success)
                {
                    StatusMessage = "Failed to install plugin (already installed or invalid plugin structure)";
                    return;
                }

                // Refresh project plugins list
                await LoadPluginsForProjectAsync();
                StatusMessage = $"Installed {plugin.Name} to {SelectedProject.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error installing plugin: {ex.Message}";
                _logger.LogError(ex, "Failed to install plugin");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task EnablePluginAsync()
        {
            if (SelectedPlugin == null || SelectedProject == null)
            {
                StatusMessage = "Please select a plugin and project";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = $"Enabling plugin {SelectedPlugin.Name}...";

                var success = await _pluginService.EnablePluginAsync(SelectedPlugin, SelectedProject);
                
                if (success)
                {
                    StatusMessage = $"Successfully enabled {SelectedPlugin.Name}";
                }
                else
                {
                    StatusMessage = $"Failed to enable {SelectedPlugin.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error enabling plugin: {ex.Message}";
                _logger.LogError(ex, $"Failed to enable plugin {SelectedPlugin.Name}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DisablePluginAsync()
        {
            if (SelectedPlugin == null || SelectedProject == null)
            {
                StatusMessage = "Please select a plugin and project";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = $"Disabling plugin {SelectedPlugin.Name}...";

                var success = await _pluginService.DisablePluginAsync(SelectedPlugin, SelectedProject);
                
                if (success)
                {
                    StatusMessage = $"Successfully disabled {SelectedPlugin.Name}";
                }
                else
                {
                    StatusMessage = $"Failed to disable {SelectedPlugin.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error disabling plugin: {ex.Message}";
                _logger.LogError(ex, $"Failed to disable plugin {SelectedPlugin.Name}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshPluginsAsync()
        {
            if (SelectedEngine != null)
            {
                await LoadProjectPluginsForEngineAsync();
                await LoadPluginsForEngineAsync(); // Also load engine plugins
            }
            else if (SelectedProject != null)
            {
                await LoadPluginsForProjectAsync();
            }
        }

        private async Task LoadPluginsForEngineAsync()
        {
            if (SelectedEngine == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = "Loading engine plugins...";

                var plugins = await _engineService.GetEnginePluginsAsync(SelectedEngine);
                
                EnginePlugins.Clear();
                foreach (var plugin in plugins)
                    EnginePlugins.Add(plugin);

                StatusMessage = $"Loaded {plugins.Count} engine plugins";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load engine plugins: {ex.Message}";
                _logger.LogError(ex, "Failed to load engine plugins");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task EnableEnginePluginAsync()
        {
            if (SelectedEnginePlugin == null || SelectedEngine == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Enabling engine plugin {SelectedEnginePlugin.Name}...";

                // Check if plugin is critical
                if (await _engineService.IsEnginePluginCriticalAsync(SelectedEnginePlugin.Id))
                {
                    StatusMessage = $"Cannot enable/disable critical engine plugin: {SelectedEnginePlugin.Name}";
                    return;
                }

                // Check permissions
                if (!await _engineService.HasEngineModifyPermissionsAsync(SelectedEngine))
                {
                    StatusMessage = "Insufficient permissions to modify engine plugins. Run as administrator.";
                    return;
                }

                var success = await _engineService.SetEnginePluginDefaultStateAsync(SelectedEngine, SelectedEnginePlugin.Id, true);
                
                if (success)
                {
                    StatusMessage = $"Successfully enabled engine plugin {SelectedEnginePlugin.Name}";
                    await LoadPluginsForEngineAsync(); // Refresh list
                }
                else
                {
                    StatusMessage = $"Failed to enable engine plugin {SelectedEnginePlugin.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error enabling engine plugin: {ex.Message}";
                _logger.LogError(ex, $"Failed to enable engine plugin {SelectedEnginePlugin.Name}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DisableEnginePluginAsync()
        {
            if (SelectedEnginePlugin == null || SelectedEngine == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Disabling engine plugin {SelectedEnginePlugin.Name}...";

                // Check if plugin is critical
                if (await _engineService.IsEnginePluginCriticalAsync(SelectedEnginePlugin.Id))
                {
                    StatusMessage = $"Cannot enable/disable critical engine plugin: {SelectedEnginePlugin.Name}";
                    return;
                }

                // Check permissions
                if (!await _engineService.HasEngineModifyPermissionsAsync(SelectedEngine))
                {
                    StatusMessage = "Insufficient permissions to modify engine plugins. Run as administrator.";
                    return;
                }

                var success = await _engineService.SetEnginePluginDefaultStateAsync(SelectedEngine, SelectedEnginePlugin.Id, false);
                
                if (success)
                {
                    StatusMessage = $"Successfully disabled engine plugin {SelectedEnginePlugin.Name}";
                    await LoadPluginsForEngineAsync(); // Refresh list
                }
                else
                {
                    StatusMessage = $"Failed to disable engine plugin {SelectedEnginePlugin.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error disabling engine plugin: {ex.Message}";
                _logger.LogError(ex, $"Failed to disable engine plugin {SelectedEnginePlugin.Name}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task AddProjectAsync()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Unreal Project File",
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter { Name = "Unreal Project Files", Extensions = new List<string> { "uproject" } }
                    },
                    AllowMultiple = false
                };

                var window = new Window();
                var result = await openFileDialog.ShowAsync(window);
                if (result != null && result.Length > 0)
                {
                    var projectPath = result[0];
                    StatusMessage = $"Loading project from {projectPath}...";
                    
                    var project = await _projectService.CreateProjectEntryAsync(projectPath);
                    if (project != null)
                    {
                        if (!Projects.Any(p => p.ProjectFilePath == project.ProjectFilePath))
                        {
                            Projects.Add(project);
                            StatusMessage = $"Successfully added project: {project.Name}";
                            _logger.LogInformation($"Added project: {project.Name} at {project.Path}");
                        }
                        else
                        {
                            StatusMessage = $"Project {project.Name} already exists";
                        }
                    }
                    else
                    {
                        StatusMessage = "Failed to load project file";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding project: {ex.Message}";
                _logger.LogError(ex, "Failed to add project");
            }
        }
    }
}
