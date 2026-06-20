using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Alife.DeskPet;

/// <summary>
/// 极薄的 UI 壳层，仅通过 IPetWindow 接口提供窗口服务
/// </summary>
public partial class MainWindow
{
    public static async Task<MainWindow> Create()
    {
        MainWindow mainWindow = new MainWindow();
        mainWindow.InitializeComponent();
        mainWindow.Show();

        //禁用窗口最大化
        mainWindow.StateChanged += (_, _) => {
            if (mainWindow.WindowState == WindowState.Maximized) mainWindow.WindowState = WindowState.Normal;
        };

        WebView2 webView = mainWindow.WebView;
        string userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data");
        if (!Directory.Exists(userDataFolder))
            Directory.CreateDirectory(userDataFolder);
        CoreWebView2EnvironmentOptions options = new(
            "--disable-gpu --disable-gpu-compositing --disable-gpu-sandbox --disable-features=RendererCodeIntegrity --no-sandbox");
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder, options: options);
        await webView.EnsureCoreWebView2Async(environment);
        string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        webView.CoreWebView2.ProcessFailed += async (_, e) => {
            await File.AppendAllTextAsync(
                "pet.log",
                $"[webview] process failed kind={e.ProcessFailedKind} reason={e.Reason} exitCode={e.ExitCode} description={e.ProcessDescription} source={e.FailureSourceModulePath}" +
                Environment.NewLine);
        };
        webView.CoreWebView2.NavigationCompleted += async (_, e) => {
            await File.AppendAllTextAsync("pet.log", $"[webview] navigation completed success={e.IsSuccess} status={e.WebErrorStatus}" + Environment.NewLine);
        };

        return mainWindow;
    }

    public void NavigateRenderer()
    {
        WebView.CoreWebView2.Navigate("https://app.local/index.html");
    }

    public (double Left, double Top, double Width, double Height) GetLayout()
    {
        return (Left, Top, Width, Height);
    }
    public (double ScaleX, double ScaleY) GetDpi()
    {
        CompositionTarget? compositionTarget = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (compositionTarget != null)
        {
            Matrix matrix = compositionTarget.TransformToDevice;
            return (matrix.M11, matrix.M22);
        }

        return (1.0, 1.0);
    }
    public void ProgrammaticMove(double offsetX, double offsetY, int durationMs)
    {
        (double ScaleX, double ScaleY) dpi = GetDpi();
        double startX = Left;
        double startY = Top;
        double endX = startX + offsetX / dpi.ScaleX;
        double endY = startY + offsetY / dpi.ScaleY;

        DoubleAnimation xAnim = new(startX, endX, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = new QuadraticEase() };
        DoubleAnimation yAnim = new(startY, endY, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = new QuadraticEase() };

        yAnim.Completed += (_, _) => {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = endX;
            Top = endY;
        };

        BeginAnimation(LeftProperty, xAnim);
        BeginAnimation(TopProperty, yAnim);
    }

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 同步 Create 中的特殊定位偏量
        Left = SystemParameters.WorkArea.Width - Width + Width * -1f;
        Top = SystemParameters.WorkArea.Height - Height + Height * 0.5f;
    }

    MainWindow() { }
}
