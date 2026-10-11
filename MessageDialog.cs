using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace dpClock_master;

/// <summary>
/// Avalonia has no MessageBox. This is a small themed replacement for the legacy
/// MessageBox.Show calls (OK dialog and Yes/No confirmation).
/// </summary>
public static class MessageDialog
{
    public static Task ShowAsync(Window? owner, string title, string message) =>
        ShowCoreAsync(owner, title, message, confirm: false);

    public static Task<bool> ConfirmAsync(Window? owner, string title, string message) =>
        ShowCoreAsync(owner, title, message, confirm: true);

    private static async Task<bool> ShowCoreAsync(Window? owner, string title, string message, bool confirm)
    {
        bool result = false;
        bool ownerUsable = owner is { IsVisible: true };

        var dialog = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Topmost = true,
            WindowStartupLocation = ownerUsable
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };

        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        var yes = new Button { Content = confirm ? "Yes" : "OK", MinWidth = 84, IsDefault = true };
        yes.Classes.Add("primary");
        yes.Click += (_, _) => { result = true; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (confirm)
        {
            var no = new Button { Content = "No", MinWidth = 84, IsCancel = true };
            no.Click += (_, _) => dialog.Close();
            buttons.Children.Add(no);
        }
        buttons.Children.Add(yes);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children = { text, buttons }
        };

        if (ownerUsable)
        {
            await dialog.ShowDialog(owner!);
        }
        else
        {
            // The main window can be hidden in the tray; a modal dialog needs a visible owner.
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            dialog.Activate();
            await closed.Task;
        }

        return result;
    }
}
