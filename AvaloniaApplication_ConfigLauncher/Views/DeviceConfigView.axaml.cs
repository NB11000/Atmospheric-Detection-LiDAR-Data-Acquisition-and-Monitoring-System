using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AvaloniaApplication_ConfigLauncher.ViewModels;

namespace AvaloniaApplication_ConfigLauncher.Views;

public partial class DeviceConfigView : UserControl
{
    public DeviceConfigView()
    {
        InitializeComponent();
    }

    private void SubTab0_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetSubTab(0);

    private void SubTab1_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetSubTab(1);

    private void SubTab2_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetSubTab(2);

    private void SubTab3_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetSubTab(3);

    private void SetSubTab(int index)
    {
        if (DataContext is DeviceConfigViewModel vm)
            vm.SelectedSubTabIndex = index;
    }

    private async void SelectDataDir_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择数据持久化目录",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is DeviceConfigViewModel vm)
        {
            vm.PersistenceDataDirectory = folders[0].Path.LocalPath;
        }
    }
}
