using System;
using System.Windows;
using Grimlo.Chat;
using Grimlo.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Grimlo;

/// <summary>
/// Application entry point with central Dependency Injection container.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Resolve MainWindow with all injected dependencies
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Central HttpClientFactory configuration (handles sockets and DNS properly)
        services.AddHttpClient("GrimloChat", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Settings and secure credential storage
        services.AddSingleton<ISettingsService, SettingsService>();

        // Chat services
        services.AddSingleton<IGrimloChatService, GrimloChatService>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<HelpWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
