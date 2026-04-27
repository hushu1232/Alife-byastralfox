using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alife.Basic;

public static class AlifeDevice
{
    public static (int Width, int Height) GetResolution()
    {
        IntPtr hdc = GetDC(IntPtr.Zero); // 获取屏幕设备上下文
        try
        {
            int width = GetDeviceCaps(hdc, DESKTOPHORZRES);
            int height = GetDeviceCaps(hdc, DESKTOPVERTRES);
            return (width, height);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc); // 必须释放句柄
        }
    }

    public static void Notice(string title, string message)
    {
        try
        {
            string script = $"$Title='{title}'; $Message='{message}'; " +
                            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                            "$Template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                            "$TextNodes = $Template.GetElementsByTagName('text'); " +
                            "$TextNodes.Item(0).AppendChild($Template.CreateTextNode($Title)) | Out-Null; " +
                            "$TextNodes.Item(1).AppendChild($Template.CreateTextNode($Message)) | Out-Null; " +
                            "$Toast = [Windows.UI.Notifications.ToastNotification]::new($Template); " +
                            "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('AlifeSpeechAssist').Show($Toast);";

            Process.Start(new ProcessStartInfo {
                FileName = "powershell",
                Arguments = $"-Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification failed: {ex.Message}");
        }
    }
    public static void Command(string fileName, string arguments)
    {
        ProcessStartInfo psi = new() {
            FileName = "cmd.exe",
            Arguments = $"/c {fileName} {arguments}",
            CreateNoWindow = false,
            UseShellExecute = true,
        };
        using Process? process = Process.Start(psi);
        process?.WaitForExit();
    }

    const int DESKTOPHORZRES = 118;
    const int DESKTOPVERTRES = 117;

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]
    static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
}
