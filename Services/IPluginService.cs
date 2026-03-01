using System.Collections.Generic;
using System.Threading.Tasks;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public enum PluginSortCriteria
    {
        Name,
        Version,
        Type,
        EngineVersion,
        LastModified
    }

    public interface IPluginService
    {
        Task<List<Plugin>> GetEnginePluginsAsync(UnrealEngine engine);
        Task<List<Plugin>> GetProjectPluginsAsync(UnrealProject project);
        Task<bool> EnablePluginAsync(Plugin plugin, UnrealProject project);
        Task<bool> DisablePluginAsync(Plugin plugin, UnrealProject project);
        Task<bool> IsPluginCompatibleAsync(Plugin plugin, string engineVersion);
        Task<List<Plugin>> SortPluginsAsync(List<Plugin> plugins, PluginSortCriteria criteria);
    }
}
