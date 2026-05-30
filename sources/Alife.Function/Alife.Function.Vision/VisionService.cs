using System;
using Alife.Platform;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Vision;

public record VisionServiceConfig
{
    //对图片的附加提示词
    public string AppendPrompt { get; set; } = "（请精简的描述一下图片大体内容，避免输出过多的文本，提高分析速度）";
}

[Plugin("视觉感知", "让 AI 能够看到屏幕内容，理解图片，观察世界。",
defaultCategory: "Alife 官方/实用工具",
EditorUI = typeof(VisionServiceUI))]
[Description("此服务让你拥有视觉感知能力：你可以截取屏幕画面并理解其内容，或者分析用户提供的图片。" +
             "在分析图片时，你需要提供prompt参数。在该参数中，你要清晰描述你的问题，并尽可能提供背景信息，以帮助视觉模型分析，但也注意不要随意揣测其中内容，防止影响识别结果！")]
public class VisionService(XmlFunctionCaller functionService, IVisionModel? visionModel = null)
    : InteractivePlugin<VisionService>, IConfigurable<VisionServiceConfig>
{
    public VisionServiceConfig? Configuration { get; set; }

    /// <summary>
    /// 获取当前可以截取的所有可用窗口的列表，供 AI 选择截屏目标。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description($"获取当前系统所有可用窗口列表，返回结果包含窗口标题和对应的 Handle，可用于传入到 {nameof(LookScreen)} 中进行识图。")]
    public void GetWindows()
    {
        var windows = WindowCaptureHelper.EnumerateWindows()
            .Where(w => !string.IsNullOrWhiteSpace(w.Title))
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【可用窗口列表】");
        foreach (var w in windows)
            sb.AppendLine($"Handle: {w.Handle.ToInt64()} | 标题: {w.Title}");

        Poke(sb.ToString());
    }

    /// <summary>
    /// 截取指定窗口或全屏并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看当前屏幕或特定窗口内容。（使用后需等待结果返回）")]
    public async Task LookScreen(
        [Description($"要查看画面的窗口句柄（可用 {nameof(GetWindows)} 获取），传入 -1 则直接识别当前全屏画面")] long windowHandle,
        string prompt,
        [Description("期望回复字数，过小可能结果不全，过大可能分析过慢")] int maxToken = 64)
    {
        if (AlifePlatform.IsLocking())
        {
            Poke("【屏幕分析结果】当前电脑处于锁屏状态，无法获取屏幕内容，用户应该不在电脑前。");
            return;
        }

        //截取目标画面
        string screenshotPath = Path.Combine(AlifePath.TempFolderPath, $"vision_capture_{DateTime.Now.Ticks}.png");
        {
            using var bmp = windowHandle == -1
                ? await WindowCaptureHelper.CaptureFullscreenAsync()
                : await WindowCaptureHelper.CaptureWindowAsync(new IntPtr(windowHandle));
            bmp.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        //获取深度识别结果
        prompt += Configuration!.AppendPrompt;
        if (windowHandle == -1)
            prompt += $"（这是一张屏幕截图，当前焦点窗口为{WindowsPlatform.GetActiveWindowTitle()}）" + Configuration!.AppendPrompt;

        CancellationTokenSource cancellationTokenSource = new(30000);
        string deepVisionResult = visionModel != null
            ? $"{await visionModel.QueryAsync(
            screenshotPath,
            prompt,
            maxToken,
            cancellationToken: cancellationTokenSource.Token)}"
            : "未开启";

        if (windowHandle == -1)
        {
            Poke($"""
                  【屏幕分析结果】
                  - 窗口列表：{AlifePlatform.GetRunningWindowTitles()}
                  - 焦点窗口：{WindowsPlatform.GetActiveWindowTitle()}
                  - 深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
                  """);
        }
        else
        {
            Poke($"""
                  【窗口分析结果】
                  - 文字识别：{await AlifePlatform.OcrAsync(screenshotPath)}
                  - 深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
                  """);
        }
    }

    /// <summary>
    /// 分析指定路径的图片。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("对指定的图片进行视觉分析。（使用后需等待结果返回）")]
    public async Task LookImage(
        [Description("图片地址或网址")] string path,
        string prompt,
        [Description("期望回复字数，过小可能结果不全，过大可能分析过慢")] int maxToken = 64)
    {
        // 处理网络图片
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            string downloaded = $"{AlifePath.TempFolderPath}/vision_download.png";
            await AlifePlatform.DownloadFileAsync(path, downloaded);
            path = downloaded;
        }

        prompt += Configuration!.AppendPrompt;

        CancellationTokenSource cancellationTokenSource = new(30000);
        string deepVisionResult = visionModel != null
            ? await visionModel.QueryAsync(
            path,
            prompt,
            maxToken,
            cancellationToken: cancellationTokenSource.Token)
            : "未开启";

        Poke($"""
              【图片分析结果】
              - 文字识别：{await AlifePlatform.OcrAsync(path)}
              - 深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
              """);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionService.RegisterHandler(this);
    }
}
