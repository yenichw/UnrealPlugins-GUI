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
    }
}
