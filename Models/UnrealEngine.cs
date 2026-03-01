using System;

namespace UnrealPluginsGUI.Models
{
    public class UnrealEngine
    {
        public string Path { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FullVersion { get; set; } = string.Empty;
        public bool IsInstalled { get; set; } = true;
        public DateTime LastDetected { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"Unreal Engine {Version} ({Path})";
        }
    }
}
