using System.Collections.Generic;
using System.Threading.Tasks;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public interface IArchiveService
    {
        Task<bool> ExtractPluginAsync(string archivePath, string destinationPath);
        Task<Plugin?> ParsePluginFromArchiveAsync(string archivePath);
        Task<bool> ValidatePluginArchiveAsync(string archivePath);
        Task<List<string>> GetSupportedFormatsAsync();
    }
}
