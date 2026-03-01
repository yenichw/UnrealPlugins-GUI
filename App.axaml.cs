using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using UnrealPluginsGUI.Services;
using UnrealPluginsGUI.ViewModels;
using UnrealPluginsGUI.Views;

namespace UnrealPluginsGUI
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Setup dependency injection
                var services = new ServiceCollection();
                ConfigureServices(services);
                var serviceProvider = services.BuildServiceProvider();
                
                desktop.MainWindow = new MainWindow
                {
                    DataContext = serviceProvider.GetRequiredService<MainViewModel>()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
        
        private static void ConfigureServices(IServiceCollection services)
        {
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddSerilog();
            });

            // Add services
            services.AddSingleton<IUnrealEngineService, UnrealEngineService>();
            services.AddSingleton<IPluginService, PluginService>();
            services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IArchiveService, ArchiveService>();
            services.AddSingleton<IPluginLibraryService, PluginLibraryService>();

            // Add ViewModels
            services.AddTransient<MainViewModel>();
        }
    }
}
