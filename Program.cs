using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using static Serilog.RollingInterval;
using UnrealPluginsGUI.Services;
using UnrealPluginsGUI.ViewModels;
using UnrealPluginsGUI.Views;

namespace UnrealPluginsGUI
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Configure logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("logs/unrealplugins-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                // Setup dependency injection
                var services = new ServiceCollection();
                ConfigureServices(services);
                var serviceProvider = services.BuildServiceProvider();

                // Create and configure the main application
                var app = BuildAvaloniaApp()
                    .UseReactiveUI();

                app.StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();

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
