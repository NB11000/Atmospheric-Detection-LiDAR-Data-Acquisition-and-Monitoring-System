using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaApplication_ConfigLauncher.ViewModels;

namespace AvaloniaApplication_ConfigLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BaseUrlTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SaveBaseUrl();
        }
    }

    /// <summary>
    /// Issue 04 Part E: When user clicks the window close button,
    /// check if WebAPI is running and confirm before closing.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            base.OnClosing(e);
            return;
        }

        if (!vm.IsWebApiProcessRunning)
        {
            base.OnClosing(e);
            return; // No WebAPI running, allow close
        }

        // WebAPI is running — confirm with user
        e.Cancel = true;
        var shouldClose = await ShowCloseConfirmationAsync();
        if (shouldClose)
        {
            // Close the window programmatically after confirmation
            e.Cancel = false;
            Close();
        }

        base.OnClosing(e);
    }

    private async Task<bool> ShowCloseConfirmationAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        var textBlock = new TextBlock
        {
            Text = "关闭启动器不会停止 WebAPI，是否继续？",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(15, 15, 15, 10)
        };

        var yesButton = new Button
        {
            Content = "是",
            Width = 70,
            Margin = new Thickness(5, 0, 5, 0)
        };

        var noButton = new Button
        {
            Content = "否",
            Width = 70,
            Margin = new Thickness(5, 0, 5, 0)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 15),
            Children = { yesButton, noButton }
        };

        var panel = new StackPanel
        {
            Children = { textBlock, buttonPanel }
        };

        var dialog = new Window
        {
            Title = "确认",
            Width = 380,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            CanResize = false
        };

        yesButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }
}
