using System.Windows;
using Alife.Platform;
using Microsoft.AspNetCore.Components.WebView;

namespace Alife;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    void OnBlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    {
        string? overrideFolder = Environment.GetEnvironmentVariable(
            ControlCenterWebView2Profile.UserDataFolderEnvironmentVariable);
        e.UserDataFolder = ControlCenterWebView2Profile.ResolveUserDataFolder(
            AlifePath.RuntimeFolderPath,
            overrideFolder);
        AlifeTerminal.LogInfo($"Control Center WebView2 user data folder: {e.UserDataFolder}");
    }

    void MinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    void MaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaxBtn.Content = "▢";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaxBtn.Content = "❐";
        }
    }

    void CloseClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
