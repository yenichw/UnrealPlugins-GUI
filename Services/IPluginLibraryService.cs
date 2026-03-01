using System.Collections.Generic;
using System.Threading.Tasks;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public interface IPluginLibraryService
    {
        Task<List<PluginLibraryEntry>> GetEntriesAsync();
        Task<PluginLibraryEntry?> ImportArchiveAsync(string archivePath);
        Task<bool> RemoveEntryAsync(PluginLibraryEntry entry);
    }
}
