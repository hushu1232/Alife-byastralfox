using System.ComponentModel;
using System.Text.Json;
using Alife.Framework;
using Alife.Function.Browser;
using Alife.Function.Interpreter;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Implement.Function;

[Plugin("网上冲浪", "让 AI 像人一样操控浏览器：打开网页、观察页面、点击、打字、滚动、执行脚本。")]
[Description(@"你拥有一个属于自己的、真实的、用户可见的浏览器窗口，你可以像真人一样去操作它。
注意：
1. 如果你遇到了需要验证、登录之类的页面，不要直接放弃，可以尝试让主人进行协助。
2. 执行任务时，优先用搜索引擎调查关键字，明确用户需求无误后，再进行操作。
3. 使用搜索引擎时优先谷歌、必应、然后百度，或者也可以用一些其他好用的网站搜素。")]
public class SurfingService(FunctionService functionService)
    : InteractivePlugin<SurfingService>, IDisposable
{
    readonly BrowserEngine browser = new();

    [XmlFunction("navigate")]
    [Description("在浏览器中打开指定网址。成功后会自动返回页面观察结果，无需再次调用 observe。")]
    public async Task Navigate(XmlExecutorContext context,
        [Description("要打开的网址")] string url)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        var result = await browser.NavigateAsync(url);
        if (result.Success)
        {
            string observation = await browser.ObserveAsync();
            Poke($"[Navigate] 已打开: {url}\n[Auto-Observe] 页面内容：\n{observation}");
        }
        else
        {
            Poke($"[Navigate] 加载失败 (HTTP {result.StatusCode})");
        }
    }


    [XmlFunction("observe")]
    [Description("观察当前页面：返回标题、URL、正文以及带有 ID 的交互元素。提示：若要点击按钮、填入文本或进行复杂操作，请直接使用 runjs 工具对选中的 [data-alife-id='xx'] 节点编写脚本。")]
    public async Task Observe(XmlExecutorContext context,
        [Description("观察区域索引（用于翻页），从 1 开始，默认 1")] int scope = 1)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        string result = await browser.ObserveAsync(scope);
        Poke($"[Observe] 页面状态：\n{result}");
    }

    [XmlFunction("runjs")]
    [Description("在浏览器中执行 JS。提示：配合 observe 返回的 [data-alife-id='x'] 属性，你可以通过 document.querySelector 精准定位并填值或点击。建议使用自闭合标签调用并把代码写在标签内容里。")]
    public async Task ExecuteScript(XmlExecutorContext context, [XmlContent] string script = "")
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string code = context.FullContent.Trim();
        // 使用自执行函数包裹，确保能捕获返回值
        string wrappedCode = JsonSerializer.Serialize(code);
        string safeScript = $@"
        (function() {{
            try {{
                let r = eval({wrappedCode});
                if (r instanceof Promise) return r.then(v => JSON.stringify(v));
                return JSON.stringify(r === undefined ? '(null/undefined)' : r);
            }} catch (e) {{
                return 'ERROR: ' + e.toString();
            }}
        }})()";

        string result = await browser.ExecuteScriptAsync(safeScript);
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