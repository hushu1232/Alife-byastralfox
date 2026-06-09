using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Alife.Framework;
using Alife.Components.Services;
using Alife.Platform;
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

#if DEBUG
        LogLoadedAssembly(typeof(Function.Memory.MemoryService).Assembly);
        LogLoadedAssembly(typeof(Function.MessageFilter.MessageFilterService).Assembly);
        LogLoadedAssembly(typeof(Function.SystemEvent.SystemEventService).Assembly);
        LogLoadedAssembly(typeof(Function.VirtualWorld.VirtualWorldService).Assembly);
        LogLoadedAssembly(typeof(Function.Developer.DeveloperService).Assembly);

        LogLoadedAssembly(typeof(Function.FunctionCaller.XmlFunctionCaller).Assembly);
        LogLoadedAssembly(typeof(Function.Mcp.McpService).Assembly);
        LogLoadedAssembly(typeof(Function.Skill.SkillService).Assembly);

        LogLoadedAssembly(typeof(Function.Browser.BrowserService).Assembly);
        LogLoadedAssembly(typeof(Function.Python.PythonService).Assembly);
        LogLoadedAssembly(typeof(Function.Vision.VisionService).Assembly);

        LogLoadedAssembly(typeof(Function.Speech.AuditoryService).Assembly);
        LogLoadedAssembly(typeof(Function.DeskPet.DeskPetService).Assembly);
        LogLoadedAssembly(typeof(Function.QChat.QChatService).Assembly);
        LogLoadedAssembly(typeof(Function.Speech.SpeechService).Assembly);

        LogLoadedAssembly(typeof(Function.Speech.IAuditoryModel).Assembly);
        LogLoadedAssembly(typeof(Function.Speech.ISpeechModel).Assembly);
        LogLoadedAssembly(typeof(Function.Vision.IVisionModel).Assembly);
#endif

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
        services.AddSingleton<ModuleSystem>();
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

#if DEBUG
    static void LogLoadedAssembly(System.Reflection.Assembly assembly)
    {
        AlifeTerminal.LogInfo(assembly.ToString());
    }
#endif
}
