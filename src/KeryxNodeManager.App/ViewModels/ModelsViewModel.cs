using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.ModelsManagement;
using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// One tier's card on the Models page: install status (checked against the real file on disk,
/// never assumed), a user-entered mirror URL + optional checksum, and download/pause/cancel/
/// delete controls wired to the real Core.ModelsManagement.ModelDownloader (brief §7).
///
/// The manual "paste a URL you trust" field below has no URL pre-filled for any tier, by design -
/// but each tier also now offers a one-click "Скачать официальную модель" button
/// (DownloadOfficialCommand) that installs from KeryxNodeManager.Core.ModelsManagement.
/// OfficialModelMirrors, a small hardcoded table of mirrors the Keryx Labs team announced and this
/// app independently verified live (see that class's own doc comment for the verification method/
/// date) - added specifically to remove the "you must go find and paste a URL yourself" friction
/// for a mainstream, non-developer user. The manual field remains for anyone who wants a different
/// or newer mirror. Either way, the miner's own IPFS auto-download still works with no URL at all -
/// this page is a convenience for pre-staging models before first launch, not the only way to get
/// them.
/// </summary>
public partial class ModelCardViewModel : ObservableObject
{
    public ModelSpec Spec { get; }

    /// <summary>Fires at the end of every RefreshState() call - i.e. every time this card's
    /// installed/paused/size state has just been re-checked against the real filesystem (download
    /// completing, delete, cancel, or an explicit page-level refresh). ModelsViewModel subscribes
    /// to this on every card so the aggregate disk-usage summary never goes stale after a
    /// per-card action, without each command handler having to remember to call back up itself.</summary>
    public event Action? StateChanged;

    private readonly ProfileStore _profileStore;
    private readonly ModelDownloader _downloader;
    private readonly OfficialModelDownloadService _officialDownloader;
    private CancellationTokenSource? _cts;

    /// <summary>Whether a verified official mirror exists for this tier (see
    /// OfficialModelMirrors) - drives whether the one-click "Скачать официальную модель" button
    /// shows at all. All 5 tiers currently have one, but this stays defensive in case a future
    /// tier is added before its mirror is verified.</summary>
    public bool HasOfficialMirror => OfficialModelMirrors.TryGet(Spec.Tier) is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private string _sourceUrl = "";

    [ObservableProperty]
    private string _expectedSha256 = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _progressIsIndeterminate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _hasPausedDownload;

    /// <summary>Plain negation of IsDownloading, kept as its own property because WPF's built-in
    /// BooleanToVisibilityConverter has no "invert" mode without a custom converter. Split into two
    /// mutually-exclusive flags (this one and ShowResumeButton below) so the View can show a
    /// distinct "Продолжить" label instead of reusing "Скачать" for a resumed download
    /// (PROJECT_STATUS.md "In progress" item 6) - both buttons call the same DownloadCommand
    /// (ModelDownloader.DownloadAsync already handles fresh-vs-resume transparently via HTTP Range,
    /// see its own doc comment), only the button's own label differs.</summary>
    [ObservableProperty]
    private bool _showDownloadButton = true;

    [ObservableProperty]
    private bool _showResumeButton;

    partial void OnIsDownloadingChanged(bool value) => UpdateDownloadButtonVisibility();
    partial void OnHasPausedDownloadChanged(bool value) => UpdateDownloadButtonVisibility();

    private void UpdateDownloadButtonVisibility()
    {
        ShowDownloadButton = !IsDownloading && !HasPausedDownload;
        ShowResumeButton = !IsDownloading && HasPausedDownload;
    }

    public ModelCardViewModel(
        ModelSpec spec, ProfileStore profileStore, ModelDownloader downloader,
        OfficialModelDownloadService officialDownloader)
    {
        Spec = spec;
        _profileStore = profileStore;
        _downloader = downloader;
        _officialDownloader = officialDownloader;

        if (_profileStore.ActiveProfile.ModelSources.TryGetValue(spec.Tier.ToString(), out var saved))
        {
            SourceUrl = saved.Url;
            ExpectedSha256 = saved.ExpectedSha256 ?? "";
        }

        RefreshState();
    }

    /// <summary>Re-checks the actual filesystem state - never trusted from memory, since the file
    /// could have been placed there manually (brief §7 manual-install path) or removed outside
    /// the app.</summary>
    public void RefreshState()
    {
        var modelsDir = _profileStore.ActiveProfile.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(modelsDir))
        {
            StatusText = "Укажите папку моделей на странице «Майнер», чтобы управлять файлами здесь.";
            IsInstalled = false;
            HasPausedDownload = false;
            StateChanged?.Invoke();
            return;
        }

        IsInstalled = ModelFileLocator.IsInstalled(modelsDir, Spec.DirName);
        HasPausedDownload = !IsInstalled && ModelFileLocator.HasPartialDownload(modelsDir, Spec.DirName);

