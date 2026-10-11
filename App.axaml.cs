using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace dpClock_master;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    #region NOTIFY ICON (backTask / backTaskContext)

    // WinForms: backTask.DoubleClick -> SHOWWINDOW
    private void TrayIcon_Clicked(object? sender, EventArgs e) => ShowMainWindow();

    // WinForms: backTaskContext_openWindow.Click
    private void _backTaskContext_openWindow(object? sender, EventArgs e) => ShowMainWindow();

    // WinForms: backTaskContext_endProcess.Click (Yes/No confirmation, then Application.Exit)
    private async void _backTaskContext_endProcess(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        bool yes = await MessageDialog.ConfirmAsync(
            desktop.MainWindow, "Confirmation", "Do you want to close the application?");

        if (yes)
            desktop.Shutdown();
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            window.SHOWWINDOW();
    }

    #endregion
}
