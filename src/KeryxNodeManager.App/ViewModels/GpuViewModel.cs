using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.ModelAssignment;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Safety;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>One selectable entry in a GPU card's mode dropdown: "auto"/"disabled"/a tier name.</summary>
public sealed record GpuModeOption(string Value, string Label);

public partial class GpuCardViewModel : ObservableObject
{
    public required GpuDevice Device { get; init; }

    /// <summary>Overclock/fan-control section for this card (tasks #108-111) - always constructed
    /// (never null) so the View can bind to it unconditionally; GpuOverclockSectionViewModel.IsSupported
    /// is what actually gates whether its controls are shown, since support is only knowable after a
    /// real NVAPI call, not from GpuDevice alone.</summary>
    public required GpuOverclockSectionViewModel Overclock { get; init; }

    /// <summary>
    /// Set by GpuViewModel after the card's Mode has been restored from the persisted profile, so
    /// that restoring a saved value on refresh does not itself count as a user edit and re-save.
    /// Only user-driven ComboBox selections (which happen after this is set) trigger persistence.
    /// </summary>
    internal Action<GpuCardViewModel>? ModeChanged;

    [ObservableProperty]
    private string _assignmentSummary = "";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _mode = GpuAssignmentMode.Auto; // "auto" | "disabled" | ModelTier name

    partial void OnModeChanged(string value) => ModeChanged?.Invoke(this);
}

/// <summary>
/// Drives the GPU page: queries IGpuInfoProvider on a timer, runs each card through TierAssigner
/// for the Auto-mode preview shown before launch (brief §6 "показывать пользователю итоговое
/// назначение до запуска"). Deliberately never sums VRAM across cards - each GpuCardViewModel is
/// independent, which is itself the guarantee against implying VRAM pooling (brief §6 critical
/// constraint).
///
/// Per-GPU mode choices (Auto/Disabled/a specific tier) are persisted to
/// MiningProfile.GpuAssignments (keyed by GPU UUID) via ProfileStore as soon as the user changes
/// them, and are what GpuAssignmentResolver reads at launch time (DashboardViewModel) and preview
/// time (MinerViewModel) - so a choice made here is guaranteed to be the choice that's actually
/// used, not a UI-only setting that silently gets ignored.
/// </summary>
public partial class GpuViewModel : ObservableObject
{
    private readonly IGpuInfoProvider _gpuInfoProvider;
    private readonly TierAssigner _tierAssigner;
    private readonly ProfileStore _profileStore;
    private readonly IGpuOverclockController _overclockController;
    private readonly SafetyMonitor _safetyMonitor;

    public ObservableCollection<GpuCardViewModel> Gpus { get; } = new();

    /// <summary>Static option list for the mode ComboBox - Auto, Disabled, then one entry per
    /// ModelTier in the same descending-VRAM order the tier table itself uses.</summary>
    public static IReadOnlyList<GpuModeOption> ModeOptions { get; } = BuildModeOptions();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string? _lastError;

