using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Diagnostics;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.ModelAssignment;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Validation;
using Microsoft.Win32;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>One row in the wizard's system-checks step (brief §4 step 1) - a direct 1:1 mapping
/// of Core.Diagnostics.SystemCheckResult, kept as its own type only so the View doesn't bind
/// straight to a Core record (keeps the ViewModel layer able to add UI-only fields later without
/// touching Core).</summary>
public sealed record WizardCheckRow(string Name, bool Passed, string Detail, bool Required);

/// <summary>One row in the wizard's GPU/tier preview step (brief §4 step 7) - purely informational:
/// shows what Auto-assignment would pick for each detected GPU right now. The user's actual,
/// persisted per-card choice is still made on the real GPU page after the wizard - this step
/// exists so a first-time user isn't launching blind, not to duplicate GpuViewModel's editing UI.</summary>
public sealed record WizardGpuPreviewRow(string Name, string Explanation);

/// <summary>
/// Drives the first-run wizard (brief §4): a linear sequence of steps that write directly into the
/// same ProfileStore.ActiveProfile / ProfileStore.Settings instances the rest of the app already
/// binds to, so nothing entered here is a separate, throwaway copy of the configuration - Finish
/// (or Skip) calls the exact same ProfileStore.SaveAsync() that the Node/Miner/GPU pages call.
///
/// Steps: 0 Welcome, 1 System checks, 2 Directories, 3 Mining address, 4 GPU/tier preview,
/// 5 Autostart, 6 Finish/summary. System checks run automatically on entering step 1; the GPU
/// preview refreshes automatically on entering step 4 - both re-query real state rather than
/// caching a snapshot taken at wizard-open time, since the user may plug in a GPU or install a
/// driver mid-wizard.
/// </summary>
public partial class WizardViewModel : ObservableObject
{
    public const int StepCount = 7;

    private readonly ProfileStore _profileStore;
    private readonly IGpuInfoProvider _gpuInfoProvider;
    private readonly TierAssigner _tierAssigner;

    public MiningProfile Profile => _profileStore.ActiveProfile;

    /// <summary>Raised once with `true` when Finish/Skip completes the wizard (WizardWindow's
    /// code-behind subscribes and closes the window) - kept as a plain event rather than a
    /// RelayCommand-driven Window.Close() call so the ViewModel stays Window-agnostic and
    /// unit-testable.</summary>
    public event Action? WizardCompleted;

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private bool _isStep0Welcome = true;
    [ObservableProperty]
    private bool _isStep1SystemChecks;
    [ObservableProperty]
    private bool _isStep2Directories;
    [ObservableProperty]
    private bool _isStep3Address;
    [ObservableProperty]
    private bool _isStep4Gpu;
    [ObservableProperty]
    private bool _isStep5Autostart;
    [ObservableProperty]
    private bool _isStep6Finish;

    /// <summary>Plain negation of IsStep6Finish, kept as its own bindable property since WPF's
    /// built-in BooleanToVisibilityConverter has no "invert" mode without a custom converter -
    /// drives the "Далее" button's visibility (shown on every step except the last, where
    /// "Завершить" takes its place).</summary>
    [ObservableProperty]
    private bool _isNotLastStep = true;

    [ObservableProperty]
    private string _stepHeader = $"Шаг 1 из {StepCount}";

    [ObservableProperty]
    private bool _isRunningChecks;

    [ObservableProperty]
    private string? _directoriesError;

    [ObservableProperty]
    private string? _addressValidationMessage;

    // Mirrors of Profile.{Node,Miner}ExecutablePath/ModelsDirectory, bound TwoWay from the View
    // instead of binding straight to the plain (non-INotifyPropertyChanged) MiningProfile
    // properties. MiningProfile is a bare data class shared with JSON serialization - it doesn't
    // implement INotifyPropertyChanged, so a TextBox bound directly to Profile.ModelsDirectory
    // updates the model but never tells NextCommand's CanExecute to re-run, leaving "Далее"
    // permanently disabled after typing (only a file-dialog pick used to work, since that path
    // called NotifyCanExecuteChanged explicitly). These *Input properties close that gap: every
    // keystroke both writes through to Profile and re-queries CanGoNext.
    [ObservableProperty]
    private string _nodeExecutablePathInput = "";

    [ObservableProperty]
    private string _minerExecutablePathInput = "";

