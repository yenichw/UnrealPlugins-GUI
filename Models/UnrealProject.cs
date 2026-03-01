using System;
using System.Collections.Generic;

namespace UnrealPluginsGUI.Models
{
    public class UnrealProject
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ProjectFilePath { get; set; } = string.Empty;
        public string EngineVersion { get; set; } = string.Empty;
        public string EngineAssociation { get; set; } = string.Empty;
        public List<Plugin> InstalledPlugins { get; set; } = new();
        public DateTime LastAccessed { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"{Name} (Engine {EngineVersion})";
        }
    }
}
