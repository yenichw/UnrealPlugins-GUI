using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public class UnrealEngineService : IUnrealEngineService
    {
        private readonly ILogger<UnrealEngineService> _logger;

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
    }

    // Helper classes for JSON parsing
    internal class EpicManifest
    {
        public string? AppName { get; set; }
        public string InstallLocation { get; set; } = string.Empty;
    }
}