    public GpuViewModel(
        IGpuInfoProvider gpuInfoProvider,
        TierAssigner tierAssigner,
        ProfileStore profileStore,
        IGpuOverclockController overclockController,
        SafetyMonitor safetyMonitor)
    {
        _gpuInfoProvider = gpuInfoProvider;
        _tierAssigner = tierAssigner;
        _profileStore = profileStore;
        _overclockController = overclockController;
        _safetyMonitor = safetyMonitor;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        LastError = null;
        try
        {
            var devices = await _gpuInfoProvider.QueryAsync();
            // The real NVAPI-backed controller matches a GPU UUID to a PhysicalGPU by list
            // position (see NvApiGpuOverclockController.ResolveGpu's doc comment) - it needs the
            // exact device list this same query just produced, refreshed every time in case a GPU
            // was hot-plugged/removed since the last refresh. MockGpuOverclockController (--mock
            // runs) has no such concept and ignores this call entirely.
            (_overclockController as KeryxNodeManager.App.Gpu.NvApiGpuOverclockController)?.SetKnownDevices(devices);

            var profile = _profileStore.ActiveProfile;
            Gpus.Clear();
            foreach (var device in devices)
            {
                var overclock = new GpuOverclockSectionViewModel(
                    _overclockController,
                    _safetyMonitor,
                    device.Uuid,
                    device.Name,
                    getPersisted: () => profile.GpuOverclockSettings.TryGetValue(device.Uuid, out var s) ? s : null,
                    setPersisted: settings => profile.GpuOverclockSettings[device.Uuid] = settings,
                    persist: () => _ = _profileStore.SaveAsync());

                var card = new GpuCardViewModel { Device = device, Overclock = overclock };
                var saved = profile.GpuAssignments.FirstOrDefault(a => a.GpuUuid == device.Uuid);
                // Restore the persisted mode quietly (ModeChanged not yet hooked), then compute the
                // summary explicitly - the ObservableProperty partial only fires on an actual value
                // change, so relying on it here would skip the "saved mode equals the default"
                // case (e.g. an explicitly-saved "auto").
                card.Mode = saved?.Mode ?? GpuAssignmentMode.Auto;
                ApplySummaryForMode(card);
                card.ModeChanged = OnCardModeChanged;
                Gpus.Add(card);

                // Fire-and-forget: querying current clock offsets/fan state is a read-only NVAPI
                // call (safe to run unattended, unlike Apply/Reset) but still real I/O against a
                // native driver API - must not block the GPU page's own refresh on it, especially
                // since a card with no exposed cooler/clock-boost table will throw here (handled
                // inside LoadAsync itself, not here).
                _ = overclock.LoadAsync();
            }
        }
        catch (GpuQueryException ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void OnCardModeChanged(GpuCardViewModel card)
    {
        ApplySummaryForMode(card);

        var profile = _profileStore.ActiveProfile;
        profile.GpuAssignments.RemoveAll(a => a.GpuUuid == card.Device.Uuid);
        profile.GpuAssignments.Add(new GpuAssignment { GpuUuid = card.Device.Uuid, Mode = card.Mode });
        // Fire-and-forget save: this mirrors the Node/Miner pages' explicit Save button in intent
        // (persist to settings.json) but happens immediately on selection since a dropdown pick,
        // unlike a text field, has no separate "confirm" step the user expects.
        _ = _profileStore.SaveAsync();
    }

    private void ApplySummaryForMode(GpuCardViewModel card)
    {
        if (card.Mode == GpuAssignmentMode.Auto)
        {
            var result = _tierAssigner.AssignAuto(card.Device);
            card.AssignmentSummary = result.Explanation;
            card.IsEnabled = !result.Disabled;
            return;
        }

        if (card.Mode == GpuAssignmentMode.Disabled)
        {
            card.AssignmentSummary = $"{card.Device.Name}: майнинг на этой GPU отключён вручную.";
            card.IsEnabled = false;
            return;
        }

        if (Enum.TryParse<ModelTier>(card.Mode, out var tier))
        {
            var result = _tierAssigner.AssignManual(card.Device, tier);
            card.AssignmentSummary = result.Explanation;
            card.IsEnabled = true;
            return;
        }

        // Unknown persisted mode string (e.g. an old profile from a future version with a tier
        // that no longer exists) - fail safe to Auto rather than silently mining nothing.
        card.Mode = GpuAssignmentMode.Auto;
    }

    private static List<GpuModeOption> BuildModeOptions()
    {
        var options = new List<GpuModeOption>
        {
            new(GpuAssignmentMode.Auto, "Авто"),
            new(GpuAssignmentMode.Disabled, "Отключено"),
        };
        options.AddRange(ModelTierCatalog.Tiers
            .OrderByDescending(t => t.MinVramMb)
            .Select(t => new GpuModeOption(t.Tier.ToString(), $"Вручную: {t.Name}")));
        return options;
    }
}
