using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public class PluginLibraryService : IPluginLibraryService
    {
        private readonly ILogger<PluginLibraryService> _logger;
        private readonly IArchiveService _archiveService;

        private readonly string _libraryRoot;
        private readonly string _catalogPath;

        public PluginLibraryService(ILogger<PluginLibraryService> logger, IArchiveService archiveService)
        {
            _logger = logger;
            _archiveService = archiveService;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _libraryRoot = Path.Combine(appData, "UnrealPluginsGUI", "PluginLibrary");
            Directory.CreateDirectory(_libraryRoot);

            _catalogPath = Path.Combine(_libraryRoot, "library.json");
        }

        public async Task<List<PluginLibraryEntry>> GetEntriesAsync()
        {
            var entries = await LoadCatalogAsync();
            return entries
                .OrderBy(e => e.PluginId, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => ParseVersionForSort(e.Version))
                .ToList();
        }

        public async Task<PluginLibraryEntry?> ImportArchiveAsync(string archivePath)
        {
            try
            {
                if (!File.Exists(archivePath))
                    return null;

                var isValid = await _archiveService.ValidatePluginArchiveAsync(archivePath);
                if (!isValid)
                    return null;

                var plugin = await _archiveService.ParsePluginFromArchiveAsync(archivePath);
                if (plugin == null)
                    return null;

                var pluginId = string.IsNullOrWhiteSpace(plugin.Id) ? plugin.Name : plugin.Id;
                var version = string.IsNullOrWhiteSpace(plugin.Version) ? "0.0.0" : plugin.Version;

                var safePluginId = SanitizePathSegment(pluginId);
                var safeVersion = SanitizePathSegment(version);

                var targetDir = Path.Combine(_libraryRoot, safePluginId, safeVersion);
                Directory.CreateDirectory(targetDir);

                var fileName = Path.GetFileName(archivePath);
                var targetArchivePath = Path.Combine(targetDir, fileName);
                File.Copy(archivePath, targetArchivePath, true);

                var entry = new PluginLibraryEntry
                {
                    PluginId = pluginId,
                    Name = plugin.Name,
                    Version = version,
                    EngineVersion = plugin.EngineVersion,
                    Description = plugin.Description,
                    Author = plugin.Author,
                    AddedAt = DateTime.UtcNow,
                    ArchivePath = targetArchivePath
                };

                var catalog = await LoadCatalogAsync();

                // Replace same pluginId+version if exists (keeps library clean)
                catalog.RemoveAll(e =>
                    string.Equals(e.PluginId, entry.PluginId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Version, entry.Version, StringComparison.OrdinalIgnoreCase));

                catalog.Add(entry);
                await SaveCatalogAsync(catalog);

                _logger.LogInformation($"Imported plugin to library: {entry.PluginId} {entry.Version}");
                return entry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to import archive to plugin library: {archivePath}");
                return null;
            }
        }

        public async Task<bool> RemoveEntryAsync(PluginLibraryEntry entry)
        {
            try
            {
                var catalog = await LoadCatalogAsync();
                var removed = catalog.RemoveAll(e =>
                    string.Equals(e.PluginId, entry.PluginId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Version, entry.Version, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.ArchivePath, entry.ArchivePath, StringComparison.OrdinalIgnoreCase)) > 0;

                if (removed)
                    await SaveCatalogAsync(catalog);

                if (!string.IsNullOrWhiteSpace(entry.ArchivePath) && File.Exists(entry.ArchivePath))
                {
                    File.Delete(entry.ArchivePath);

                    var dir = Path.GetDirectoryName(entry.ArchivePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir, true);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to remove plugin library entry: {entry.PluginId} {entry.Version}");
                return false;
            }
        }

        private async Task<List<PluginLibraryEntry>> LoadCatalogAsync()
        {
            try
            {
                if (!File.Exists(_catalogPath))
                    return new List<PluginLibraryEntry>();

                var json = await File.ReadAllTextAsync(_catalogPath);
                return JsonSerializer.Deserialize<List<PluginLibraryEntry>>(json) ?? new List<PluginLibraryEntry>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load plugin library catalog: {ex.Message}");
                return new List<PluginLibraryEntry>();
            }
        }

        private async Task SaveCatalogAsync(List<PluginLibraryEntry> entries)
        {
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_catalogPath, json);
        }

        private static string SanitizePathSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static Version ParseVersionForSort(string version)
        {
            // Best-effort: extracts numeric parts like 1.2.3; falls back to 0.0.0
            var cleaned = new string(version.Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
            if (Version.TryParse(cleaned, out var v))
                return v;
            return new Version(0, 0, 0);
        }
    }
}
