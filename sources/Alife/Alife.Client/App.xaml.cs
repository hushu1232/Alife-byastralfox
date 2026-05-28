using System.Reflection;
using System.Text;
using Alife.Platform;
using Microsoft.Extensions.DependencyInjection;
using Alife.Framework;
using Alife.Components.Services;
using Microsoft.Extensions.Logging;

namespace Alife;

public partial class App
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        ServiceCollection services = new();
        // 基础 Blazor Desktop 支持
        services.AddWpfBlazorWebView();
        services.AddBlazorWebViewDeveloperTools();// 允许 F12
        // UI 库
        services.AddAntDesign();
        // logger 库
        services.AddLogging(builder => {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        // Alife.Client 核心业务系统
        services.AddSingleton<StorageSystem>();
        services.AddSingleton<ConfigurationSystem>();
        services.AddSingleton<PluginSystem>();
        services.AddSingleton<CharacterSystem>();
        services.AddSingleton<ChatActivitySystem>();
        // 添加主窗口本身到容器，以便以后注入
        services.AddSingleton<ActivityNotifyService>();
        services.AddSingleton<ChatMessageService>();
        services.AddSingleton<MainWindow>();

        ServiceProvider = services.BuildServiceProvider();
        ServiceProvider.GetRequiredService<ChatMessageService>();
        ServiceProvider.GetRequiredService<MainWindow>().Show();
    }
}