    [ObservableProperty]
    private string _modelsDirectoryInput = "";

    /// <summary>User-typed name for the "create a new profile from the wizard" option on step 0
    /// (PROJECT_STATUS.md "In progress" item 10) - a returning user previously had no way to run
    /// the wizard against a second rig without first creating+switching to a new profile via the
    /// Profiles page, then reopening the wizard. Kept separate from ProfilesViewModel's own
    /// NewProfileName - the wizard doesn't share a ProfilesViewModel instance.</summary>
    [ObservableProperty]
    private string _newProfileNameInput = "";

    [ObservableProperty]
    private string? _createProfileMessage;

    /// <summary>Drives the Finish step's address highlight (PROJECT_STATUS.md "In progress" item
    /// 7) - the wizard already warns on step 3 if the address doesn't look valid but still lets
    /// the user proceed (matching the Miner page's own leniency), so a user who dismissed that
    /// warning and clicked through the remaining steps could reach Finish with no visual reminder
    /// that the address is still off. Recomputed on entering step 6 rather than on every keystroke
    /// - Profile is a bare data class with no INotifyPropertyChanged, so there is no cheaper hook
    /// to recompute this from, and step 6 is the only place it's displayed.</summary>
    [ObservableProperty]
    private bool _isAddressValid = true;

    public ObservableCollection<WizardCheckRow> Checks { get; } = new();
    public ObservableCollection<WizardGpuPreviewRow> GpuPreview { get; } = new();

    public WizardViewModel(ProfileStore profileStore, IGpuInfoProvider gpuInfoProvider, TierAssigner tierAssigner)
    {
        _profileStore = profileStore;
        _gpuInfoProvider = gpuInfoProvider;
        _tierAssigner = tierAssigner;

        _nodeExecutablePathInput = Profile.NodeExecutablePath;
        _minerExecutablePathInput = Profile.MinerExecutablePath;
        _modelsDirectoryInput = Profile.ModelsDirectory;
    }

    partial void OnNodeExecutablePathInputChanged(string value) => Profile.NodeExecutablePath = value;

    partial void OnMinerExecutablePathInputChanged(string value) => Profile.MinerExecutablePath = value;

    partial void OnModelsDirectoryInputChanged(string value)
    {
        Profile.ModelsDirectory = value;
        // Clear a stale error as soon as the user starts correcting it, rather than leaving a red
        // message from a previous invalid value up while they're mid-edit of a new one.
        DirectoriesError = null;
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        IsStep0Welcome = value == 0;
        IsStep1SystemChecks = value == 1;
        IsStep2Directories = value == 2;
        IsStep3Address = value == 3;
        IsStep4Gpu = value == 4;
        IsStep5Autostart = value == 5;
        IsStep6Finish = value == 6;
        IsNotLastStep = value != 6;
        StepHeader = $"Шаг {value + 1} из {StepCount}";

        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        if (value == 1) _ = RunSystemChecksAsync();
        if (value == 4) _ = RefreshGpuPreviewAsync();
        if (value == 6) IsAddressValid = KeryxAddressValidator.LooksValid(Profile.MiningAddress);
    }

