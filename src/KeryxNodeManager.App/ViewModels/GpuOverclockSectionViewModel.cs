using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Safety;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Drives one GPU card's overclock/fan-control section (tasks #108-111). Deliberately reads the
/// LIVE state from IGpuOverclockController on load (not the persisted MiningProfile.GpuOverclockSettings
/// value) - a reboot or driver reset silently returns the card to stock, and showing a stale
/// "requested" value as if it were the current one would be actively misleading. The persisted
/// value only records "what the user last asked for," written back here purely so a future
/// auto-re-apply feature (see MiningProfile.GpuOverclockSettings' doc comment) has something to
/// read - this pass does not implement that auto-re-apply itself.
///
/// Every hardware-affecting action (ApplyAsync/ResetAsync) is gated behind a MessageBox.Show(...
/// YesNo...) confirmation, matching the same low-risk-irreversible-action pattern
/// ModelsViewModel.Delete already established for this app, PLUS an extra SafetyMonitor.GetLastLevel
/// check that refuses to apply a NEW overclock while the card is currently Critical (a card already
/// too hot is exactly the wrong moment to push it further, even if the requested offset itself is
/// conservative) - Reset is deliberately NOT blocked by this check, since resetting to stock can
/// only help a card that's already in trouble.
/// </summary>
public partial class GpuOverclockSectionViewModel : ObservableObject
{
    /// <summary>
    /// 0.2.7 feature flag (brief follow-up after 0.2.6's visual acceptance review): the overclock
    /// UI (sliders, Manual fan, Apply/Reset, and the "unavailable"/raw-NVAPI-error message) is
    /// hidden from GpuView.xaml regardless of <see cref="IsSupported"/> until a real hardware
    /// Apply/write has actually been performed and verified with the user present - see
    /// PROJECT_STATUS.md for why no real Apply has been done yet. Flip this back to true (and
    /// remove the hardcoded Visibility="Collapsed" overrides in GpuView.xaml that reference this
    /// constant in their comments) once that verification happens. Backend code
    /// (IGpuOverclockController/NvApiGpuOverclockController/MockGpuOverclockController and this
    /// ViewModel) is deliberately left fully intact - only the View is gated.
    /// </summary>
    public const bool UiFeatureEnabled = false;

    private readonly IGpuOverclockController _controller;
    private readonly SafetyMonitor _safetyMonitor;
    private readonly string _gpuUuid;
    private readonly string _deviceName;
    private readonly Func<GpuOverclockSettings?> _getPersisted;
    private readonly Action<GpuOverclockSettings> _setPersisted;
    private readonly Action _persist;

    /// <summary>False once a real GetCapabilitiesAsync/GetCurrentStateAsync call has failed (e.g.
    /// no NVAPI-controllable cooler, or NVAPI/nvidia-smi enumeration mismatch - see
    /// NvApiGpuOverclockController's own exceptions) - the card still shows temperature/VRAM from
    /// IGpuInfoProvider either way, only this section's controls are hidden.</summary>
    [ObservableProperty]
    private bool _isSupported = true;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private int _minCoreOffsetMhz;

    [ObservableProperty]
    private int _maxCoreOffsetMhz;

    [ObservableProperty]
    private int _minMemoryOffsetMhz;

    [ObservableProperty]
    private int _maxMemoryOffsetMhz;

    [ObservableProperty]
    private bool _supportsFanControl;

    [ObservableProperty]
    private int _minFanPercent;

    [ObservableProperty]
    private int _maxFanPercent = 100;

    [ObservableProperty]
    private int _coreOffsetMhz;

    [ObservableProperty]
    private int _memoryOffsetMhz;

    /// <summary>True = user wants a fixed fan percent (FanPercent below); false = automatic/
    /// driver-controlled curve (the safe default - matches GpuOverclockSettings.FanSpeedPercent's
    /// null-means-auto convention).</summary>
    [ObservableProperty]
    private bool _fanIsManual;

    [ObservableProperty]
    private int _fanPercent = 50;

