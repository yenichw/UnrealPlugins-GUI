using System;

namespace UnrealPluginsGUI.Models
{
    public class Plugin
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string EngineVersion { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public PluginType Type { get; set; } = PluginType.Installed;
        public bool IsEnabled { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime LastModified { get; set; } = DateTime.Now;
        public string? ArchivePath { get; set; } // For plugins installed from archives

        public override string ToString()
        {
            return $"{Name} v{Version} (Engine {EngineVersion})";
        }
    }

    public enum PluginType
    {
        System,
        Installed,
        Archive
    }
}
