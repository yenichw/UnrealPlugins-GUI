using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public class VersionInfo
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
    }

    public class PluginService : IPluginService
    {
        private readonly ILogger<PluginService> _logger;

        public PluginService(ILogger<PluginService> logger)
        {
            _logger = logger;
        }

        public async Task<List<Plugin>> GetEnginePluginsAsync(UnrealEngine engine)
        {
            var plugins = new List<Plugin>();

            // Get system plugins (built-in)
            await GetSystemPluginsAsync(engine, plugins);

            // Get installed plugins
            await GetInstalledPluginsAsync(engine, plugins);

            return plugins;
        }

        public async Task<List<Plugin>> GetProjectPluginsAsync(UnrealProject project)
        {
            var plugins = new List<Plugin>();
            var pluginsPath = Path.Combine(project.Path, "Plugins");

            var enabledOverrides = await GetPluginEnabledOverridesAsync(project);

            if (!Directory.Exists(pluginsPath))
                return plugins;

            var pluginDirectories = Directory.GetDirectories(pluginsPath);
            foreach (var pluginDir in pluginDirectories)
            {
                try
                {
                    var plugin = await ParsePluginFromDirectoryAsync(pluginDir, PluginType.Installed);
                    if (plugin != null)
                    {
                        if (!string.IsNullOrWhiteSpace(plugin.Id) && enabledOverrides.TryGetValue(plugin.Id, out var enabledOverride))
                        {
                            plugin.IsEnabled = enabledOverride;
                        }
                        plugins.Add(plugin);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to parse plugin at {pluginDir}: {ex.Message}");
                }
            }

            return plugins;
        }

        public async Task<bool> EnablePluginAsync(Plugin plugin, UnrealProject project)
        {
            try
            {
                var pluginId = string.IsNullOrWhiteSpace(plugin.Id) ? plugin.Name : plugin.Id;
                var success = await SetPluginEnabledInProjectAsync(project, pluginId, true);
                if (!success)
                    return false;

                plugin.IsEnabled = true;
                _logger.LogInformation($"Enabled plugin {plugin.Name} for project {project.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to enable plugin {plugin.Name}: {ex.Message}");
            }

            return false;
        }

        private async Task<Dictionary<string, bool>> GetPluginEnabledOverridesAsync(UnrealProject project)
        {
            var overrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var uprojectPath = project.ProjectFilePath;
                if (string.IsNullOrWhiteSpace(uprojectPath) || !File.Exists(uprojectPath))
                    return overrides;

                var jsonText = await File.ReadAllTextAsync(uprojectPath);
                var root = JsonNode.Parse(jsonText) as JsonObject;
                var pluginsArray = root?["Plugins"] as JsonArray;

                if (pluginsArray == null)
                    return overrides;

                foreach (var node in pluginsArray)
                {
                    if (node is not JsonObject obj)
                        continue;

                    var name = obj["Name"]?.GetValue<string>();
                    var enabledNode = obj["Enabled"];

                    if (string.IsNullOrWhiteSpace(name) || enabledNode == null)
                        continue;

                    var enabled = enabledNode.GetValue<bool>();
                    overrides[name] = enabled;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to read plugin overrides from .uproject for {project.Name}: {ex.Message}");
            }

            return overrides;
        }

        private async Task<bool> SetPluginEnabledInProjectAsync(UnrealProject project, string pluginId, bool enabled)
        {
            try
            {
                var uprojectPath = project.ProjectFilePath;
                if (string.IsNullOrWhiteSpace(uprojectPath) || !File.Exists(uprojectPath))
                    return false;

                File.Copy(uprojectPath, uprojectPath + ".backup", true);

                var jsonText = await File.ReadAllTextAsync(uprojectPath);
                var root = JsonNode.Parse(jsonText) as JsonObject;
                if (root == null)
                    return false;

                var pluginsArray = root["Plugins"] as JsonArray;
                if (pluginsArray == null)
                {
                    pluginsArray = new JsonArray();
                    root["Plugins"] = pluginsArray;
                }

                var existing = pluginsArray
                    .OfType<JsonObject>()
                    .FirstOrDefault(p => string.Equals(p["Name"]?.GetValue<string>(), pluginId, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing["Enabled"] = enabled;
                }
                else
                {
                    pluginsArray.Add(new JsonObject
                    {
                        ["Name"] = pluginId,
                        ["Enabled"] = enabled
                    });
                }

                await File.WriteAllTextAsync(
                    uprojectPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to update .uproject plugin state for {project.Name}/{pluginId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DisablePluginAsync(Plugin plugin, UnrealProject project)
        {
            try
            {
                var pluginId = string.IsNullOrWhiteSpace(plugin.Id) ? plugin.Name : plugin.Id;
                var success = await SetPluginEnabledInProjectAsync(project, pluginId, false);
                if (!success)
                    return false;

                plugin.IsEnabled = false;
                _logger.LogInformation($"Disabled plugin {plugin.Name} for project {project.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to disable plugin {plugin.Name}: {ex.Message}");
            }

            return false;
        }

        public async Task<bool> IsPluginCompatibleAsync(Plugin plugin, string engineVersion)
        {
            // If plugin doesn't specify engine version, assume compatible
            if (string.IsNullOrEmpty(plugin.EngineVersion))
                return true;

            try
            {
                // Parse both versions for comparison
                var pluginVersion = ParseVersion(plugin.EngineVersion);
                var engineVersionParsed = ParseVersion(engineVersion);

                if (pluginVersion == null || engineVersionParsed == null)
                    return true; // Assume compatible if parsing fails

                // Major version must match
                if (pluginVersion.Major != engineVersionParsed.Major)
                {
                    _logger.LogWarning($"Plugin {plugin.Name} major version mismatch: plugin={pluginVersion.Major}, engine={engineVersionParsed.Major}");
                    return false;
                }

                // Minor version: allow plugin to be older or same, warn if newer
                if (pluginVersion.Minor > engineVersionParsed.Minor)
                {
                    _logger.LogWarning($"Plugin {plugin.Name} minor version newer than engine: plugin={pluginVersion.Minor}, engine={engineVersionParsed.Minor}");
                    return false; // Plugin requires newer engine version
                }

                // Same major and plugin minor <= engine minor = compatible
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error checking plugin compatibility: {ex.Message}");
                return true; // Assume compatible on error
            }
        }

        private VersionInfo? ParseVersion(string versionString)
        {
            if (string.IsNullOrEmpty(versionString))
                return null;

            // Handle formats like "5.3", "5.3.0", "5.3.2"
            var match = System.Text.RegularExpressions.Regex.Match(versionString, @"^(\d+)\.(\d+)(?:\.(\d+))?$");
            if (!match.Success)
                return null;

            var major = int.Parse(match.Groups[1].Value);
            var minor = int.Parse(match.Groups[2].Value);
            var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

            return new VersionInfo { Major = major, Minor = minor, Patch = patch };
        }

        public async Task<List<Plugin>> SortPluginsAsync(List<Plugin> plugins, Services.PluginSortCriteria criteria)
        {
            return criteria switch
            {
                Services.PluginSortCriteria.Name => plugins.OrderBy(p => p.Name).ToList(),
                Services.PluginSortCriteria.Version => plugins.OrderByDescending(p => p.Version).ToList(),
                Services.PluginSortCriteria.Type => plugins.OrderBy(p => p.Type).ToList(),
                Services.PluginSortCriteria.EngineVersion => plugins.OrderBy(p => p.EngineVersion).ToList(),
                Services.PluginSortCriteria.LastModified => plugins.OrderByDescending(p => p.LastModified).ToList(),
                _ => plugins.OrderBy(p => p.Name).ToList()
            };
        }

        private async Task GetSystemPluginsAsync(UnrealEngine engine, List<Plugin> plugins)
        {
            var systemPluginsPath = Path.Combine(engine.Path, "Engine", "Plugins");
            if (!Directory.Exists(systemPluginsPath))
                return;

            await ScanPluginDirectoryAsync(systemPluginsPath, PluginType.System, plugins);
        }

        private async Task GetInstalledPluginsAsync(UnrealEngine engine, List<Plugin> plugins)
        {
            var marketplacePluginsPath = Path.Combine(engine.Path, "Engine", "MarketplacePlugins");
            if (Directory.Exists(marketplacePluginsPath))
            {
                await ScanPluginDirectoryAsync(marketplacePluginsPath, PluginType.Installed, plugins);
            }
        }

        private async Task ScanPluginDirectoryAsync(string pluginsPath, PluginType type, List<Plugin> plugins)
        {
            var pluginDirectories = Directory.GetDirectories(pluginsPath, "*", SearchOption.AllDirectories);
            
            foreach (var pluginDir in pluginDirectories)
            {
                var upluginFile = Path.Combine(pluginDir, Path.GetFileName(pluginDir) + ".uplugin");
                if (File.Exists(upluginFile))
                {
                    try
                    {
                        var plugin = await ParsePluginFromDirectoryAsync(pluginDir, type);
                        if (plugin != null)
                        {
                            plugins.Add(plugin);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to parse plugin at {pluginDir}: {ex.Message}");
                    }
                }
            }
        }

        private async Task<Plugin?> ParsePluginFromDirectoryAsync(string pluginDir, PluginType type)
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

                var engineVersion = "Unknown";
                
                if (root.TryGetProperty("EngineVersion", out var engineVersionProp))
                {
                    engineVersion = engineVersionProp.GetString() ?? "Unknown";
                }

                // Check for compatible engine versions array
                if (root.TryGetProperty("CompatibleEngineVersions", out var compatibleVersionsProp))
                {
                    // Use the first compatible version if available
                    if (compatibleVersionsProp.ValueKind == JsonValueKind.Array && compatibleVersionsProp.GetArrayLength() > 0)
                    {
                        var firstCompatible = compatibleVersionsProp[0];
                        if (firstCompatible.TryGetProperty("Name", out var compatibleNameProp))
                        {
                            engineVersion = compatibleNameProp.GetString() ?? engineVersion;
                        }
                    }
                }

                var isEnabled = true;
                if (root.TryGetProperty("bEnabled", out var enabledProp))
                    isEnabled = enabledProp.ValueKind == JsonValueKind.True;

                return new Plugin
                {
                    Id = pluginId,
                    Name = pluginName,
                    Version = version,
                    EngineVersion = engineVersion,
                    Path = pluginDir,
                    Type = type,
                    IsEnabled = isEnabled,
                    Description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() ?? "" : "",
                    Author = root.TryGetProperty("CreatedBy", out var authorProp) ? authorProp.GetString() ?? "" : "",
                    LastModified = Directory.GetLastWriteTime(pluginDir)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to parse plugin descriptor for {pluginId}: {ex.Message}");
                return null;
            }
        }
    }
}
