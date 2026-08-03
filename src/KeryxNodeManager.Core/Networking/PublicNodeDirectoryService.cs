using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeryxNodeManager.Core.Networking;

/// <summary>
/// Loads a list of public/community Keryx nodes - either the small bundled default shipped inside
/// this assembly, or a remote JSON list the user (Settings page, brief-adjacent request) points at
/// - and probes each one's reachability. See <see cref="PublicNodeInfo"/>'s doc comment for why
/// this list is hand-curated rather than protocol-discovered.
///
/// The bundled default ships as an EMPTY array, deliberately - this project has an established
/// rule (see ModelDownloader's doc comment re: no hardcoded model-mirror URL) against shipping a
/// guessed/unverified endpoint: no real, confirmed-reachable public Keryx node address was found
/// anywhere in this project's research (docs/KERYX_RESEARCH.md has no seed-node list, and no
/// operator address was captured during any prior session). Populate `Resources/PublicNodes.json`
/// for real once real node operators/addresses are known, or point Settings' remote-list URL at a
/// hosted JSON file with the same schema.
/// </summary>
public sealed class PublicNodeDirectoryService
{
    private const string EmbeddedResourceName = "KeryxNodeManager.Core.Networking.PublicNodes.json";

    private readonly HttpClient _httpClient;

    public PublicNodeDirectoryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IReadOnlyList<PublicNodeInfo> LoadBundled()
    {
        using var stream = typeof(PublicNodeDirectoryService).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            // A missing embedded resource is a packaging/build bug (the .csproj's
            // <EmbeddedResource> entry and this constant's name must agree), not something a user
            // can hit at runtime by any action of their own - fail loudly rather than silently
            // returning an empty list that would be indistinguishable from "no default nodes yet."
            throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' is missing from {typeof(PublicNodeDirectoryService).Assembly.GetName().Name} - packaging bug.");
        }
        return ParseJson(stream);
    }

    /// <summary>Fetches and parses a remote node-list JSON (same schema as the bundled default).
    /// Deliberately does not merge with the bundled list itself - callers decide whether "replace"
    /// or "union" is the right behavior for their UI; merging silently here would hide a remote
    /// list that accidentally shadows/duplicates a bundled entry.</summary>
    public async Task<IReadOnlyList<PublicNodeInfo>> FetchRemoteAsync(Uri url, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return ParseJson(stream);
    }

    private static IReadOnlyList<PublicNodeInfo> ParseJson(Stream stream)
    {
        var dtos = JsonSerializer.Deserialize<List<PublicNodeDto>>(stream) ?? new List<PublicNodeDto>();
        return dtos
            .Where(d => !string.IsNullOrWhiteSpace(d.Endpoint))
            .Select(d => new PublicNodeInfo(
                d.Name ?? d.Endpoint!, d.Endpoint!, d.Port, d.Region, d.Notes, d.SelfReportedUptimePercent))
            .ToList();
    }

    /// <summary>
    /// The one honest, locally-measurable signal this app can produce for a remote node: "did a
    /// plain TCP connect to Endpoint:Port succeed just now, and how long did it take." This is
    /// NOT a protocol-aware health check (it doesn't speak gRPC to confirm the node actually
    /// answers Keryx RPC calls, just that something is listening on that port) - deliberately kept
    /// this simple so it works identically whether the port is keryxd's gRPC port or anything
    /// else, and never blocks longer than <paramref name="timeout"/> even against a host that
    /// silently drops packets instead of actively refusing the connection.
    /// </summary>
    public static async Task<PublicNodeHealthResult> CheckHealthAsync(
        PublicNodeInfo node, TimeSpan timeout, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await client.ConnectAsync(node.Endpoint, node.Port, timeoutCts.Token);
            stopwatch.Stop();
            return new PublicNodeHealthResult(true, stopwatch.Elapsed.TotalMilliseconds, DateTimeOffset.UtcNow, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PublicNodeHealthResult(false, null, DateTimeOffset.UtcNow, "timeout");
        }
        catch (SocketException ex)
        {
            return new PublicNodeHealthResult(false, null, DateTimeOffset.UtcNow, ex.Message);
        }
    }

    private sealed class PublicNodeDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("endpoint")]
        public string? Endpoint { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("selfReportedUptimePercent")]
        public double? SelfReportedUptimePercent { get; set; }
    }
}
