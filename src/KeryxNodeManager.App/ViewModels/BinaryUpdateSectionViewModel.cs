using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.ModelsManagement;
using KeryxNodeManager.Core.Updates;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Drives one binary's (keryxd.exe or keryx-miner.exe) "check for update / install update" UI -
/// composed into both NodeViewModel and MinerViewModel rather than duplicated, since the two flows
/// are identical apart from which GitHub repo and which MiningProfile field they touch (see
/// KeryxRepos in Core for the per-kind repo/exe-name mapping this reuses).
///
/// Deliberately never auto-applies an update: CheckForUpdateCommand only populates
/// LatestVersion/UpdateAvailable, and InstallUpdateCommand is a distinct, explicit user action -
/// replacing a binary the user might currently have running is not something this app does
/// silently in the background (same reasoning as BinaryUpdateService's own doc comment). If the
/// target file is still locked by a running process, File.Copy inside ApplyUpdate throws an
/// IOException, which this class surfaces as a clear "stop it first" message rather than a raw
/// stack trace - this ViewModel has no handle on the actual running process (ProcessSupervisor
/// instances are owned by DashboardViewModel, not shared here), so it cannot stop it itself; the
/// user is directed to the Dashboard instead.
///
/// InstallUpdateAsync used to hard-require the caller to already have typed/browsed to an
/// executable path before it would do anything ("Сначала укажите путь..."). That was exactly the
/// kind of friction a first-time, non-developer user should never have to deal with (brief
/// follow-up, 2026-08-03) - if no path is configured yet, this now auto-picks
/// KeryxNodeManager.Core.Config.DefaultInstallPaths.ExecutablePathFor(kind) and persists it via
/// setExecutablePath, so clicking one button is enough to go from "nothing installed" to "running
/// binary" with zero manual path entry. BrowseExecutable on Node/Miner pages still lets anyone
/// point at a different/existing install instead.
/// </summary>
public partial class BinaryUpdateSectionViewModel : ObservableObject
{
    private readonly BinaryUpdateService _updateService;
    private readonly ManagedBinaryKind _kind;
    private readonly Func<string> _getExecutablePath;
    private readonly Action<string> _setExecutablePath;
    private readonly Func<string?> _getInstalledVersion;
    private readonly Action<string> _setInstalledVersion;
    private readonly Action _persist;

    private Uri? _pendingDownloadUrl;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double? _progressPercent;

    public string? InstalledVersion => _getInstalledVersion();

    /// <summary>True once this binary has never been installed by this app AND no path is
    /// configured - drives the UI toward a single prominent "Установить автоматически" button
    /// instead of the check/install pair that assumes a path already exists.</summary>
    public bool NeedsFirstInstall => string.IsNullOrWhiteSpace(_getExecutablePath());

    public BinaryUpdateSectionViewModel(
        BinaryUpdateService updateService,
        ManagedBinaryKind kind,
        Func<string> getExecutablePath,
        Action<string> setExecutablePath,
        Func<string?> getInstalledVersion,
        Action<string> setInstalledVersion,
        Action persist)
    {
        _updateService = updateService;
        _kind = kind;
        _getExecutablePath = getExecutablePath;
        _setExecutablePath = setExecutablePath;
        _getInstalledVersion = getInstalledVersion;
        _setInstalledVersion = setInstalledVersion;
        _persist = persist;
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _updateService.CheckAsync(_kind, _getInstalledVersion(), CancellationToken.None);
            LatestVersion = result.LatestVersion;
            UpdateAvailable = result.UpdateAvailable;
            _pendingDownloadUrl = result.DownloadUrl;
            OnPropertyChanged(nameof(InstalledVersion));

            StatusMessage = result.UpdateAvailable
                ? (result.DownloadUrl is null
                    ? $"Доступна версия {result.LatestVersion}, но в этом релизе нет Windows-архива (win64-amd64.zip)."
                    : $"Доступно обновление: {result.LatestVersion}.")
                : "Установлена последняя версия.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось проверить обновления: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_pendingDownloadUrl is null)
        {
            await CheckForUpdateAsync();
            if (_pendingDownloadUrl is null) return;
        }

        var exePath = _getExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            // No path configured yet - auto-default rather than block the user (see class doc
            // comment). Persisted immediately so a crash/close mid-download doesn't lose the
            // choice, and so BrowseExecutable's "current path" display picks it up too.
            exePath = DefaultInstallPaths.ExecutablePathFor(_kind);
            _setExecutablePath(exePath);
            _persist();
            OnPropertyChanged(nameof(NeedsFirstInstall));
        }

        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            var workDir = Path.Combine(Path.GetTempPath(), "KeryxNodeManagerUpdates", _kind.ToString());
            var progress = new Progress<ModelDownloadProgress>(p =>
                ProgressPercent = p.TotalBytes is long total && total > 0 ? 100.0 * p.BytesReceived / total : ProgressPercent);

            var extractedExePath = await _updateService.DownloadAndExtractAsync(
                _kind, _pendingDownloadUrl, workDir, progress, CancellationToken.None);

            _updateService.ApplyUpdate(extractedExePath, exePath);

            _setInstalledVersion(LatestVersion ?? "");
            _persist();
            OnPropertyChanged(nameof(InstalledVersion));
            UpdateAvailable = false;
            StatusMessage = $"Обновлено до {LatestVersion}.";
        }
        catch (IOException ex)
        {
            StatusMessage = "Не удалось заменить файл - похоже, процесс ещё запущен. Остановите его на " +
                             $"странице Dashboard и попробуйте снова. ({ex.Message})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка обновления: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = null;
        }
    }
}
