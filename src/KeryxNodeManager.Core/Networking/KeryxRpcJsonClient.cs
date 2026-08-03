using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeryxNodeManager.Core.Networking;

/// <summary>
/// Minimal client for keryxd's own wRPC JSON interface (confirmed real against the actual
/// keryx-node source, not guessed): the RPC method surface is defined by the `RpcApiOps` enum in
/// rpc/core/src/api/ops.rs, which carries `#[serde(rename_all = "camelCase")]`, and the JSON wire
/// encoding is `WrpcEncoding::SerdeJson` (rpc/wrpc/client/src/client.rs) - a WebSocket connection
/// exchanging one JSON object per text frame: request `{"id":N,"method":"<camelCase op>",
/// "params":{...}}`, response `{"id":N,"result":{...}}` or `{"id":N,"error":"..."}`.
///
/// keryxd does NOT expose this listener by default - it must be launched with
/// `--rpclisten-json=&lt;addr&gt;` (see NodeArgumentBuilder, which this app now always adds, bound
/// to 127.0.0.1 only). Default port is 24110 (mainnet) / 24210 (testnet) per `keryxd --help`.
///
/// This is intentionally a small hand-rolled client rather than a generated one: keryxd has no
/// OpenAPI/JSON-Schema description of its RPC surface, so there is nothing to code-gen against,
/// and pulling in the full Rust wRPC client via FFI would be far more complexity than the handful
/// of read-only calls this app needs (getServerInfo, getBlockDagInfo, getConnectedPeerInfo,
/// getPeerAddresses).
/// </summary>
public sealed class KeryxRpcJsonClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private long _nextId = 1;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        var uri = new Uri($"ws://{host}:{port}");
        await _socket.ConnectAsync(uri, ct);
    }

    public async Task<ServerInfo> GetServerInfoAsync(CancellationToken ct) =>
        await CallAsync<ServerInfo>("getServerInfo", ct);

    public async Task<BlockDagInfo> GetBlockDagInfoAsync(CancellationToken ct) =>
        await CallAsync<BlockDagInfo>("getBlockDagInfo", ct);

    public async Task<ConnectedPeerInfoResponse> GetConnectedPeerInfoAsync(CancellationToken ct) =>
        await CallAsync<ConnectedPeerInfoResponse>("getConnectedPeerInfo", ct);

    public async Task<PeerAddressesResponse> GetPeerAddressesAsync(CancellationToken ct) =>
        await CallAsync<PeerAddressesResponse>("getPeerAddresses", ct);

    private async Task<T> CallAsync<T>(string method, CancellationToken ct)
    {
        if (_socket.State != WebSocketState.Open)
        {
            throw new KeryxRpcException($"RPC socket is not connected (state: {_socket.State}).");
        }

        var id = Interlocked.Increment(ref _nextId);
        // Deliberately NOT JsonSerializer.Serialize(new RpcRequest(...)) - that emits the C#
        // record's PascalCase property names ("Id"/"Method"/"Params"), but keryxd's wRPC JSON
        // envelope uses lowercase keys (confirmed by writing a real loopback-server test for this
        // client: a naive PascalCase request left the test server unable to find an "id"/"method"
        // property at all). Anonymous object with explicit lowercase names avoids relying on any
        // naming-policy configuration being remembered/applied consistently everywhere this class
        // might someday serialize a request.
        var requestJson = JsonSerializer.Serialize(new { id, method, @params = new { } });
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
        await _socket.SendAsync(requestBytes, WebSocketMessageType.Text, true, ct);

        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new KeryxRpcException("RPC socket closed by keryxd while awaiting a response.");
            }
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        using var doc = JsonDocument.Parse(ms.ToArray());
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var errorEl))
        {
            throw new KeryxRpcException($"keryxd RPC error for '{method}': {errorEl}");
        }
        if (!root.TryGetProperty("result", out var resultEl))
        {
            throw new KeryxRpcException($"Unexpected RPC response shape for '{method}' - no 'result' or 'error' field.");
        }

        var value = resultEl.Deserialize<T>(JsonOpts);
        if (value is null)
        {
            throw new KeryxRpcException($"Failed to deserialize '{method}' result into {typeof(T).Name}.");
        }
        return value;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch
            {
                // Best-effort close - the process may already be gone.
            }
        }
        _socket.Dispose();
    }
}

public sealed class KeryxRpcException(string message) : Exception(message);

/// <summary>Subset of GetServerInfoResponse's real fields (rpc/core/src/model) - only what this
/// app actually uses. `IsSynced` is the field the sync-aware node-switching feature depends on.</summary>
public sealed class ServerInfo
{
    [JsonPropertyName("isSynced")]
    public bool IsSynced { get; set; }

    [JsonPropertyName("serverVersion")]
    public string? ServerVersion { get; set; }

    [JsonPropertyName("networkId")]
    public string? NetworkId { get; set; }

    [JsonPropertyName("hasUtxoIndex")]
    public bool HasUtxoIndex { get; set; }
}

public sealed class BlockDagInfo
{
    [JsonPropertyName("blockCount")]
    public string? BlockCount { get; set; }

    [JsonPropertyName("headerCount")]
    public string? HeaderCount { get; set; }

    [JsonPropertyName("virtualDaaScore")]
    public string? VirtualDaaScore { get; set; }
}

public sealed class ConnectedPeerInfoResponse
{
    [JsonPropertyName("peerInfo")]
    public List<RpcPeerInfoDto> PeerInfo { get; set; } = new();
}

/// <summary>Mirrors rpc/core/src/model/peer.rs's RpcPeerInfo (confirmed real field names against
/// the actual struct, adapted to expected camelCase JSON keys per the RpcApiOps serde convention).</summary>
public sealed class RpcPeerInfoDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("isOutbound")]
    public bool IsOutbound { get; set; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }
}

public sealed class PeerAddressesResponse
{
    [JsonPropertyName("knownAddresses")]
    public List<RpcPeerAddressDto> KnownAddresses { get; set; } = new();
}

public sealed class RpcPeerAddressDto
{
    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }
}
