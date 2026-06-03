using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaApplication_ConfigLauncher.ViewModels;

namespace AvaloniaApplication_ConfigLauncher.Views;

public partial class MainWindow : Window
{
    private bool _confirmedClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_confirmedClose)
        {
            base.OnClosing(e);
            return;
        }

        if (DataContext is not MainWindowViewModel vm)
        {
            base.OnClosing(e);
            return;
        }

        var isReachable = await vm.IsWebApiHttpReachableAsync();
        if (!isReachable)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        var result = await ShowCloseDialogAsync(vm);

        switch (result)
        {
            case CloseDialogResult.CloseWithWebApi:
            case CloseDialogResult.CloseLauncherOnly:
                _confirmedClose = true;
                Close();
                break;
            case CloseDialogResult.Cancel:
                break;
        }

        base.OnClosing(e);
    }

    private async Task<CloseDialogResult> ShowCloseDialogAsync(MainWindowViewModel vm)
    {
        var tcs = new TaskCompletionSource<CloseDialogResult>();
        var alreadyFailed = false;

        var textBlock = new TextBlock
        {
            Text = "WebAPI 正在运行。关闭启动器时是否同时关闭 WebAPI？",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(15, 15, 15, 10)
        };

        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Margin = new Thickness(15, 5, 15, 5),
            IsVisible = false,
            Height = 20
        };

        var errorText = new TextBlock
        {
            Foreground = Brushes.Red,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(15, 5, 15, 5),
            IsVisible = false
        };

        var shutdownBtn = new Button
        {
            Content = "关闭 WebAPI",
            Width = 90,
            Margin = new Thickness(5, 0, 5, 0)
        };

        var launcherOnlyBtn = new Button
        {
            Content = "只关启动器",
            Width = 90,
            Margin = new Thickness(5, 0, 5, 0)
        };

        var cancelBtn = new Button
        {
            Content = "取消",
            Width = 70,
            Margin = new Thickness(5, 0, 5, 0)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 15),
            Children = { shutdownBtn, launcherOnlyBtn, cancelBtn }
        };

        var panel = new StackPanel
        {
            Children = { textBlock, errorText, progressBar, buttonPanel }
        };

        var dialog = new Window
        {
            Title = "关闭启动器",
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            CanResize = false
        };

        dialog.Closing += (_, args) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                args.Cancel = true;
            }
        };

        shutdownBtn.Click += async (_, _) =>
        {
            if (alreadyFailed)
            {
                errorText.Text = "请手动关闭 WebAPI 进程。";
                errorText.IsVisible = true;
                return;
            }

            SetProgressState(textBlock, progressBar, buttonPanel);

            var success = await Task.Run(() => vm.ShutdownWebApiAsync());

            if (success)
            {
                tcs.TrySetResult(CloseDialogResult.CloseWithWebApi);
                dialog.Close();
            }
            else
            {
                alreadyFailed = true;
                errorText.Text = "WebAPI 未能在 3 秒内退出。";
                errorText.IsVisible = true;
                progressBar.IsVisible = false;
                textBlock.IsVisible = true;
                textBlock.Text = "WebAPI 正在运行。关闭启动器时是否同时关闭 WebAPI？";
                buttonPanel.IsVisible = true;
            }
        };

        launcherOnlyBtn.Click += (_, _) =>
        {
            tcs.TrySetResult(CloseDialogResult.CloseLauncherOnly);
            dialog.Close();
        };

        cancelBtn.Click += (_, _) =>
        {
            tcs.TrySetResult(CloseDialogResult.Cancel);
            dialog.Close();
        };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private static void SetProgressState(TextBlock textBlock, ProgressBar progressBar, StackPanel buttonPanel)
    {
        textBlock.Text = "正在关闭 WebAPI，请稍候...";
        textBlock.IsVisible = true;
        buttonPanel.IsVisible = false;
        progressBar.IsVisible = true;
    }

    private enum CloseDialogResult
    {
        CloseWithWebApi,
        CloseLauncherOnly,
        Cancel
    }
}
