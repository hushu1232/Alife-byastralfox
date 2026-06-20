using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Browser;

[Module("网上冲浪", "让AI可以像人一样操控真实的浏览器，从而能够执行各种网页任务的同时，避免反爬。",
    defaultCategory: "Alife 官方/实用工具")]
[Description(@"你拥有一个独属于自己的真实浏览器，可借此进行网上冲浪，每天学点新知识，找点新话题。
提示：
1. 若遇到验证或登录，可以请求主人协助，从而避免被反爬。
2. 办事前先明确需求，再行动。
3. 优先使用搜索引擎`谷歌 > 必应 > 百度`")]
public class BrowserService(
    XmlFunctionCaller functionService,
    IBrowserRuntime? browserRuntime = null,
    ILifeEventPublisher? lifeEventPublisher = null)
    : InteractiveModule<BrowserService>, IDisposable, IEmbodiedCapability, IModuleHealthReporter
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("打开网页。")]
    public async Task Navigate(string url)
    {
        await browser.NavigateAsync(url);
        PublishLifeEvent($"You opened a browser page: {url}");
        Poke($"[Navigate] 已打开: {url}（接下来可以使用 observe 来查看页面内容）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看页面内容（注意！内容过多时会被分页，所以当你没看到想要的内容时，可以尝试用 page 翻页。此外该功能还会自动为可交互元素分配[ID]，借此可用`document.querySelector(\"[data-alife-id='ID']\")`定位交互）")]
    public async Task Observe([Description("观察的页面区域，从1开始")] int page)
    {
        string result = await browser.ObserveAsync(page);
        PublishLifeEvent($"You observed browser page segment {page}.");
        Poke(FormatObservedPageResult(page, result));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查询页面中指定ID元素的详细信息，例如 href、type、placeholder。ID 来自 observe 中的 [ID:文本] 标记。")]
    public async Task GetElementInfo([Description("元素的 data-alife-id")] int id)
    {
        string result = await browser.GetElementInfoAsync(id);
        PublishLifeEvent($"You inspected browser element {id}.");
        Poke(FormatElementInfoResult(id, result));
    }

    [XmlFunction(FunctionMode.Content, riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("执行JS表达式（这只能在浏览器沙盒中使用，不能执行全局性脚本操作）")]
    public async Task RunJs(XmlExecutorContext context, [XmlContent] string script)
    {
        if (context.CallMode == CallMode.Closing)
        {
            string code = context.FullContent.Trim();
            string result = await browser.ExecuteScriptAsync(code);
            PublishLifeEvent("You executed JavaScript in your browser.");
            Poke(FormatScriptResult(result));
        }
    }

    [XmlFunction(FunctionMode.OneShot, riskLevel: XmlFunctionRiskLevel.High, budgetCost: 4)]
    [Description("下载文件。")]
    public async Task Download([Description("下载链接")] string url, [Description("本地绝对路径")] string path)
    {
        await AlifePlatform.DownloadFileAsync(url, path);
        PublishLifeEvent($"You downloaded a browser file to {path}.");
        Poke($"[Download] 文件已下载至：{path}");
    }

    readonly IBrowserRuntime browser = browserRuntime ?? new BrowserEngine();

    public static string FormatObservedPageResult(int page, string result)
    {
        return $"""
                页面结果如下（注意！网站页面大多不能一次全显示，必须通过 page 翻页来查看完整内容。此外若遇到人机验证或登录，可请求主人协助）：
                {ExternalContextFormatter.WrapUntrusted($"browser-page-{page}", result)}
                """;
    }

    public static string FormatScriptResult(string result)
    {
        return $"""
                [RunJS] 执行结果：
                {ExternalContextFormatter.WrapUntrusted("browser-script-result", result)}
                """;
    }

    public static string FormatElementInfoResult(int id, string result)
    {
        return $"""
                [GetElementInfo] Element {id}:
                {ExternalContextFormatter.WrapUntrusted($"browser-element-{id}", result)}
                """;
    }

    public string Name => "Browser";
    public EmbodiedCapabilityKind Kind => EmbodiedCapabilityKind.Sense;
    public string SelfDescription => "Your real browser for opening pages, observing web content, and operating web interfaces when external information is needed.";
    public string? GetCurrentState() => "Initialized when this module awakes; use browser XML functions to open and observe pages.";
    public ModuleHealth GetHealth()
    {
        if (browserReady || browser.IsReady)
            return new ModuleHealth("Browser", ModuleHealthStatus.Healthy, "Browser runtime is initialized.");

        string summary = lastInitializationError == null
            ? "Browser runtime is not initialized yet."
            : $"Browser runtime did not initialize during module startup: {lastInitializationError.Message}";
        return new ModuleHealth("Browser", ModuleHealthStatus.Degraded, summary);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        try
        {
            await browser.WaitToLoadedAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            lastInitializationError = ex;
        }
        browserReady = browser.IsReady;
        functionService?.RegisterHandler(new XmlHandler(this), DocumentMode.Implicit, nameof(RunJs));
    }

    public void Dispose() => browser.Dispose();

    bool browserReady;
    Exception? lastInitializationError;

    void PublishLifeEvent(string summary)
    {
        lifeEventPublisher?.Publish(new LifeEvent(
            DateTimeOffset.Now,
            LifeEventKind.Browser,
            "Browser",
            summary));
    }
}
