using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.Browser;

public class NavigateResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
}

public class BrowserEngine : IDisposable
{
    /// <summary>
    /// 跳转到指定页面
    /// </summary>
    public async Task<NavigateResult> NavigateAsync(string url)
    {
        return await worker.AddFormTask(async webView =>
        {
            var tcs = new TaskCompletionSource<NavigateResult>();
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.Navigate(url);
            return await tcs.Task;

            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                tcs.SetResult(new NavigateResult { Success = e.IsSuccess, StatusCode = (int)e.WebErrorStatus });
            }
        });
    }
    /// <summary>
    /// 执行JavaScript并易读的结果
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string code)
    {
        return await worker.AddFormTask(async webView =>
        {
            string wrapperScript =
                $$$"""
                   (function() {
                       const logs = [];
                       const originalLog = console.log;
                       
                       console.log = (...args) => {
                           logs.push(args.map(v => typeof v === 'object' ? JSON.stringify(v) : String(v)).join(' '));
                       };

                       try {
                           const rawCode = {{{JsonSerializer.Serialize(code)}}};
                           let result;

                           try {
                               result = eval(rawCode);
                           } catch (e) {
                               if (e instanceof SyntaxError) {
                                   result = eval("(function() {\n" + rawCode + "\n})()");
                               } else {
                                   // 如果是运行时错误（代码执行到一半报错），直接抛出，拒绝重试
                                   throw e;
                               }
                           }
                           
                           const finalValue = result;
                           
                           let output = "";
                           
                           if (typeof finalValue !== 'undefined' && finalValue !== null) {
                               output += typeof finalValue === 'object' ? JSON.stringify(finalValue, null, 2) : String(finalValue);
                           } else {
                               output += "[执行成功，无返回值]";
                           }
                           
                           //追加控制台日志（如果有）
                           if (logs.length > 0) {
                               output += "\n\n[Console Logs]\n" + logs.join('\n');
                           }
                           
                           return output.trim();
                           
                       } finally {
                           console.log = originalLog;
                       }
                   })();
                   """;
            var result = await webView.CoreWebView2.ExecuteScriptWithResultAsync(wrapperScript);
            if (result.Succeeded)
            {
                result.TryGetResultAsString(out string stringResult, out int isSuccess);
                stringResult = isSuccess == 1 ? stringResult : result.ResultAsJson;
                return $"[Success] Return:\n{stringResult}";
            }

            var ex = result.Exception;
            return $"[Error]\nName: {ex.Name}\nMessage: {ex.Message}\nDetail: {ex.ToJson}\nLocation: Line {ex.LineNumber}, Column {ex.ColumnNumber}";
        });
    }
    /// <summary>
    /// 观察当前页面，返回格式化后的页面信息，同时会对可交互组件增加data-alife-id属性
    /// </summary>
    public async Task<string> ObserveAsync(int scope)
    {
        //等待页面稳定
        while (worker.IsLoading)
        {
            await Task.Delay(300);
        }

        int currentScope = scope < 1 ? 1 : scope;


        string jsCode = $$$"""
                           (function() {
                               document.querySelectorAll('[data-alife-id]').forEach(el => el.removeAttribute('data-alife-id'));

                               const scope = {{{currentScope}}}; 
                               const TEXT_SIZE = 1500;
                               const ELEMENT_SIZE = 40;

                               let id = 0;
                               const allItems = [];
                               
                               const getTxt = n => {
                                   let txt = (n.innerText || n.value || n.placeholder || n.title || n.getAttribute('aria-label') || '').trim();
                                   if (!txt) {
                                       const iconMatch = n.className?.toString().match(/(?:icon|btn|fa)[_-]([a-z0-9-]+)/i);
                                       if (iconMatch) txt = `Icon:${iconMatch[1]}`;
                                   }
                                   return txt.replace(/\s+/g, ' ').slice(0, 40);
                               };

                               for (const n of document.querySelectorAll('body *')) {
                                   if (!n.offsetWidth || ['SCRIPT', 'STYLE', 'SVG', 'PATH', 'META'].includes(n.tagName)) continue;

                                   const cursor = window.getComputedStyle(n).cursor;
                                   const isInput = cursor === 'text' || ['INPUT', 'TEXTAREA'].includes(n.tagName) || n.isContentEditable;
                                   const isBtn = cursor === 'pointer' || ['A', 'BUTTON'].includes(n.tagName) || n.getAttribute('role') === 'button';

                                   if (!isInput && (n.closest('a') && n.tagName !== 'A' || n.closest('button') && n.tagName !== 'BUTTON')) continue;

                                   if (isInput || isBtn) {
                                       const text = getTxt(n);
                                       const href = n.href || '';
                                       if (!isInput && !text && !href) continue;

                                       // 使用符合 HTML5 标准的 data-alife-id
                                       n.setAttribute('data-alife-id', ++id);
                                       allItems.push({
                                           type: isInput ? 'input' : 'btn',
                                           text: text || (isInput ? 'Input' : 'Button'),
                                           id: id,
                                           href: href ? href.substring(0, 150) : ''
                                       });
                                   }
                               }

                               const fullText = (document.body?.innerText || '').replace(/\s+/g, ' ').trim();
                               const totalScopes = Math.max(Math.ceil(fullText.length / TEXT_SIZE), Math.ceil(allItems.length / ELEMENT_SIZE), 1);

                               const pageText = fullText.substring((scope - 1) * TEXT_SIZE, scope * TEXT_SIZE);
                               const pageItems = allItems.slice((scope - 1) * ELEMENT_SIZE, scope * ELEMENT_SIZE); 

                               const ins = [], btns = [];
                               for (const i of pageItems) {
                                   if (i.type === 'input') {
                                       ins.push(`${i.text}[${i.id}]`);
                                   } else {
                                       // 链接紧跟 ID 后面，极致压缩
                                       btns.push(`${i.text}[${i.id}]${i.href}`);
                                   }
                               }

                               let out = `TITLE:${document.title}\nURL:${location.href}\nPAGING:${scope}/${totalScopes}`;
                               if (scope < totalScopes) out += ` (Hint: use scope=${scope + 1} to see more)`;
                               out += `\n\n${pageText}\n\n`;

                               if (ins.length) out += `--INPUTS--\n${ins.join('\n')}\n\n`;
                               if (btns.length) out += `--BUTTONS--\n${btns.join('\n')}`;

                               return out.trim();
                           })();
                           """;

        return await ExecuteScriptAsync(jsCode);
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
    
    readonly WebViewWorker worker = new();
    
    public void Dispose() => worker.Dispose();
}