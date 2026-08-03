using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Models;
using KeryxNodeManager.Core.ModelsManagement;
using KeryxNodeManager.Core.Networking;
using KeryxNodeManager.Core.Updates;
using Microsoft.Win32;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Collects the fields NodeArgumentBuilder/NativeWindowsRuntimeBackend need to actually launch
/// keryxd.exe (brief §8). Binds directly to the shared ProfileStore.ActiveProfile POCO - this is
/// a settings-editing screen with a single writer, so plain property binding (no
/// INotifyPropertyChanged needed on MiningProfile itself) is enough; Save persists it via
/// ConfigStore.
/// </summary>
public partial class NodeViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;

    public MiningProfile Profile => _profileStore.ActiveProfile;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _executablePathIsValid;

    public BinaryUpdateSectionViewModel NodeUpdate { get; }
    public PublicNodeListViewModel PublicNodes { get; }
    public DataDirSectionViewModel DataDir { get; }

    public NodeViewModel(ProfileStore profileStore, HttpClient httpClient)
    {
        _profileStore = profileStore;
        ExecutablePathIsValid = File.Exists(Profile.NodeExecutablePath);

        var updateService = new BinaryUpdateService(new GitHubReleaseChecker(httpClient), new ModelDownloader(httpClient));
        NodeUpdate = new BinaryUpdateSectionViewModel(
            updateService,
            ManagedBinaryKind.Node,
            getExecutablePath: () => Profile.NodeExecutablePath,
            getInstalledVersion: () => Profile.NodeInstalledVersion,
            setInstalledVersion: v => Profile.NodeInstalledVersion = v,
            persist: () => { _ = _profileStore.SaveAsync(); });

        PublicNodes = new PublicNodeListViewModel(
            new PublicNodeDirectoryService(httpClient),
            new OwnNodePeerDiscoveryService(),
            Profile,
            persist: () => { _ = _profileStore.SaveAsync(); },
            discoveredCachePath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeryxNodeManager", "discovered-nodes.json"));

        DataDir = new DataDirSectionViewModel(
            new DataDirDownloadService(httpClient),
            getDataDirectory: () => Profile.NodeDataDirectory,
            setDataDirectory: v => Profile.NodeDataDirectory = v,
            persist: () => { _ = _profileStore.SaveAsync(); });
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите keryxd.exe",
            Filter = "keryxd.exe|keryxd.exe|Исполняемые файлы (*.exe)|*.exe",
        };
        if (dialog.ShowDialog() == true)
        {
            Profile.NodeExecutablePath = dialog.FileName;
            OnPropertyChanged(nameof(Profile));
            ExecutablePathIsValid = File.Exists(Profile.NodeExecutablePath);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _profileStore.SaveAsync();
        StatusMessage = "Настройки ноды сохранены.";
    }

    /// <summary>Real TCP connectivity check against the configured gRPC endpoint - not a stub.
    /// Only confirms a listener is accepting connections on that port; it does not speak the
    /// gRPC/protobuf protocol to verify it's actually keryxd (brief §8 "проверить endpoint").</summary>
    [RelayCommand]
    private async Task CheckEndpointAsync()
    {
        var port = Profile.NodePort ?? (Profile.UseTestnet ? 22211 : 22110);
        StatusMessage = $"Проверка {Profile.NodeEndpoint}:{port}...";
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(Profile.NodeEndpoint, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(3)));
            if (completed == connectTask && client.Connected)
            {
                StatusMessage = $"Endpoint {Profile.NodeEndpoint}:{port} принимает соединения.";
            }
            else
            {
                StatusMessage = $"Endpoint {Profile.NodeEndpoint}:{port} не отвечает (таймаут 3с). " +
                                 "Нода не запущена или порт указан неверно.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось подключиться к {Profile.NodeEndpoint}:{port}: {ex.Message}";
        }
    }
}
