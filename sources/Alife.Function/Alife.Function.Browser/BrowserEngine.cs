using Alife.Basic;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace Alife.Function.Browser;

/// <summary>
/// AI 可控浏览器引擎。
/// 只提供最基本的浏览器操控原语：导航、截图、点击、打字、滚动。
/// AI 通过「看截图 → 决定操作 → 看截图」的循环来完成一切任务。
/// </summary>
public class BrowserEngine : IDisposable
{
    readonly WebViewWorker worker = new();

    /// <summary>
    /// 导航到指定 URL 并等待页面加载完成且稳定
    /// </summary>
    public async Task<(bool Success, int StatusCode)> NavigateAsync(string url)
    {
        var navResult = await worker.EnqueueAsync(async webView =>
        {
            TaskCompletionSource<(bool, int)> tcs = new();
            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                => tcs.TrySetResult((e.IsSuccess, e.HttpStatusCode));

            webView.CoreWebView2.NavigationCompleted += Handler;
            webView.Source = new Uri(url);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            cts.Token.Register(() => tcs.TrySetResult((true, 200)));

            var result = await tcs.Task;
            webView.CoreWebView2.NavigationCompleted -= Handler;
            return result;
        });

        await WaitUntilStableAsync();
        return navResult;
    }

    /// <summary>
    /// 内部方法：等待页面加载完成并保持稳定
    /// </summary>
    /// <param name="expectNavFromUrl">如果不为 null，则会等待直到 URL 偏离此值</param>
    private async Task WaitUntilStableAsync(string? expectNavFromUrl = null)
    {
        await worker.EnqueueAsync(async webView =>
        {
            // 1. 如果指定了旧 URL，先等待 URL 发生变化（或 3 秒超时）
            if (expectNavFromUrl != null)
            {
                for (int i = 0; i < 15; i++)
                {
                    string currentUrl = await webView.CoreWebView2.ExecuteScriptAsync("location.href");
                    if (JsonSerializer.Deserialize<string>(currentUrl) != expectNavFromUrl) break;
                    await Task.Delay(200);
                }
            }

            // 2. 等待 ReadyState
            for (int i = 0; i < 40; i++)
            {
                string state = await webView.CoreWebView2.ExecuteScriptAsync("document.readyState");
                if (state == "\"complete\"" || state == "\"interactive\"") break;
                await Task.Delay(250);
            }

            // 3. 智能等待：探测 DOM 树是否稳定
            int stableCount = 0;
            int lastLen = -1;
            for (int i = 0; i < 20; i++)
            {
                string lenStr = await webView.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerHTML.length.toString() : '0'");
                int currentLen = int.TryParse(lenStr.Trim('"'), out var len) ? len : 0;
                
                if (currentLen > 200 || i > 10)
                {
                    if (currentLen == lastLen) stableCount++;
                    else stableCount = 0;

                    if (stableCount >= 3) break; 
                }

                lastLen = currentLen;
                await Task.Delay(300);
            }
            return true;
        });
    }

