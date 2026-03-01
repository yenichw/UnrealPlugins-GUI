using System;

namespace UnrealPluginsGUI.Models
{
    public class PluginLibraryEntry
    {
        public string PluginId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string EngineVersion { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; }
        public string ArchivePath { get; set; } = string.Empty;
    }
}
