using System.Collections.Generic;
using System.Threading.Tasks;
using UnrealPluginsGUI.Models;

namespace UnrealPluginsGUI.Services
{
    public interface IProjectService
    {
        Task<List<UnrealProject>> DetectProjectsAsync();
        Task<UnrealProject?> LoadProjectAsync(string projectPath);
        Task<bool> InstallPluginToProjectAsync(Plugin plugin, UnrealProject project);
        Task<bool> RemovePluginFromProjectAsync(Plugin plugin, UnrealProject project);
        Task<UnrealProject?> CreateProjectEntryAsync(string uprojectFilePath);
    }
}
