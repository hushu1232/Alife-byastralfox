using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.Browser;

public class NavigateResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
}

public interface IBrowserRuntime : IDisposable
{
    bool IsReady { get; }
    Task WaitToLoadedAsync(TimeSpan timeout);
    Task<NavigateResult> NavigateAsync(string url, TimeSpan? timeout = null);
    Task<string> ExecuteScriptAsync(string code);
    Task<string> ObserveAsync(int page);
    Task<string> GetElementInfoAsync(int id);
}

public class BrowserEngine : IBrowserRuntime
{
    public BrowserEngine(string? userDataFolder = null)
    {
        worker = new WebViewWorker(userDataFolder);
    }

    public bool IsReady => worker.IsLoaded;

    public async Task WaitToLoadedAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (!worker.IsLoaded)
            {
                if (worker.InitializationError != null)
                    throw new InvalidOperationException("Browser WebView failed to initialize.", worker.InitializationError);
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Browser WebView did not initialize within {timeout.TotalSeconds:F0} seconds.");
        }
    }

    /// <summary>
    /// 跳转到指定页面
    /// </summary>
    public Task<NavigateResult> NavigateAsync(string url, TimeSpan? timeout = null)
    {
        return worker.AddFormTask(async webView => {
            if (webView.CoreWebView2 == null)
                throw new InvalidOperationException("Browser WebView is not initialized.");

            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            DateTime deadline = DateTime.UtcNow + effectiveTimeout;
            webView.Source = new Uri(url);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
                string readyStateJson = await webView.CoreWebView2.ExecuteScriptAsync("document.readyState");
                if (readyStateJson.Contains("interactive", StringComparison.OrdinalIgnoreCase) ||
                    readyStateJson.Contains("complete", StringComparison.OrdinalIgnoreCase))
                {
                    return new NavigateResult { Success = true, StatusCode = 0 };
                }
            }

            throw new TimeoutException($"Navigation to '{url}' did not complete within {effectiveTimeout.TotalSeconds:F0} seconds.");
        });
    }

    /// <summary>
    /// 执行JavaScript并易读的结果
    /// </summary>
    public Task<string> ExecuteScriptAsync(string code)
    {
        return worker.AddFormTask(async webView => {
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
            try
            {
                string resultJson = await webView.CoreWebView2.ExecuteScriptAsync(wrapperScript);
                string stringResult = JsonSerializer.Deserialize<string>(resultJson) ?? resultJson;
                return $"[Success] Return:\n{stringResult}";
            }
            catch (Exception ex)
            {
                return $"[Error]\nName: {ex.GetType().Name}\nMessage: {ex.Message}";
            }
        });
    }

    /// <summary>
    /// 观察当前页面，返回格式化后的页面信息，同时会对可交互组件增加data-alife-id属性
    /// </summary>
    public async Task<string> ObserveAsync(int page)
    {
        //等待页面稳定
        while (worker.IsNavigating)
        {
            await Task.Delay(300);
        }

        int currentPage = page < 1 ? 1 : page;
        string jsCode = $$$"""
                           (() => {
                               const M_CLS = 'al-m';
                               const TEXT_LIMIT = 1000;  // 期望单页文本上限
                               const ITEM_LIMIT = 20;   // 期望单页按钮上限
                               const ATTR_OLD = 'data-al-old';
                               const scope = {{{currentPage}}};
                               
                               document.querySelectorAll('.' + M_CLS).forEach(e => e.remove());
                               document.querySelectorAll(`[${ATTR_OLD}]`).forEach(e => {
                                   e.style.display = e.getAttribute(ATTR_OLD);
                                   e.removeAttribute(ATTR_OLD);
                               });
                               document.querySelectorAll('[data-alife-id]').forEach(e => e.removeAttribute('data-alife-id'));

                               let id = 0;
                               const map = {};
                               const getT = e => (e.innerText || e.value || e.placeholder || e.title || e.getAttribute('aria-label') || '').trim().replace(/\s+/g, ' ').slice(0, 50);

                               const targetNodes = [];
                               for (const n of document.querySelectorAll('body *')) {
                                   if (!n.offsetWidth || ['SCRIPT', 'STYLE', 'SVG', 'META'].includes(n.tagName)) continue;
                                   const s = window.getComputedStyle(n);
                                   const isI = s.cursor === 'text' || ['INPUT', 'TEXTAREA'].includes(n.tagName) || n.isContentEditable;
                                   const isB = s.cursor === 'pointer' || ['A', 'BUTTON'].includes(n.tagName) || n.getAttribute('role') === 'button';
                                   if (!isI && (n.closest('a') && n.tagName !== 'A' || n.closest('button') && n.tagName !== 'BUTTON')) continue;

                                   if (isI || isB) {
                                       const t = getT(n), h = n.href || '';
                                       if (!isI && !t && !h) continue;
                                       const cur = ++id;
                                       n.setAttribute('data-alife-id', cur);
                                       map[cur] = { t: t || (isI ? '输入框' : '按钮'), h: h ? h.substring(0, 150) : '', isI };
                                       n.setAttribute(ATTR_OLD, n.style.display);
                                       n.style.display = 'none';
                                       const m = document.createElement('span');
                                       m.className = M_CLS;
                                       m.innerText = `[${cur}]`;
                                       m.style.cssText = 'position:absolute;opacity:0;font-size:1px;pointer-events:none;';
                                       n.after(m);
                                       targetNodes.push(n);
                                   }
                               }

                               const fullText = (document.body?.innerText || '').replace(/\s+/g, ' ').trim();

                               document.querySelectorAll('.' + M_CLS).forEach(e => e.remove());
                               for (const n of targetNodes) {
                                   n.style.display = n.getAttribute(ATTR_OLD);
                                   n.removeAttribute(ATTR_OLD);
                               }

                               // --- 核心：复合分页逻辑 ---
                               const totalItems = id;
                               // 总页数取文本页数和按钮页数的最大值
                               const totalPages = Math.max(
                                   Math.ceil(fullText.length / TEXT_LIMIT), 
                                   Math.ceil(totalItems / ITEM_LIMIT), 
                                   1
                               );
                               // 根据总页数计算本页应截取的平均字符步长
                               const stride = Math.ceil(fullText.length / totalPages);
                               const startIdx = (scope - 1) * stride;
                               let endIdx = startIdx + stride;
                               
                               if (endIdx < fullText.length) {
                                   const nextSpace = fullText.indexOf(' ', endIdx);
                                   if (nextSpace !== -1 && (nextSpace - endIdx) < 30) endIdx = nextSpace;
                               }
                               const pageContent = fullText.substring(startIdx, endIdx);
                               // ---------------------------

                               const found = [];
                               const re = /\[(\d+)\]/g;
                               let m;
                               while ((m = re.exec(pageContent)) !== null) {
                                   if (!found.includes(m[1])) found.push(m[1]);
                               }

                               const replaced = pageContent.replace(/\[(\d+)\]/g, (_, id) => {
                                   const i = map[id];
                                   return i ? `[${id}:${i.t}]` : `[${id}]`;
                               });

                               let out = `标题:${document.title}\n链接:${location.href}\n分页:${scope}/${totalPages}`;
                               if (scope < totalPages) out += ` (注意！当前页面显示不完整，请使用 page=${scope + 1} 来查看下一页)`;
                               out += `\n\n${replaced}`;

                               return out.trim();
                           })();
                           """;


        return await ExecuteScriptAsync(jsCode);
    }

    public Task<string> GetElementInfoAsync(int id)
    {
        string jsCode = $$$"""
                           (() => {
                               const el = document.querySelector('[data-alife-id="{{{id}}}"]');
                               if (!el) return JSON.stringify({ found: false });
                               const info = {
                                   found: true,
                                   id: {{{id}}},
                                   tag: el.tagName.toLowerCase(),
                                   text: (el.innerText || el.value || el.placeholder || el.title || '').trim().slice(0, 200),
                                   href: el.href || '',
                                   type: el.type || '',
                                   placeholder: el.placeholder || '',
                                   ariaLabel: el.getAttribute('aria-label') || '',
                                   className: el.className || ''
                               };
                               return JSON.stringify(info, null, 2);
                           })();
                           """;
        return ExecuteScriptAsync(jsCode);
    }

    readonly WebViewWorker worker;

    public void Dispose() => worker.Dispose();
}
