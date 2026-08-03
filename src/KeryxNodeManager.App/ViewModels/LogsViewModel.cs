using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeryxNodeManager.Core.Config;
using KeryxNodeManager.Core.Logging;
using KeryxNodeManager.Core.Process;
using Microsoft.Win32;

namespace KeryxNodeManager.App.ViewModels;

/// <summary>
/// Drives the Logs page (brief §12): shows the same masked lines LogSink already buffers from the
/// node/miner's real stdout/stderr (see DashboardViewModel, which wires each launch's OnOutputLine
/// callback to LogSink.Append), and exports a diagnostic ZIP via DiagnosticsExporter. This
/// ViewModel does not itself capture any process output - it only reads what LogSink already
/// captured, so the Logs page and the actual running processes can never disagree about what was
/// said.
/// </summary>
public partial class LogsViewModel : ObservableObject
{
    private readonly LogSink _logSink;
    private readonly ProfileStore _profileStore;

    /// <summary>0.2.7 fix: the Logs page's ItemsControl is not virtualized (plain ItemsControl +
    /// ScrollViewer, each TextBlock has TextWrapping="Wrap"), and LogSink itself buffers up to
    /// 2000 lines per process. During an active model download the node/miner can emit lines fast
    /// enough that the page became nearly unusable ("жестко начинает лагать... еле вышел") -
    /// re-measuring/arranging thousands of wrapped TextBlocks on every single new line. Capping
    /// what's actually displayed to the most recent lines fixes this without touching LogSink's own
    /// on-disk files or its export/diagnostics buffer - only the live on-screen list is capped.</summary>
    public const int MaxDisplayedLines = 50;

    public ObservableCollection<string> NodeLines { get; } = new();
    public ObservableCollection<string> MinerLines { get; } = new();

    [ObservableProperty]
    private string? _statusMessage;

    public LogsViewModel(LogSink logSink, ProfileStore profileStore)
    {
        _logSink = logSink;
        _profileStore = profileStore;

        // A stale log from a previous install/run shouldn't sit around forever - this mirrors
        // what a scheduled/background pass would do, run once per Logs-page construction since
        // there is no background timer wired up yet (see PROJECT_STATUS.md next steps).
        _logSink.PruneOldFiles();

        foreach (var line in _logSink.GetBuffered(ManagedProcessKind.Node).TakeLast(MaxDisplayedLines)) NodeLines.Add(Format(line));
        foreach (var line in _logSink.GetBuffered(ManagedProcessKind.Miner).TakeLast(MaxDisplayedLines)) MinerLines.Add(Format(line));

        _logSink.LineAppended += OnLineAppended;
    }

    private void OnLineAppended(LogLine line)
    {
        // LogSink.Append is called from the runtime backends' OutputDataReceived/ErrorDataReceived
        // handlers, which fire on a ThreadPool thread, not the UI thread - every ObservableCollection
        // mutation must be marshaled back to the dispatcher or WPF throws a cross-thread exception.
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var target = line.Kind == ManagedProcessKind.Node ? NodeLines : MinerLines;
            target.Add(Format(line));
            while (target.Count > MaxDisplayedLines) target.RemoveAt(0);
        });
    }

    private static string Format(LogLine line) =>
        $"[{line.At.ToLocalTime():HH:mm:ss}] {(line.IsError ? "ERR" : "OUT")} {line.Text}";

    [RelayCommand]
    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog
        {
            Title = AppStrings.Get("Str_Logs_ExportDialog_Title"),
            Filter = AppStrings.Get("Str_Logs_ExportDialog_Filter"),
            FileName = $"keryx-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            DiagnosticsExporter.Export(_logSink.LogsDirectory, dialog.FileName, _profileStore.Settings, AppVersionInfo.Current);
            StatusMessage = AppStrings.Format("Str_Logs_ExportSaved", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = AppStrings.Format("Str_Logs_ExportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_logSink.LogsDirectory);
        Process.Start(new ProcessStartInfo(_logSink.LogsDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ClearDisplayedLines()
    {
        // Clears only what's shown on this page - the on-disk log files (and diagnostic export)
        // are untouched, so this is a display convenience, never a data-loss action.
        NodeLines.Clear();
        MinerLines.Clear();
    }
}
