using Alife.Function.Browser;
using Xunit;
using System.Text.Json;

namespace Alife.Test.Browser;

public class BrowserIntegrationTest
{
    [Fact]
    public async Task TestFullSearchWorkflow()
    {
        using var engine = new BrowserEngine();
        
        // 1. 初始导航
        Console.WriteLine("正在导航到首页...");
        var navResult = await engine.NavigateAsync("https://www.midiclouds.com/");
        Assert.True(navResult.Success, "首页导航失败");
        
        // 2. 第一次观察：获取输入框 ID
        Console.WriteLine("正在执行第一次观察...");
        string obsJson = await engine.ObserveAsync();
        Assert.NotNull(obsJson);
        
        using var doc = JsonDocument.Parse(obsJson);
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.True(inputs.GetArrayLength() > 0, "未在首页找到任何输入框");
        
        // 假设第一个输入框就是搜索框
        string selector = inputs[0].GetProperty("selector").GetString()!;
        Console.WriteLine($"找到输入框，选择器为: {selector}");
        
        // 3. 执行 JS 输入和回车搜索
        string keyword = "极乐净土";
        string searchScript = $@"
            (function() {{
                const inp = document.querySelector(""{selector}"");
                if (!inp) throw new Error('未找到输入框');
                inp.focus();
                inp.value = '{keyword}';
                inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                
                // 模拟回车
                const ev = new KeyboardEvent('keydown', {{ key: 'Enter', keyCode: 13, which: 13, bubbles: true }});
                inp.dispatchEvent(ev);
                return '搜索已发起';
            }})()";
        
        Console.WriteLine($"正在执行搜索 JS: {keyword}");
        string jsResult = await engine.ExecuteScriptAsync(searchScript);
        Assert.Contains("搜索已发起", jsResult);
        
        // 4. 等待页面跳转和稳定
        Console.WriteLine("正在等待搜索结果加载...");
        await engine.WaitUntilStableAsync();
        await Task.Delay(2000); // 额外等待 AJAX 渲染
        
        // 5. 第二次观察：验证搜索结果
        Console.WriteLine("正在执行第二次观察（搜索结果页）...");
        string resultJson = await engine.ObserveAsync();
        Assert.NotNull(resultJson);
        
        using var doc2 = JsonDocument.Parse(resultJson);
        string newTitle = doc2.RootElement.GetProperty("title").GetString()!;
        string newText = doc2.RootElement.GetProperty("text").GetString()!;
        
        Console.WriteLine($"新页面标题: {newTitle}");
        // 验证标题或内容是否包含关键词
        Assert.True(newTitle.Contains(keyword) || newText.Contains(keyword), "搜索结果页未发现关键词内容");
        
        Console.WriteLine("全流程测试通过！");
    }
}