    public GpuOverclockSectionViewModel(
        IGpuOverclockController controller,
        SafetyMonitor safetyMonitor,
        string gpuUuid,
        string deviceName,
        Func<GpuOverclockSettings?> getPersisted,
        Action<GpuOverclockSettings> setPersisted,
        Action persist)
    {
        _controller = controller;
        _safetyMonitor = safetyMonitor;
        _gpuUuid = gpuUuid;
        _deviceName = deviceName;
        _getPersisted = getPersisted;
        _setPersisted = setPersisted;
        _persist = persist;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        LastError = null;
        try
        {
            var caps = await _controller.GetCapabilitiesAsync(_gpuUuid);
            MinCoreOffsetMhz = caps.MinCoreClockOffsetMhz;
            MaxCoreOffsetMhz = caps.MaxCoreClockOffsetMhz;
            MinMemoryOffsetMhz = caps.MinMemoryClockOffsetMhz;
            MaxMemoryOffsetMhz = caps.MaxMemoryClockOffsetMhz;
            SupportsFanControl = caps.SupportsFanControl;
            MinFanPercent = caps.MinFanPercent;
            MaxFanPercent = caps.MaxFanPercent;

            var state = await _controller.GetCurrentStateAsync(_gpuUuid);
            CoreOffsetMhz = state.CoreClockOffsetMhz;
            MemoryOffsetMhz = state.MemoryClockOffsetMhz;
            FanIsManual = !state.FanIsAutoControlled;
            if (state.FanSpeedPercent is int pct) FanPercent = pct;

            IsSupported = true;
            IsLoaded = true;
        }
        catch (GpuOverclockException ex)
        {
            // Genuinely expected on hardware without exposed clock-boost/cooler control (see
            // NvApiGpuOverclockController's own exception messages) - not a bug, just "this card
            // doesn't support it," so the section hides its controls rather than showing a
            // misleading error banner over an unusable form.
            IsSupported = false;
            LastError = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_safetyMonitor.GetLastLevel(_gpuUuid) == SafetyLevel.Critical)
        {
            LastError = AppStrings.Format("Str_Overclock_CriticalTemp", _deviceName);
            return;
        }

        var fanLine = FanIsManual
            ? AppStrings.Format("Str_Overclock_FanManual", FanPercent)
            : AppStrings.Get("Str_Overclock_FanAuto");
        var coreStr = (CoreOffsetMhz >= 0 ? "+" : "") + CoreOffsetMhz;
        var memStr = (MemoryOffsetMhz >= 0 ? "+" : "") + MemoryOffsetMhz;
        var result = MessageBox.Show(
            AppStrings.Format("Str_Overclock_ApplyConfirm_Body", _deviceName, coreStr, memStr, fanLine),
            AppStrings.Get("Str_Overclock_ApplyConfirm_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        LastError = null;
        StatusMessage = null;
        try
        {
            await _controller.ApplyClockOffsetsAsync(_gpuUuid, CoreOffsetMhz, MemoryOffsetMhz);
            await _controller.ApplyFanSpeedAsync(_gpuUuid, FanIsManual ? FanPercent : null);

            _setPersisted(new GpuOverclockSettings
            {
                CoreClockOffsetMhz = CoreOffsetMhz,
                MemoryClockOffsetMhz = MemoryOffsetMhz,
                FanSpeedPercent = FanIsManual ? FanPercent : null,
            });
            _persist();

            StatusMessage = AppStrings.Get("Str_Overclock_Applied");
        }
        catch (GpuOverclockException ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApply() => IsSupported && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ResetAsync()
    {
        var result = MessageBox.Show(
            AppStrings.Format("Str_Overclock_ResetConfirm_Body", _deviceName),
            AppStrings.Get("Str_Overclock_ResetConfirm_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        LastError = null;
        StatusMessage = null;
        try
        {
            await _controller.ResetToDefaultsAsync(_gpuUuid);
            CoreOffsetMhz = 0;
            MemoryOffsetMhz = 0;
            FanIsManual = false;

            _setPersisted(new GpuOverclockSettings());
            _persist();

            StatusMessage = AppStrings.Get("Str_Overclock_ResetDone");
        }
        catch (GpuOverclockException ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsSupportedChanged(bool value) => ApplyCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }
}
