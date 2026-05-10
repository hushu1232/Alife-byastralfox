using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Browser;
using Alife.Function.Interpreter;

namespace Alife.Implement.Function;

[Plugin("网上冲浪", "让 AI 像人一样操控浏览器：打开网页、观察页面、点击、打字、滚动、执行脚本。")]
[Description(@"你拥有一个真实的浏览器窗口，可以借此进行网上冲浪，从而每天学点新知识，找点新话题。
提示：若遇到验证或登录，一定要尝试请求主人协助，不然总被反爬就没意义了。此外优先使用搜索引擎（谷歌 > 必应 > 百度）来明确需求，再行动。")]
public class SurfingService(FunctionService functionService)
    : InteractivePlugin<SurfingService>, IDisposable
{
    readonly BrowserEngine browser = new();

    [XmlFunction("navigate")]
    [Description("打开网页。")]
    public async Task Navigate(XmlExecutorContext context, string url)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        await browser.NavigateAsync(url);
        Poke($"[Navigate] 已打开: {url}（接下来可以使用 observe 来查看页面内容）");
    }


    [XmlFunction("observe")]
    [Description(
        "查看页面内容（注意！内容过多时会被分页，所以当你没看到想要的内容时，可以尝试用scope翻页。此外该功能还会自动为可交互元素分配[ID]，借此可用`document.querySelector(\"[data-alife-id='ID']\")`定位交互）")]
    public async Task Observe(XmlExecutorContext context, [Description("观察的页面区域，从1开始")] int scope)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");
        if (scope == 0)
            throw new Exception("必须提供要观察的页面区域：scope");

        string result = await browser.ObserveAsync(scope);
        Poke($"（注意：如果 PAGING 支持翻页，说明页面显示不全，没有所需内容时可用 scope 翻页查看。此外若遇到人机验证或登录，可请求主人协助）：\n{result}");
    }

    [XmlFunction("runjs")]
    [Description("在浏览器中执行JS表达式。")]
    public async Task ExecuteScript(XmlExecutorContext context, [XmlContent] string script = "")
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string code = context.FullContent.Trim();
        string result = await browser.ExecuteScriptAsync(code);
        Poke($"[RunJS] 执行结果：\n{result}");
    }


    [XmlFunction("download")]
    [Description("下载文件到本地。")]
    public async Task Download(XmlExecutorContext context,
        [Description("下载链接")] string url,
        [Description("本地绝对路径")] string path)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        await BrowserEngine.DownloadFileAsync(url, path);
        Poke($"[Download] 文件已下载至：{path}");
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionService.RegisterHandler(this, "runjs");
    }

    public void Dispose() => browser.Dispose();
}