    /// <summary>
    /// 观察当前页面，返回精简的页面结构信息（支持逻辑区域分页）
    /// </summary>
    public async Task<string> ObserveAsync(int scope = 1)
    {
        if (scope < 1) scope = 1;

        return await ExecuteScriptAsync($@"
        (function() {{
            const scope = {scope};
            const TEXT_SIZE = 3000;
            const ELEMENT_SIZE = 60;

            const info = {{
                title: document.title,
                url: location.href,
                currentScope: scope,
                totalScopes: 1,
                text: '',
                interactiveElements: [],
                status: ''
            }};

            const fullText = document.body ? document.body.innerText : '';
            const textStart = (scope - 1) * TEXT_SIZE;
            info.text = fullText.substring(textStart, textStart + TEXT_SIZE);

            // 1. 获取基础交互元素
            const elements = Array.from(document.querySelectorAll('a, button, input, textarea, select, [role=button], [onclick]'));
            
            // 2. 深度扫描：找出所有 CSS 鼠标样式为 'pointer' (小手) 的隐式按钮
            const allNodes = document.body ? document.body.querySelectorAll('*') : [];
            for(let i = 0; i < allNodes.length; i++) {{
                let node = allNodes[i];
                if (elements.includes(node)) continue;
                if (['HTML', 'BODY', 'SCRIPT', 'STYLE'].includes(node.tagName)) continue;

                let style = window.getComputedStyle(node);
                if (style && style.cursor === 'pointer') {{
                    elements.push(node);
                }}
            }}

            const processedElements = [];
            let index = 0;
            elements.forEach((el) => {{
                if (el.offsetWidth === 0 || el.offsetHeight === 0) return;

                let id = el.getAttribute('data-alife-id');
                if (!id) {{
                    id = (++index).toString();
                    el.setAttribute('data-alife-id', id);
                }}
                const selector = '[data-alife-id=""' + id + '""]';
                
                const text = (el.innerText || el.value || el.placeholder || el.title || el.alt || '').substring(0, 60).trim();
                const type = el.type || el.tagName.toLowerCase();
                const href = el.href || '';
                
                if (!text && type !== 'input' && type !== 'img' && type !== 'button') return;

                processedElements.push({{ selector, type, text, href: href.substring(0, 150) }});
            }});
            
            const elementStart = (scope - 1) * ELEMENT_SIZE;
            info.interactiveElements = processedElements.slice(elementStart, elementStart + ELEMENT_SIZE);
            
            // 计算总区域数
            const textScopes = Math.ceil(fullText.length / TEXT_SIZE);
            const elementScopes = Math.ceil(processedElements.length / ELEMENT_SIZE);
            info.totalScopes = Math.max(textScopes, elementScopes, 1);
            
            info.status = `Displaying scope ${{scope}} of ${{info.totalScopes}} (Text length: ${{fullText.length}}, Total elements: ${{processedElements.length}})`;

            return JSON.stringify(info, null, 2);
        }})()");
    }

    /// <summary>
    /// 点击页面上匹配 CSS 选择器的元素
    /// </summary>
    public async Task<string> ClickAsync(string selector)
    {
        string oldUrl = await ExecuteScriptAsync("location.href");
        
        string result = await ExecuteScriptAsync($@"
        (function() {{
            try {{
                const el = document.querySelector('{selector.Replace("'", "\\'")}');
                if (!el) return 'ERROR: 未找到匹配元素: {selector}';
                el.scrollIntoView({{block:'center'}});
                const text = (el.innerText || el.tagName).substring(0, 50);
                
                // 1. 自动处理链接跳转（优先处理）
                let current = el;
                let link = null;
                while (current && current !== document.body) {{
                    if (current.tagName.toLowerCase() === 'a' && current.href && current.href.startsWith('http')) {{
                        link = current;
                        break;
                    }}
                    current = current.parentElement;
                }}
                
                if (link) {{
                    window.location.href = link.href;
                    return 'SUCCESS: 已跳转链接 ' + text;
                }}
                
                // 2. 全仿真点击序列 (解决 div/span 伪按钮不响应 click() 的问题)
                const events = ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'];
                events.forEach(type => {{
                    el.dispatchEvent(new MouseEvent(type, {{
                        view: window,
                        bubbles: true,
                        cancelable: true,
                        buttons: 1
                    }}));
                }});

                return 'SUCCESS: 已点击 ' + text;
            }} catch (e) {{
                return 'ERROR: JS异常 - ' + e.toString();
            }}
        }})()");

        if (result == null || !result.StartsWith("ERROR"))
        {
            await WaitUntilStableAsync(oldUrl);
        }
        
        return result ?? "SUCCESS: 点击操作已发出";
    }

    /// <summary>
    /// 在页面上匹配 CSS 选择器的输入框中输入文字
    /// </summary>
    public async Task<string> TypeAsync(string selector, string text, bool submit = false)
    {
        string oldUrl = await ExecuteScriptAsync("location.href");
        string escapedText = text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n");

        string result = await ExecuteScriptAsync($@"
        (function() {{
            const el = document.querySelector('{selector.Replace("'", "\\'")}');
            if (!el) return 'ERROR: 未找到输入框: {selector}';
            el.scrollIntoView({{block:'center'}});
            el.focus();
            
            // 1. 注入值
            const nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set
                                        || Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value')?.set;
            if (nativeInputValueSetter) {{
                nativeInputValueSetter.call(el, '{escapedText}');
            }} else {{
                el.value = '{escapedText}';
            }}
            
            el.dispatchEvent(new Event('input', {{bubbles:true}}));
            el.dispatchEvent(new Event('change', {{bubbles:true}}));
            
            // 3. 验证注入是否成功
            if (el.value !== '{escapedText}') {{
                return 'ERROR: 注入值失败，当前值仍为: ' + el.value;
            }}

            // 4. 提交或触发回车逻辑
            if ({submit.ToString().ToLower()} || true) {{
                const evParams = {{ key: 'Enter', keyCode: 13, code: 'Enter', which: 13, bubbles: true, cancelable: true }};
                el.dispatchEvent(new KeyboardEvent('keydown', evParams));
                el.dispatchEvent(new KeyboardEvent('keypress', evParams));
                
                if ({submit.ToString().ToLower()}) {{
                    const form = el.form;
                    if (form) {{
                        if (typeof form.requestSubmit === 'function') form.requestSubmit();
                        else form.submit();
                    }} else {{
                        el.dispatchEvent(new KeyboardEvent('keyup', evParams));
                    }}
                }}
            }}

            return '已输入: {escapedText}';
        }})()");

        if (submit || result == null)
        {
            await WaitUntilStableAsync(oldUrl);
        }
        
        return result ?? "SUCCESS: 输入操作已执行";
    }



    /// <summary>
    /// 在当前页面执行 JavaScript 并返回结果
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        string rawRes = await worker.EnqueueAsync(async webView =>
        {
            try
            {
                var res = await webView.CoreWebView2.ExecuteScriptAsync(script);
                // 仅用于调试底层通路
                // Console.WriteLine($"[WebView2 Raw] {res}");
                return res;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize("ERROR: JS 引擎异常 - " + ex.Message);
            }
        });

        if (string.IsNullOrEmpty(rawRes) || rawRes == "null")
        {
            return null;
        }

        // 尝试剥离 WebView2 返回的 JSON 字符串外壳
        // 由于 SurfingService 现在强制在 JS 侧执行 JSON.stringify，
        // 所以这里一定能拿到一个被双重序列化的字符串。
        if (rawRes.StartsWith("\"") && rawRes.EndsWith("\""))
        {
            try 
            {
                return JsonSerializer.Deserialize<string>(rawRes) ?? rawRes;
            }
            catch 
            {
                return rawRes.Trim('"');
            }
        }

        return rawRes;
    }


    /// <summary>
    /// 通过 HttpClient 下载文件到本地
    /// </summary>
    public static async Task DownloadFileAsync(string url, string savePath)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        var bytes = await client.GetByteArrayAsync(url);

        string? dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(savePath, bytes);
    }

    public void Dispose() => worker.Dispose();
}