    private bool CanGoBack() => CurrentStepIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentStepIndex > 0) CurrentStepIndex--;
    }

    private bool CanGoNext()
    {
        if (CurrentStepIndex >= StepCount - 1) return false;
        if (CurrentStepIndex == 2) return PathValidator.Validate(ModelsDirectoryInput).IsValid;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (CurrentStepIndex == 2)
        {
            var validation = PathValidator.Validate(ModelsDirectoryInput);
            if (!validation.IsValid)
            {
                DirectoriesError = validation.Error;
                return;
            }
            DirectoriesError = null;
        }
        if (CurrentStepIndex == 3)
        {
            ValidateAddress();
        }
        if (CurrentStepIndex < StepCount - 1) CurrentStepIndex++;
    }

    /// <summary>Creates a new, blank-defaults profile via the same ProfileStore.CreateProfileAsync
    /// the Profiles page uses, switches to it, and re-seeds this wizard's own input-mirror
    /// properties from it (PROJECT_STATUS.md "In progress" item 10). Deliberately only offered on
    /// step 0, before any of the wizard's own steps have been filled in - creating a profile
    /// mid-wizard (e.g. after already typing a mining address for the *old* profile) would silently
    /// discard that in-progress input, which would be surprising; step 0 has nothing yet to lose.</summary>
    [RelayCommand]
    private async Task CreateNewProfileAsync()
    {
        var name = NewProfileNameInput.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CreateProfileMessage = "Введите имя нового профиля.";
            return;
        }
        try
        {
            await _profileStore.CreateProfileAsync(name);
            NewProfileNameInput = "";

            // Profile is a live getter over ProfileStore.ActiveProfile - it now points at the
            // freshly-created profile. Re-seed the wizard's own *Input mirror properties (see their
            // doc comment above: MiningProfile has no INotifyPropertyChanged, so nothing re-reads
            // it automatically) and clear any validation state left over from the previous profile.
            NodeExecutablePathInput = Profile.NodeExecutablePath;
            MinerExecutablePathInput = Profile.MinerExecutablePath;
            ModelsDirectoryInput = Profile.ModelsDirectory;
            DirectoriesError = null;
            AddressValidationMessage = null;
            OnPropertyChanged(nameof(Profile));

            CreateProfileMessage = $"Профиль «{name}» создан и стал активным — мастер теперь настраивает именно его.";
        }
        catch (Exception ex)
        {
            CreateProfileMessage = $"Не удалось создать профиль: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ValidateAddress()
    {
        AddressValidationMessage = KeryxAddressValidator.LooksValid(Profile.MiningAddress)
            ? null
            : "Адрес не похож на действительный Keryx-адрес (ожидается keryx:... или keryxtest:...). " +
              "Мастер позволяет продолжить, но запуск майнинга с неверным адресом будет отклонён узлом.";
    }

    [RelayCommand]
    private async Task RunSystemChecksAsync()
    {
        IsRunningChecks = true;
        Checks.Clear();
        try
        {
            Checks.Add(ToRow(SystemChecker.CheckWindowsVersion()));
            Checks.Add(ToRow(await SystemChecker.CheckNvidiaAsync(_gpuInfoProvider)));
            Checks.Add(ToRow(await SystemChecker.CheckWslAsync()));
            Checks.Add(ToRow(SystemChecker.CheckDocker()));
        }
        finally
        {
            IsRunningChecks = false;
        }
    }

    private async Task RefreshGpuPreviewAsync()
    {
        GpuPreview.Clear();
        try
        {
            var devices = await _gpuInfoProvider.QueryAsync();
            foreach (var device in devices.OrderBy(d => d.CudaIndex))
            {
                var result = _tierAssigner.AssignAuto(device);
                GpuPreview.Add(new WizardGpuPreviewRow(device.Name, result.Explanation));
            }
            if (devices.Count == 0)
            {
                GpuPreview.Add(new WizardGpuPreviewRow("—", "Видеокарты NVIDIA не обнаружены."));
            }
        }
        catch (GpuQueryException ex)
        {
            GpuPreview.Add(new WizardGpuPreviewRow("—", ex.Message));
        }
    }

    [RelayCommand]
    private void BrowseNodeExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите keryxd.exe",
            Filter = "keryxd.exe|keryxd.exe|Исполняемые файлы (*.exe)|*.exe",
        };
        if (dialog.ShowDialog() == true)
        {
            NodeExecutablePathInput = dialog.FileName; // partial handler writes through to Profile
        }
    }

    [RelayCommand]
    private void BrowseMinerExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите keryx-miner.exe",
            Filter = "keryx-miner.exe|keryx-miner.exe|Исполняемые файлы (*.exe)|*.exe",
        };
        if (dialog.ShowDialog() == true)
        {
            MinerExecutablePathInput = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseModelsDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку для моделей" };
        if (dialog.ShowDialog() == true)
        {
            ModelsDirectoryInput = dialog.FolderName; // partial handler validates + writes through
        }
    }

    /// <summary>Persists whatever has been entered so far and marks the wizard seen. Called by
    /// both Finish (all steps reviewed) and Skip (user jumped out early) - both are honest "save
    /// what's here" operations, never a silent discard, so a user who fills in the address then
    /// hits Skip doesn't lose it.</summary>
    [RelayCommand]
    private async Task FinishAsync()
    {
        _profileStore.Settings.FirstRunCompleted = true;
        await _profileStore.SaveAsync();
        WizardCompleted?.Invoke();
    }

    private static WizardCheckRow ToRow(Core.Diagnostics.SystemCheckResult r) =>
        new(r.Name, r.Passed, r.Detail, r.Required);
}
