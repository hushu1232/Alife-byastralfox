using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Alife.Framework;
using Alife.Implement;
using Alife.Services;
using Microsoft.Extensions.Logging;

namespace Alife;

public partial class App : System.Windows.Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // 官方插件加载 (模仿 Program.cs)
        try { Assembly.Load("Alife.Implement"); } catch { }

        var services = new ServiceCollection();

        // 基础 Blazor Desktop 支持
        services.AddWpfBlazorWebView();
        services.AddBlazorWebViewDeveloperTools(); // 允许 F12

        // UI 库
        services.AddAntDesign();

        // Alife 核心业务系统
        services.AddSingleton<StorageSystem>();
        services.AddSingleton<ConfigurationSystem>();
        services.AddSingleton<PluginSystem>();
        services.AddSingleton<CharacterSystem>();
        services.AddSingleton<ChatActivitySystem>();
        services.AddSingleton<TutorialService>();
        
        // 添加主窗口本身到容器，以便以后注入
        services.AddSingleton<MainWindow>();

        ServiceProvider = services.BuildServiceProvider();

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
