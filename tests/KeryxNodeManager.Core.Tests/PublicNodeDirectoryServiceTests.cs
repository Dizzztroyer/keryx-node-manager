using System.Net;
using System.Net.Sockets;
using System.Text;
using KeryxNodeManager.Core.Networking;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers PublicNodeDirectoryService: that the bundled embedded resource actually loads (a
/// packaging regression here would silently return nothing at runtime, not fail loudly, so this
/// test is what catches it), that a remote JSON list parses correctly, and that the health check
/// honestly distinguishes a real open TCP port from a closed/unreachable one - using a real
/// loopback TcpListener rather than mocking sockets, since a fake would trivially "pass" without
/// proving the actual connect-with-timeout logic works.
/// </summary>
public class PublicNodeDirectoryServiceTests
{
    [Fact]
    public void LoadBundled_ReturnsEmptyList_NotNullAndDoesNotThrow()
    {
        // The bundled default ships empty by design (see PublicNodeDirectoryService's doc
        // comment) - this test's job is to prove the embedded-resource wiring itself works
        // (LogicalName in the .csproj matches the constant in the service), not to assert
        // anything about real node addresses.
        var service = new PublicNodeDirectoryService(new HttpClient());

        var nodes = service.LoadBundled();

        Assert.NotNull(nodes);
        Assert.Empty(nodes);
    }

    [Fact]
    public async Task FetchRemoteAsync_ParsesJsonList_IncludingSelfReportedUptime()
    {
        const string json = """
            [
              { "name": "Community Node 1", "endpoint": "node1.example.com", "port": 22110, "region": "EU", "selfReportedUptimePercent": 99.5 },
              { "name": "Community Node 2", "endpoint": "node2.example.com", "port": 22110 }
            ]
            """;
        var service = new PublicNodeDirectoryService(new HttpClient(new FakeJsonHandler(json)));

        var nodes = await service.FetchRemoteAsync(new Uri("http://fake.local/nodes.json"), CancellationToken.None);

        Assert.Equal(2, nodes.Count);
        Assert.Equal("Community Node 1", nodes[0].Name);
        Assert.Equal(22110, nodes[0].Port);
        Assert.Equal("EU", nodes[0].Region);
        Assert.Equal(99.5, nodes[0].SelfReportedUptimePercent);
        Assert.Null(nodes[1].Region); // optional field genuinely absent, not defaulted to something misleading
        Assert.Null(nodes[1].SelfReportedUptimePercent);
    }

    [Fact]
    public async Task FetchRemoteAsync_SkipsEntriesWithNoEndpoint()
    {
        const string json = """[ { "name": "Broken entry", "port": 1 }, { "name": "Good", "endpoint": "host", "port": 2 } ]""";
        var service = new PublicNodeDirectoryService(new HttpClient(new FakeJsonHandler(json)));

        var nodes = await service.FetchRemoteAsync(new Uri("http://fake.local/nodes.json"), CancellationToken.None);

        Assert.Single(nodes);
        Assert.Equal("Good", nodes[0].Name);
    }

    [Fact]
    public async Task CheckHealthAsync_RealOpenLoopbackPort_ReportsReachableWithLatency()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var node = new PublicNodeInfo("loopback", "127.0.0.1", port);
        var result = await PublicNodeDirectoryService.CheckHealthAsync(node, TimeSpan.FromSeconds(2));

        Assert.True(result.Reachable);
        Assert.NotNull(result.LatencyMs);
        Assert.True(result.LatencyMs >= 0);

        (await acceptTask).Dispose();
        listener.Stop();
    }

    [Fact]
    public async Task CheckHealthAsync_NothingListeningOnPort_ReportsUnreachable()
    {
        // Bind and immediately close a listener to get a port that is very likely free but whose
        // OS-level "connection refused" behavior is realistic (rather than guessing a random port
        // number that might coincidentally be in use on the test machine).
        int freePort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var node = new PublicNodeInfo("nothing-here", "127.0.0.1", freePort);
        var result = await PublicNodeDirectoryService.CheckHealthAsync(node, TimeSpan.FromSeconds(2));

        Assert.False(result.Reachable);
        Assert.Null(result.LatencyMs);
    }

    private sealed class FakeJsonHandler : HttpMessageHandler
    {
        private readonly string _json;
        public FakeJsonHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
    }
}
