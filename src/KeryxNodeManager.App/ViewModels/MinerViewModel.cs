using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Cli;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Gpu;
using KeryxNodeManager.Core.ModelAssignment;
using KeryxNodeManager.Core.ModelsManagement;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.Updates;
using KeryxNodeManager.Core.Validation;
using Microsoft.Win32;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Collects the fields MinerArgumentBuilder/NativeWindowsRuntimeBackend need to actually launch
/// keryx-miner.exe (brief §9). Advanced mode shows the real command line that would be executed -
/// built by the exact same MinerArgumentBuilder.Build() the real launch path uses, so the
/// preview can never drift from what actually runs (brief §6 "показывать пользователю итоговое
/// назначение до запуска", §9 "полный preview команды").
/// </summary>
public partial class MinerViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;
    private readonly IGpuInfoProvider _gpuInfoProvider;
    private readonly TierAssigner _tierAssigner;

    public MiningProfile Profile => _profileStore.ActiveProfile;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _addressValidationMessage;

    [ObservableProperty]
    private bool _advancedMode;

    [ObservableProperty]
    private string _commandPreview = "";

    [ObservableProperty]
    private string _extraArgumentsText = "";

    public BinaryUpdateSectionViewModel MinerUpdate { get; }

    public MinerViewModel(
        ProfileStore profileStore, IGpuInfoProvider gpuInfoProvider, TierAssigner tierAssigner, HttpClient httpClient)
    {
        _profileStore = profileStore;
        _gpuInfoProvider = gpuInfoProvider;
        _tierAssigner = tierAssigner;
        ExtraArgumentsText = string.Join(" ", Profile.ExtraMinerArguments);
        ValidateAddress();
        _ = RefreshPreviewAsync();

        var updateService = new BinaryUpdateService(new GitHubReleaseChecker(httpClient), new ModelDownloader(httpClient));
        MinerUpdate = new BinaryUpdateSectionViewModel(
            updateService,
            ManagedBinaryKind.Miner,
            getExecutablePath: () => Profile.MinerExecutablePath,
            getInstalledVersion: () => Profile.MinerInstalledVersion,
            setInstalledVersion: v => Profile.MinerInstalledVersion = v,
            persist: () => { _ = _profileStore.SaveAsync(); });
    }

    partial void OnAdvancedModeChanged(bool value)
    {
        if (value) _ = RefreshPreviewAsync();
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите keryx-miner.exe",
            Filter = "keryx-miner.exe|keryx-miner.exe|Исполняемые файлы (*.exe)|*.exe",
        };
        if (dialog.ShowDialog() == true)
        {
            Profile.MinerExecutablePath = dialog.FileName;
            OnPropertyChanged(nameof(Profile));
        }
    }

    [RelayCommand]
    private void BrowseModelsDirectory()
    {
        // .NET 8's native folder picker (Microsoft.Win32.OpenFolderDialog) - avoids pulling in
        // System.Windows.Forms just for one dialog (see KeryxNodeManager.App.csproj comment).
        var dialog = new OpenFolderDialog { Title = "Выберите папку для моделей" };
        if (dialog.ShowDialog() == true)
        {
            var validation = PathValidator.Validate(dialog.FolderName);
            if (!validation.IsValid)
            {
                StatusMessage = validation.Error;
                return;
            }
            Profile.ModelsDirectory = dialog.FolderName;
            OnPropertyChanged(nameof(Profile));
        }
    }

    [RelayCommand]
    private void ValidateAddress()
    {
        AddressValidationMessage = KeryxAddressValidator.LooksValid(Profile.MiningAddress)
            ? null
            : "Адрес не похож на действительный Keryx-адрес (ожидается keryx:... или keryxtest:...).";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidateAddress();
        Profile.ExtraMinerArguments = ExtraArgumentsText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        await _profileStore.SaveAsync();
        StatusMessage = "Настройки майнера сохранены.";
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        try
        {
            // Reads the GPU page's actual per-card choices (persisted to
            // profile.GpuAssignments) through the same GpuAssignmentResolver the real launch path
            // (DashboardViewModel) uses, so this preview can never drift from what would actually
            // run (brief §9 "полный preview команды").
            IReadOnlyList<Core.Models.GpuDevice> devices;
            try
            {
                devices = await _gpuInfoProvider.QueryAsync();
            }
            catch (GpuQueryException)
            {
                devices = Array.Empty<Core.Models.GpuDevice>();
            }

            var (gpuAssignments, anyManualOverride) = GpuAssignmentResolver.Resolve(devices, Profile, _tierAssigner);
            var args = MinerArgumentBuilder.Build(Profile, gpuAssignments, anyManualOverride);
            var exe = string.IsNullOrWhiteSpace(Profile.MinerExecutablePath)
                ? "keryx-miner.exe"
                : Profile.MinerExecutablePath;
            CommandPreview = exe + " " + string.Join(" ", args.Select(QuoteIfNeeded));
        }
        catch (InvalidOperationException ex)
        {
            CommandPreview = $"(нельзя построить команду: {ex.Message})";
        }
    }

    private static string QuoteIfNeeded(string arg) =>
        arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
