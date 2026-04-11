using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Alife.Function.DeskPet;

/// <summary>
/// 网页桌宠的 C# 化身，仅作为渲染层的协议转发
/// </summary>
public class PetBridge : IDisposable
{
    public event Action? OnReady;
    public event Action<List<string>>? OnPoke;
    public event Action<string>? OnInput;
    public event Action? OnDragStart;
    public event Action? OnDragEnd;

    public PetBridge(WebView2 webView, PetModelMetadata metadata)
    {
        this.webView = webView;
        this.webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        this.metadata = metadata;

        cancellationTokenSource = new CancellationTokenSource();
        Update(cancellationTokenSource.Token);
    }
    public void Dispose()
    {
        cancellationTokenSource.Dispose();
    }

    public void LoadModel(string url)
    {
        SendCommand(new { type = "load", url });
    }
    public void PlayExpression(string? id)
    {
        lock (locker)
        {
            lastExpressionTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            SendCommand(new { type = "expression", id });
        }
    }
    public void PlayMotion(string group, int index)
    {
        SendCommand(new { type = "motion", group, index });
    }
    public void ShowBubble(string text)
    {
        lock (locker)
        {
            lastBubbleTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            SendCommand(new { type = "bubble", text });
        }
    }
    public void HideBubble()
    {
        SendCommand(new { type = "hide-bubble" });
    }
    public void SetFocus(double x, double y, bool instant = false)
    {
        SendCommand(new { type = "look", x, y, instant });
    }

    readonly WebView2 webView;
    readonly PetModelMetadata metadata;
    readonly CancellationTokenSource cancellationTokenSource;
    long lastExpressionTime;
    long lastBubbleTime;
    Lock locker = new Lock();

    void SendCommand(object command)
    {
        string json = JsonSerializer.Serialize(command);
        webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("type", out JsonElement typeProp) == false) return;
            string? type = typeProp.GetString();
            
            switch (type)
            {
                case "ready":
                    OnReady?.Invoke();
                    break;
                case "drag_start":
                    OnDragStart?.Invoke();
                    break;
                case "drag_end":
                    OnDragEnd?.Invoke();
                    break;
                case "poke":
                    List<string> areas = new();
                    if (root.TryGetProperty("areas", out JsonElement areasProp))
                    {
                        foreach (JsonElement area in areasProp.EnumerateArray())
                        {
                            areas.Add(area.GetString() ?? "");
                        }
                    }
                    OnPoke?.Invoke(areas);
                    break;
                case "input":
                    OnInput?.Invoke(root.GetProperty("text").GetString() ?? "");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PetBridge Message Error: {ex.Message}");
        }
    }


    async void Update(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(200, cancellationToken);

                lock (locker)
                {
                    if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastExpressionTime > 3000)
                        PlayExpression(metadata.Expressions.First());
                    if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastBubbleTime > 6000)
                        HideBubble();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
