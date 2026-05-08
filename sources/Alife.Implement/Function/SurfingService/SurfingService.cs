using System.ComponentModel;
using System.Text.Json;
using Alife.Framework;
using Alife.Function.Browser;
using Alife.Function.Interpreter;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Implement.Function;

[Plugin("网上冲浪", "让 AI 像人一样操控浏览器：打开网页、观察页面、点击、打字、滚动、执行脚本。")]
[Description(@"你拥有一个真实的浏览器窗口。你可以通过 observe 或 navigate 获取由系统动态分配了 [ID] 的组件。
操作提示：在使用 runjs 时，请务必使用属性选择器 `[data-alife-id='ID']` 来精准定位并操控这些组件。
注意：
1. 若遇到验证或登录，可请求主人协助。
2. 优先使用搜索引擎（谷歌 > 必应 > 百度）明确需求。")]
public class SurfingService(FunctionService functionService)
    : InteractivePlugin<SurfingService>, IDisposable
{
    readonly BrowserEngine browser = new();

    [XmlFunction("navigate")]
    [Description("跳转到指定网址。")]
    public async Task Navigate(XmlExecutorContext context, string url)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        await browser.NavigateAsync(url);
        Poke($"[Navigate] 已打开: {url}（接下来可以使用 observe 来查看页面内容）");
    }


    [XmlFunction("observe")]
    [Description(
        "观察当前页面：返回标题、正文及交互组件（会自动为页面组件分配[ID]，可用`document.querySelector(\"[data-alife-id='ID']\")`定位）。注意：若当前页（PAGING）没看到目标，说明组件被分页了，必须增加 scope 索引（如 observe(scope=2)）来翻页查看")]
    public async Task Observe(XmlExecutorContext context, [Description("观察的页面区域，从1开始")] int scope)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");
        if (scope == 0)
            throw new Exception("必须提供要观察的页面区域：scope");

        string result = await browser.ObserveAsync(scope);
        Poke($"[Observe] 页面状态 (提示：若遇到人机验证或登录，可请求主人协助)：\n{result}");
    }

    [XmlFunction("runjs")]
    [Description("在浏览器中执行JS表达式。")]
    public async Task ExecuteScript(XmlExecutorContext context, [XmlContent] string script = "")
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string code = context.FullContent.Trim();
        string serializedCode = JsonSerializer.Serialize(code);

        // 智能双模执行：支持控制台日志捕获、eval 表达式和 return 语句。
        string safeScript = "(function() {\n" +
                            "    let logs = [];\n" +
                            "    let oldLog = console.log;\n" +
                            "    console.log = function() { logs.push(Array.from(arguments).map(v => typeof v === 'object' ? JSON.stringify(v) : v).join(' ')); };\n" +
                            "    try {\n" +
                            "        let r;\n" +
                            "        try {\n" +
                            "            r = eval(" + serializedCode + ");\n" +
                            "        } catch (e) {\n" +
                            "            r = (function() {\n" +
                            "                " + code + "\n" +
                            "            })();\n" +
                            "        }\n" +
                            "        let res = '[Status] Success' + (r === undefined ? ' (No return value)' : '');\n" +
                            "        if (r !== undefined) res += '\\n[Return] ' + (typeof r === 'string' ? r : JSON.stringify(r, null, 2));\n" +
                            "        if (logs.length > 0) res += '\\n[Console Log]\\n' + logs.join('\\n');\n" +
                            "        return res;\n" +
                            "    } catch (e) {\n" +
                            "        return '[Status] Error: ' + e.toString();\n" +
                            "    } finally {\n" +
                            "        console.log = oldLog;\n" +
                            "    }\n" +
                            "})()";

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