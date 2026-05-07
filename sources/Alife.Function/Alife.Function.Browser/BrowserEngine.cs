using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.Browser;

public class BrowserEngine : IDisposable
{
    readonly WebViewWorker worker = new();

    public async Task<NavigateResult> NavigateAsync(string url)
    {
        var tcs = new TaskCompletionSource<NavigateResult>();

        await worker.EnqueueAsync(async webView =>
        {
            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                tcs.SetResult(new NavigateResult { Success = e.IsSuccess, StatusCode = (int)e.WebErrorStatus });
            }

            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.Navigate(url);
            return true;
        });

        var result = await tcs.Task;
        if (result.Success)
        {
            await WaitUntilStableAsync();
        }
        return result;
    }

    /// <summary>
    /// 等待页面加载稳定（内容长度不再剧烈变化）
    /// </summary>
    public async Task WaitUntilStableAsync(string? oldUrl = null)
    {
        await worker.EnqueueAsync(async webView =>
        {
            int lastLen = -1;
            int stableCount = 0;

            for (int i = 0; i < 20; i++)
            {
                if (oldUrl != null)
                {
                    string currentUrl = await webView.CoreWebView2.ExecuteScriptAsync("location.href");
                    if (JsonSerializer.Deserialize<string>(currentUrl) != oldUrl) break;
                }

                int currentLen = (await webView.CoreWebView2.ExecuteScriptAsync("document.body.innerText.length"))?.Length ?? 0;
                
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
    /// 观察当前页面，返回精简的页面结构信息
    /// </summary>
    public async Task<string> ObserveAsync(int scope = 1)
    {
        if (scope < 1) scope = 1;

        return await ExecuteScriptAsync($@"
        (function() {{
            try {{
                const scope = {scope};
                const TEXT_SIZE = 3000;
                const ELEMENT_SIZE = 60;

                const info = {{
                    title: document.title,
                    url: location.href,
                    currentScope: scope,
                    totalScopes: 1,
                    text: '',
                    inputs: [],
                    links: [],
                    status: ''
                }};

                if (document.body) {{
                    const fullText = document.body.innerText || '';
                    const textStart = (scope - 1) * TEXT_SIZE;
                    info.text = fullText.substring(textStart, textStart + TEXT_SIZE);
                    const textScopes = Math.ceil(fullText.length / TEXT_SIZE);
                    info.totalScopes = Math.max(textScopes, 1);
                }}

                const allInputs = [];
                const allLinks = [];
                let maxId = parseInt(document.body.getAttribute('data-alife-max-id') || '0');

                const scan = (root) => {{
                    if (!root) return;
                    try {{
                        const nodes = root.querySelectorAll('*');
                        for (let i = 0; i < nodes.length; i++) {{
                            const node = nodes[i];
                            try {{
                                const tagName = (node.tagName || '').toLowerCase();
                                if (!tagName || ['script', 'style', 'svg', 'path', 'meta'].includes(tagName)) continue;

                                const isInput = ['input', 'textarea', 'select'].includes(tagName);
                                const isLink = tagName === 'a' && node.href;
                                const isBtn = tagName === 'button' || node.getAttribute('role') === 'button' || node.hasAttribute('onclick');
                                
                                let isPointer = false;
                                if (!isInput && !isLink && !isBtn && !['html', 'body'].includes(tagName)) {{
                                    const style = window.getComputedStyle(node);
                                    isPointer = style && style.cursor === 'pointer';
                                }}

                                if (isInput || isLink || isBtn || isPointer) {{
                                    if (node.offsetWidth === 0 || node.offsetHeight === 0) continue;

                                    const text = (node.innerText || node.value || node.placeholder || node.title || node.alt || node.getAttribute('aria-label') || '').substring(0, 80).trim();
                                    const href = node.href || '';

                                    if (isInput) {{
                                        let id = node.getAttribute('data-alife-id');
                                        if (!id) {{
                                            id = (++maxId).toString();
                                            node.setAttribute('data-alife-id', id);
                                        }}
                                        allInputs.push({{
                                            text: text || ('[' + (node.type || tagName) + ']'),
                                            type: node.type || tagName,
                                            selector: '[data-alife-id=\'' + id + '\']'
                                        }});
                                    }} else if (href || isPointer || isBtn) {{
                                        const linkItem = {{ text: text || (href ? '[Link]' : '[Button]') }};
                                        if (href) linkItem.href = href.substring(0, 150);
                                        
                                        if (!href || isBtn || isPointer) {{
                                            let id = node.getAttribute('data-alife-id');
                                            if (!id) {{
                                                id = (++maxId).toString();
                                                node.setAttribute('data-alife-id', id);
                                            }}
                                            linkItem.selector = '[data-alife-id=\'' + id + '\']';
                                        }}
                                        allLinks.push(linkItem);
                                    }}
                                }}

                                if (node.shadowRoot) scan(node.shadowRoot);
                            }} catch (e) {{}}
                        }}
                    }} catch (e) {{}}
                }};

                scan(document);
                if (document.body) document.body.setAttribute('data-alife-max-id', maxId.toString());

                const elementStart = (scope - 1) * ELEMENT_SIZE;
                info.inputs = allInputs; 
                info.links = allLinks.slice(elementStart, elementStart + ELEMENT_SIZE);
                
                const linkScopes = Math.ceil(allLinks.length / ELEMENT_SIZE);
                info.totalScopes = Math.max(info.totalScopes, linkScopes);
                info.status = 'Scope ' + scope + '/' + info.totalScopes + 
                              ' (Text: ' + info.text.length + ', Inputs: ' + allInputs.length + ', Links: ' + allLinks.length + ')';

                return JSON.stringify(info);
            }} catch (err) {{
                return JSON.stringify({{ error: err.toString() }});
            }}
        }})()");
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
                return await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize("ERROR: JS 引擎异常 - " + ex.Message);
            }
        });

        if (string.IsNullOrEmpty(rawRes) || rawRes == "null") return null;

        try
        {
            using var doc = JsonDocument.Parse(rawRes);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return doc.RootElement.GetString();
            }
            return rawRes;
        }
        catch
        {
            return rawRes;
        }
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

public class NavigateResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
}
