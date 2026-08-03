using System.IO;
using System.IO.Pipes;

namespace KeryxNodeManager.App.SingleInstance;

/// <summary>
/// Brief §10/§27: relaunching the app while an instance is already running should bring the
/// existing window to front, not just refuse to start with a message box (the previous behaviour,
/// tracked as an open TODO across several sessions - see PROJECT_STATUS.md). This is a minimal
/// one-directional signal, not a general RPC channel: the only message ever sent is the literal
/// string "SHOW", and the only thing the receiving instance does is call back into
/// TrayIconService.ShowMainWindow (reusing the exact same restore logic the tray icon's own
/// "Открыть Keryx Node Manager" menu item already uses).
///
/// Named pipes (not sockets/HTTP) because this only ever needs to work between two processes on
/// the same machine, in the same user session - a local named pipe needs no port, no firewall
/// exception, and (with the default ACL) is not reachable from other user sessions.
/// </summary>
public sealed class SingleInstanceIpc : IDisposable
{
    private const string PipeName = "KeryxNodeManager.SingleInstance.Pipe";

    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;
    private bool _disposed;

    /// <summary>Starts a background accept-loop that invokes <paramref name="onShowRequested"/>
    /// every time a second launch attempt successfully signals this instance. The loop re-creates
    /// the pipe server after each connection - NamedPipeServerStream serves exactly one client
    /// connection per instance, so a fresh one is needed to accept the next.</summary>
    public void StartServer(Action onShowRequested)
    {
        _serverTask = Task.Run(() => ServerLoopAsync(onShowRequested, _cts.Token));
    }

    private static async Task ServerLoopAsync(Action onShowRequested, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                string? message = await reader.ReadLineAsync(ct);
                if (message == "SHOW")
                {
                    onShowRequested();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // A client that connected then dropped mid-read (or any other transient pipe
                // fault) must not kill the accept-loop for the whole rest of the app's lifetime -
                // just go back to waiting for the next connection attempt.
            }
        }
    }

    /// <summary>Called by a second launch attempt (the one that lost the single-instance mutex
    /// race) to ask the already-running instance to show itself. Returns false if no instance is
    /// actually listening within the timeout - e.g. a narrow startup race, or (more likely in
    /// practice) something has gone wrong and the mutex is held by a process that isn't really the
    /// app - so the caller can fall back to its own honest "already running" message instead of
    /// silently doing nothing.</summary>
    public static async Task<bool> TrySendShowRequestAsync(TimeSpan timeout)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(cts.Token);

            await using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("SHOW");
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Idempotent by design: both App.ShutdownWithCleanup (tray "Exit") and App.OnExit
    /// (WPF's own normal shutdown, which always runs after ShutdownWithCleanup calls
    /// Application.Shutdown()) call Dispose() on this same instance - a real crash confirmed via
    /// Windows Event Log (ObjectDisposedException from _cts.Cancel() on an already-disposed
    /// CancellationTokenSource, thrown from OnExit, terminating the whole process on every "Exit"
    /// from the tray). Guarding against the second call is simpler and safer than trying to remove
    /// one of the two call sites - OnExit must keep its own call for the case where the window is
    /// closed some OTHER way (e.g. the X button) that never goes through ShutdownWithCleanup.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
