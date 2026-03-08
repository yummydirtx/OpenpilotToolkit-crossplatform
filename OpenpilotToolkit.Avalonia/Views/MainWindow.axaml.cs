using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenpilotToolkit.Avalonia.ViewModels;

namespace OpenpilotToolkit.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseSshKeyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || StorageProvider is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a private SSH key",
            AllowMultiple = false
        });

        var selectedPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            viewModel.SetSshKeyPath(selectedPath);
        }
    }

    private async void BrowseExportFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || StorageProvider is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose an export folder",
            AllowMultiple = false
        });

        var selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            viewModel.SetExportFolder(selectedPath);
        }
    }

    public async Task<string?> RequestSshKeyPassphraseAsync(string promptMessage)
    {
        var dialog = new PassphrasePromptWindow(promptMessage);
        return await dialog.ShowDialog<string?>(this);
    }
}