        if (IsInstalled)
        {
            var sizeBytes = ModelFileLocator.GetInstalledSizeBytes(modelsDir, Spec.DirName) ?? 0;
            StatusText = $"Установлена ({FormatSize(sizeBytes)}).";
            ProgressPercent = 100;
        }
        else if (HasPausedDownload)
        {
            var partialBytes = new FileInfo(ModelFileLocator.GetPartialPath(modelsDir, Spec.DirName)).Length;
            StatusText = $"Скачивание приостановлено ({FormatSize(partialBytes)} получено).";
        }
        else if (!IsDownloading)
        {
            StatusText = "Не установлена. Модель также будет скачана автоматически майнером по IPFS " +
                         "при первом запуске - эта страница нужна только для ручной предзагрузки.";
        }

        StateChanged?.Invoke();
    }

    [RelayCommand]
    private async Task SaveSourceAsync()
    {
        _profileStore.ActiveProfile.ModelSources[Spec.Tier.ToString()] = new ModelSourceConfig
        {
            Url = SourceUrl.Trim(),
            ExpectedSha256 = string.IsNullOrWhiteSpace(ExpectedSha256) ? null : ExpectedSha256.Trim(),
        };
        await _profileStore.SaveAsync();
    }

    private bool CanDownload() => !IsDownloading && !string.IsNullOrWhiteSpace(SourceUrl);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var modelsDir = _profileStore.ActiveProfile.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(modelsDir))
        {
            StatusText = "Сначала укажите папку моделей на странице «Майнер».";
            return;
        }
        if (!Uri.TryCreate(SourceUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusText = "URL должен быть полной ссылкой http:// или https://.";
            return;
        }

        await SaveSourceAsync();

        var destination = ModelFileLocator.GetModelPath(modelsDir, Spec.DirName);
        _cts = new CancellationTokenSource();
        IsDownloading = true;
        HasPausedDownload = false;
        ProgressIsIndeterminate = false;
        var expected = string.IsNullOrWhiteSpace(ExpectedSha256) ? null : ExpectedSha256.Trim();

        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            if (p.PercentComplete is double pct)
            {
                ProgressIsIndeterminate = false;
                ProgressPercent = pct;
                StatusText = $"Скачивание... {pct:F1}% ({FormatSize(p.BytesReceived)}" +
                             (p.TotalBytes is long total ? $" из {FormatSize(total)})" : ")");
            }
            else
            {
                ProgressIsIndeterminate = true;
                StatusText = $"Скачивание... {FormatSize(p.BytesReceived)} (общий размер неизвестен)";
            }
        });

        try
        {
            await _downloader.DownloadAsync(uri, destination, progress, _cts.Token, expected);
            StatusText = "Скачивание завершено.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Скачивание приостановлено - нажмите «Продолжить», чтобы возобновить с той же точки.";
        }
        catch (ModelChecksumMismatchException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка скачивания: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            _cts = null;
            RefreshState();
        }
    }

    private bool CanDownloadOfficial() => !IsDownloading && HasOfficialMirror;

    /// <summary>One-click install from the verified official mirror (see OfficialModelMirrors) -
    /// no URL to paste, no path to pick. Falls back to the same
    /// KeryxNodeManager.Core.Config.DefaultInstallPaths.ModelsDirectory the rest of the app now
    /// defaults to if the user genuinely has no ModelsDirectory configured yet (should be rare -
    /// MinerViewModel now defaults this on first load), so this button works even before the user
    /// has ever visited the Miner page.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadOfficial))]
    private async Task DownloadOfficialAsync()
    {
        var mirror = OfficialModelMirrors.TryGet(Spec.Tier);
        if (mirror is null) return;

        var modelsDir = _profileStore.ActiveProfile.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(modelsDir))
        {
            modelsDir = KeryxNodeManager.Core.Config.DefaultInstallPaths.ModelsDirectory;
            _profileStore.ActiveProfile.ModelsDirectory = modelsDir;
            await _profileStore.SaveAsync();
        }

        _cts = new CancellationTokenSource();
        IsDownloading = true;
        HasPausedDownload = false;
        ProgressIsIndeterminate = true;

        var progress = new Progress<OfficialModelDownloadProgress>(p =>
        {
            if (p.TotalBytes is long total && total > 0)
            {
                ProgressIsIndeterminate = false;
                ProgressPercent = 100.0 * p.BytesReceived / total;
                StatusText = $"{p.Phase} {ProgressPercent:F1}% ({FormatSize(p.BytesReceived)} из {FormatSize(total)})";
            }
            else
            {
                ProgressIsIndeterminate = true;
                StatusText = $"{p.Phase} {FormatSize(p.BytesReceived)}";
            }
        });

        try
        {
            // Torrent mirror preferred when available (offloads bandwidth from the single HTTP
            // origin) - same preference DataDirDownloadService's callers use, falls back to the
            // direct HTTP URL for tiers with no torrent (currently just VeryLight).
            var source = mirror.TorrentUrl ?? mirror.DirectUrl;
            await _officialDownloader.DownloadAndInstallAsync(Spec, source, modelsDir, progress, _cts.Token);
            StatusText = "Скачивание завершено.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Скачивание отменено.";
        }
        catch (OfficialModelDownloadException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка скачивания: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            _cts = null;
            RefreshState();
        }
    }

    [RelayCommand]
    private void Pause()
    {
        // Cancelling here deliberately leaves the .part file on disk - DownloadAsync's next
        // invocation resumes from it via HTTP Range (see ModelDownloader doc comment).
        _cts?.Cancel();
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _cts?.Cancel();
        var modelsDir = _profileStore.ActiveProfile.ModelsDirectory;
        if (!string.IsNullOrWhiteSpace(modelsDir))
        {
            ModelDownloader.DeletePartial(ModelFileLocator.GetModelPath(modelsDir, Spec.DirName));
        }
        RefreshState();
    }

    [RelayCommand]
    private void Delete()
    {
        var modelsDir = _profileStore.ActiveProfile.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(modelsDir)) return;
        var path = ModelFileLocator.GetModelPath(modelsDir, Spec.DirName);
        if (!File.Exists(path)) return;

        // A model file can be several GB - previously this deleted immediately on click with no
        // confirmation at all (PROJECT_STATUS.md "In progress" item 6). A confirmation dialog is
        // the standard, low-risk fix for an irreversible, large-file-affecting action; matches the
        // MessageBox.Show(...YesNo...) pattern TrayIconService already uses for its own
        // close-vs-minimize confirmation, rather than inventing a second UI convention.
        var result = MessageBox.Show(
            $"Удалить модель «{Spec.Name}»? Файл будет удалён с диска безвозвратно.",
            "Удаление модели",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        File.Delete(path);
        RefreshState();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var modelsDir = _profileStore.ActiveProfile.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(modelsDir)) return;
        var folder = Path.Combine(modelsDir, Spec.DirName);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    // Internal (not private) so ModelsViewModel can reuse it for the aggregate disk-usage summary
    // below - one formatting rule, not two independently-drifting copies.
    internal static string FormatSize(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024) return $"{mb / 1024:F2} ГБ";
        if (mb >= 1) return $"{mb:F0} МБ";
        return $"{bytes / 1024.0:F0} КБ"; // sub-1MB (e.g. a test download) - "0 МБ" would misleadingly read as empty
    }
}

