using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaApplication_ConfigLauncher.ViewModels;

namespace AvaloniaApplication_ConfigLauncher.Views;

public partial class ConfigView : UserControl
{
    public ConfigView()
    {
        InitializeComponent();
    }

    private async void SaveAndStart_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm)
            return;

        // Check username and show warning if empty
        if (string.IsNullOrWhiteSpace(vm.Username))
        {
            await ShowWarningDialogAsync(
                "用户名未填写，EMQX Serverless 需要认证凭据。\n\n是否继续保存？");
        }

        vm.SaveAndStartCommand.Execute(null);
    }

    private async void SelectCaFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 CA 证书文件",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("证书文件")
                {
                    Patterns = new[] { "*.crt", "*.pem", "*.cer" }
                },
                new("所有文件")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            vm.SetCaCertificatePath(files[0].Path.LocalPath);
        }
    }

    private async Task ShowWarningDialogAsync(string message)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var tcs = new TaskCompletionSource<bool>();

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(15, 15, 15, 5)
        };

        var button = new Button
        {
            Content = "确定",
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 80,
            Margin = new Thickness(0, 0, 0, 15)
        };

        var panel = new StackPanel
        {
            Children = { textBlock, button }
        };

        var dialog = new Window
        {
            Title = "警告",
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            CanResize = false
        };

        button.Click += (_, _) => dialog.Close();
        dialog.Closed += (_, _) => tcs.TrySetResult(true);

        await dialog.ShowDialog(window);
        await tcs.Task;
    }
}
