using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public class ArchiveService : IArchiveService
    {
        private readonly ILogger<ArchiveService> _logger;
        private readonly string _pluginStoragePath;

        public ArchiveService(ILogger<ArchiveService> logger)
        {
            _logger = logger;
            _pluginStoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginStorage");
            
            // Ensure storage directory exists
            if (!Directory.Exists(_pluginStoragePath))
            {
                Directory.CreateDirectory(_pluginStoragePath);
            }
        }

        public async Task<bool> ExtractPluginAsync(string archivePath, string destinationPath)
        {
            try
            {
                _logger.LogInformation($"Extracting plugin from {archivePath} to {destinationPath}");

                // Ensure destination directory exists
                if (!Directory.Exists(destinationPath))
                {
                    Directory.CreateDirectory(destinationPath);
                }

                using var archive = ArchiveFactory.Open(archivePath);
                var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
                
                if (!entries.Any())
                {
                    _logger.LogWarning($"Archive {archivePath} contains no files");
                    return false;
                }

                // Check if archive has a single top-level folder
                var firstEntry = entries.First();
                var topLevelFolders = entries
                    .Select(e => GetTopLevelFolder(e.Key))
                    .Where(f => !string.IsNullOrEmpty(f))
                    .Distinct()
                    .ToList();

                bool shouldStripTopLevel = false;
                if (topLevelFolders.Count == 1)
                {
                    var topLevelFolder = topLevelFolders.First();
                    // Check if this looks like a plugin folder (contains .uplugin)
                    var hasUpluginInTopLevel = entries.Any(e => 
                        e.Key.StartsWith(topLevelFolder, StringComparison.OrdinalIgnoreCase) && 
                        e.Key.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase));
                    
                    if (hasUpluginInTopLevel)
                    {
                        shouldStripTopLevel = true;
                        _logger.LogInformation($"Archive contains single top-level plugin folder: {topLevelFolder}");
                    }
                }

                foreach (var entry in entries)
                {
                    var relativePath = entry.Key;
                    
                    // Strip top-level folder if detected
                    if (shouldStripTopLevel)
                    {
                        var topLevelFolder = topLevelFolders.First() + "/";
                        if (relativePath.StartsWith(topLevelFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = relativePath.Substring(topLevelFolder.Length);
                        }
                    }

                    // Skip empty paths
                    if (string.IsNullOrEmpty(relativePath))
                        continue;

                    var fullPath = Path.Combine(destinationPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var fullDir = Path.GetDirectoryName(fullPath);
                    
                    if (!string.IsNullOrEmpty(fullDir) && !Directory.Exists(fullDir))
                    {
                        Directory.CreateDirectory(fullDir);
                    }

                    await using var entryStream = entry.OpenEntryStream();
                    await using var fileStream = File.Create(fullPath);
                    await entryStream.CopyToAsync(fileStream);
                }

                _logger.LogInformation($"Successfully extracted plugin to {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to extract archive {archivePath}: {ex.Message}");
                return false;
            }
        }

        private string GetTopLevelFolder(string entryPath)
        {
            var parts = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? parts[0] : string.Empty;
        }

        public async Task<Plugin?> ParsePluginFromArchiveAsync(string archivePath)
        {
            try
            {
                _logger.LogInformation($"Parsing plugin from archive {archivePath}");

                var extension = Path.GetExtension(archivePath).ToLowerInvariant();
                if (extension == ".zip")
                {
                    try
                    {
                        using var zip = ZipFile.OpenRead(archivePath);
                        var entry = zip.Entries.FirstOrDefault(e =>
                            !string.IsNullOrEmpty(e.FullName) &&
                            e.FullName.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase));

                        if (entry != null)
                        {
                            await using var entryStream = entry.Open();
                            using var reader = new StreamReader(entryStream);
                            var content = await reader.ReadToEndAsync();

                            using var doc = JsonDocument.Parse(content);
                            var root = doc.RootElement;

                            var pluginId = Path.GetFileNameWithoutExtension(entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            var pluginName = pluginId;
                            if (root.TryGetProperty("FriendlyName", out var friendlyNameProp))
                                pluginName = friendlyNameProp.GetString() ?? pluginName;

                            var version = "1.0";
                            if (root.TryGetProperty("VersionName", out var versionNameProp))
                                version = versionNameProp.GetString() ?? version;
                            else if (root.TryGetProperty("Version", out var versionProp))
                            {
                                if (versionProp.ValueKind == JsonValueKind.Number)
                                    version = versionProp.GetInt32().ToString();
                                else if (versionProp.ValueKind == JsonValueKind.String)
                                    version = versionProp.GetString() ?? version;
                            }

                            var engineVersion = "Unknown";
                            if (root.TryGetProperty("EngineVersion", out var engineVersionProp))
                                engineVersion = engineVersionProp.GetString() ?? engineVersion;

                            var description = string.Empty;
                            if (root.TryGetProperty("Description", out var descriptionProp))
                                description = descriptionProp.GetString() ?? string.Empty;

                            var author = string.Empty;
                            if (root.TryGetProperty("CreatedBy", out var createdByProp))
                                author = createdByProp.GetString() ?? string.Empty;

                            return new Plugin
                            {
                                Id = pluginId,
                                Name = pluginName,
                                Version = version,
                                EngineVersion = engineVersion,
                                Path = Path.GetDirectoryName(entry.FullName)?.Replace('/', Path.DirectorySeparatorChar) ?? string.Empty,
                                Type = PluginType.Archive,
                                IsEnabled = true,
                                Description = description,
                                Author = author,
                                LastModified = File.GetLastWriteTime(archivePath),
                                ArchivePath = archivePath
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Fast zip parse failed for {archivePath}: {ex.Message}");
                    }
                }

                using var archive = ArchiveFactory.Open(archivePath);
                
                // Look for .uplugin file in the archive
                var upluginEntry = archive.Entries.FirstOrDefault(e => 
                    !e.IsDirectory && e.Key.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase));

                if (upluginEntry == null)
                {
                    _logger.LogWarning($"No .uplugin file found in archive {archivePath}");
                    return null;
                }

                // Extract the .uplugin file to a temporary location to read it
                var tempPath = Path.Combine(Path.GetTempPath(), "UnrealPluginsGUI", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempPath);

                try
                {
                    var tempUpluginPath = Path.Combine(tempPath, upluginEntry.Key);
                    upluginEntry.WriteToFile(tempUpluginPath);

                    var content = await File.ReadAllTextAsync(tempUpluginPath);
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;

                    var pluginId = Path.GetFileNameWithoutExtension(upluginEntry.Key);
                    var pluginName = pluginId;
                    if (root.TryGetProperty("FriendlyName", out var friendlyNameProp))
                    {
                        pluginName = friendlyNameProp.GetString() ?? pluginName;
                    }

                    var version = "1.0";
                    if (root.TryGetProperty("VersionName", out var versionNameProp))
                    {
                        version = versionNameProp.GetString() ?? version;
                    }
                    else if (root.TryGetProperty("Version", out var versionProp) && versionProp.ValueKind == JsonValueKind.Number)
                    {
                        version = versionProp.GetInt32().ToString();
                    }

                    var engineVersion = "Unknown";
                    if (root.TryGetProperty("EngineVersion", out var engineVersionProp))
                    {
                        engineVersion = engineVersionProp.GetString() ?? engineVersion;
                    }

                    var description = string.Empty;
                    if (root.TryGetProperty("Description", out var descriptionProp))
                    {
                        description = descriptionProp.GetString() ?? string.Empty;
                    }

                    var author = string.Empty;
                    if (root.TryGetProperty("CreatedBy", out var createdByProp))
                    {
                        author = createdByProp.GetString() ?? string.Empty;
                    }

                    // Try to determine the plugin directory structure
                    var pluginDir = FindPluginDirectoryInArchive(archive, pluginName);

                    return new Plugin
                    {
                        Id = pluginId,
                        Name = pluginName,
                        Version = version,
                        EngineVersion = engineVersion,
                        Path = pluginDir ?? string.Empty,
                        Type = PluginType.Archive,
                        IsEnabled = true,
                        Description = description,
                        Author = author,
                        LastModified = File.GetLastWriteTime(archivePath),
                        ArchivePath = archivePath
                    };
                }
                finally
                {
                    // Clean up temporary files
                    if (Directory.Exists(tempPath))
                    {
                        Directory.Delete(tempPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to parse plugin from archive {archivePath}: {ex.Message}");
            }

            return null;
        }

        public async Task<bool> ValidatePluginArchiveAsync(string archivePath)
        {
            try
            {
                if (!File.Exists(archivePath))
                    return false;

                var extension = Path.GetExtension(archivePath).ToLowerInvariant();
                var supportedFormats = await GetSupportedFormatsAsync();

                if (!supportedFormats.Contains(extension))
                {
                    _logger.LogWarning($"Unsupported archive format: {extension}");
                    return false;
                }

                // Try to open the archive to ensure it's valid
                using var archive = ArchiveFactory.Open(archivePath);
                
                // Check if it contains a .uplugin file
                var hasUpluginFile = archive.Entries.Any(e => 
                    !e.IsDirectory && e.Key.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase));

                if (!hasUpluginFile)
                {
                    _logger.LogWarning($"Archive {archivePath} does not contain a .uplugin file");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to validate archive {archivePath}: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> GetSupportedFormatsAsync()
        {
            return new List<string> { ".zip", ".rar", ".7z", ".tar", ".gz" };
        }

        public async Task<bool> StorePluginArchiveAsync(string archivePath, string pluginName)
        {
            try
            {
                var storagePath = Path.Combine(_pluginStoragePath, pluginName);
                Directory.CreateDirectory(storagePath);

                var fileName = Path.GetFileName(archivePath);
                var destinationPath = Path.Combine(storagePath, fileName);

                // Copy the archive to storage
                File.Copy(archivePath, destinationPath, true);

                _logger.LogInformation($"Stored plugin archive {fileName} in {storagePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to store plugin archive {archivePath}: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> GetStoredPluginArchivesAsync()
        {
            var archives = new List<string>();

            if (!Directory.Exists(_pluginStoragePath))
                return archives;

            try
            {
                var pluginDirectories = Directory.GetDirectories(_pluginStoragePath);
                foreach (var pluginDir in pluginDirectories)
                {
                    var files = Directory.GetFiles(pluginDir, "*.*", SearchOption.TopDirectoryOnly);
                    archives.AddRange(files);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to get stored plugin archives: {ex.Message}");
            }

            return archives;
        }

        private string? FindPluginDirectoryInArchive(IArchive archive, string pluginName)
        {
            // Try to find the main plugin directory by looking for common plugin structures
            var possiblePaths = new[]
            {
                $"{pluginName}/",
                $"{pluginName.Replace(" ", "")}/",
                $"Plugin/",
                $"Plugins/{pluginName}/",
                $"{pluginName}.{pluginName}/"
            };

            foreach (var path in possiblePaths)
            {
                var entry = archive.Entries.FirstOrDefault(e => 
                    e.Key.StartsWith(path, StringComparison.OrdinalIgnoreCase) && 
                    e.Key.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    return path.TrimEnd('/');
                }
            }

            // Fallback: return the directory containing the .uplugin file
            var upluginEntry = archive.Entries.FirstOrDefault(e => 
                !e.IsDirectory && e.Key.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase));

            if (upluginEntry != null)
            {
                return Path.GetDirectoryName(upluginEntry.Key)?.Replace('\\', '/');
            }

            return null;
        }
    }
}
