using System.Collections.Generic;
using System.Threading.Tasks;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public interface IUnrealEngineService
    {
        Task<List<UnrealEngine>> DetectEnginesAsync();
        Task<UnrealEngine?> GetEngineByVersionAsync(string version);
        Task<bool> ValidateEnginePathAsync(string path);
        Task<string> GetEngineVersionFromPathAsync(string path);
        
        // Engine plugin management
        Task<List<Plugin>> GetEnginePluginsAsync(UnrealEngine engine);
        Task<bool> SetEnginePluginDefaultStateAsync(UnrealEngine engine, string pluginId, bool enabled);
        Task<bool> IsEnginePluginCriticalAsync(string pluginId);
        Task<bool> HasEngineModifyPermissionsAsync(UnrealEngine engine);
    }
}
