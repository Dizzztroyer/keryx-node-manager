using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Networking;
using Microsoft.Win32;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Drives the Node page's "one-click data directory download" card (user request: paste a link,
/// it downloads/extracts, node syncs from there). See DataDirDownloadService's doc comment for why
/// no default URL is pre-filled and why Google Drive links aren't supported.
/// </summary>
public partial class DataDirSectionViewModel : ObservableObject
{
    private readonly DataDirDownloadService _downloadService;
    private readonly Func<string> _getDataDirectory;
    private readonly Action<string> _setDataDirectory;
    private readonly Action _persist;

    [ObservableProperty]
    private string? _sourceUrl;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double? _progressPercent;

    public string DataDirectory => _getDataDirectory();

    public DataDirSectionViewModel(
        DataDirDownloadService downloadService,
        Func<string> getDataDirectory,
        Action<string> setDataDirectory,
        Action persist)
    {
        _downloadService = downloadService;
        _getDataDirectory = getDataDirectory;
        _setDataDirectory = setDataDirectory;
        _persist = persist;
    }

    [RelayCommand]
    private void BrowseDataDirectory()
    {
        var dialog = new OpenFolderDialog { Title = AppStrings.Get("Str_DataDir_BrowseDialog_Title") };
        if (dialog.ShowDialog() == true)
        {
            _setDataDirectory(dialog.FolderName);
            _persist();
            OnPropertyChanged(nameof(DataDirectory));
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceUrl) || !Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri))
        {
            StatusMessage = AppStrings.Get("Str_DataDir_InvalidUrl");
            return;
        }

        var targetDir = string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeryxNodeManager", "NodeDataDir")
            : DataDirectory;

        IsBusy = true;
        ProgressPercent = 0;
        StatusMessage = AppStrings.Format("Str_DataDir_StartingDownload", DataDirDownloadService.IsTorrentUrl(SourceUrl) ? "torrent" : "HTTP");
        try
        {
            var progress = new Progress<DataDirDownloadProgress>(p =>
            {
                ProgressPercent = p.TotalBytes is long total && total > 0
                    ? 100.0 * p.BytesReceived / total
                    : ProgressPercent;
                StatusMessage = p.Phase;
            });

            await _downloadService.DownloadAndExtractAsync(uri, targetDir, progress, CancellationToken.None);

            _setDataDirectory(targetDir);
            _persist();
            OnPropertyChanged(nameof(DataDirectory));
            StatusMessage = AppStrings.Format("Str_DataDir_DownloadComplete", targetDir);
        }
        catch (Exception ex)
        {
            StatusMessage = AppStrings.Format("Str_DataDir_DownloadFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = null;
        }
    }
}