/// <summary>
/// Drives the Models page (brief §7): one card per ModelTier, each independently
/// downloadable/pausable/resumable/checksummable and re-checked against the actual filesystem
/// state rather than assumed from app memory.
/// </summary>
public partial class ModelsViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;

    public ObservableCollection<ModelCardViewModel> Models { get; } = new();

    /// <summary>Aggregate "how much disk space do my installed models actually take up" summary
    /// (PROJECT_STATUS.md "In progress" item 6) - summed fresh from the real filesystem every time
    /// (constructor + Refresh), never cached/estimated, matching this project's established
    /// "never trust a stale figure" pattern (see ModelCardViewModel.RefreshState's own doc comment).</summary>
    [ObservableProperty]
    private string _totalDiskUsageText = "";

    public ModelsViewModel(ProfileStore profileStore, HttpClient httpClient)
    {
        _profileStore = profileStore;
        var downloader = new ModelDownloader(httpClient);
        var officialDownloader = new OfficialModelDownloadService(httpClient);
        foreach (var spec in ModelTierCatalog.Tiers)
        {
            var card = new ModelCardViewModel(spec, profileStore, downloader, officialDownloader);
            card.StateChanged += RefreshTotalDiskUsage;
            Models.Add(card);
        }
        RefreshTotalDiskUsage();
    }

    [RelayCommand]
    private void Refresh()
    {
        foreach (var card in Models) card.RefreshState();
        RefreshTotalDiskUsage();
    }

    private void RefreshTotalDiskUsage()
    {
        var modelsDir = _profileStore.ActiveProfile.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(modelsDir))
        {
            TotalDiskUsageText = "";
            return;
        }

        long totalBytes = 0;
        int installedCount = 0;
        foreach (var spec in ModelTierCatalog.Tiers)
        {
            if (!ModelFileLocator.IsInstalled(modelsDir, spec.DirName)) continue;
            totalBytes += ModelFileLocator.GetInstalledSizeBytes(modelsDir, spec.DirName) ?? 0;
            installedCount++;
        }

        TotalDiskUsageText = installedCount == 0
            ? ""
            : $"Установлено моделей: {installedCount}, занято на диске: {ModelCardViewModel.FormatSize(totalBytes)}.";
    }
}
