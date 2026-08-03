using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeryxNodeManager.Core.Networking;
using Xunit;

namespace KeryxNodeManager.Core.Tests;

/// <summary>
/// Covers KeryxRpcJsonClient against a REAL loopback WebSocket server - not a mocked socket, since
/// the whole point of this class is correctly framing JSON request/response messages over an
/// actual WebSocket connection. Uses a hand-rolled minimal RFC 6455 server over a plain
/// TcpListener (see RawWebSocketTestServer below) rather than System.Net.HttpListener: an earlier
/// version of this test used HttpListener.AcceptWebSocketAsync and it hung indefinitely on this
/// dev machine (HttpListener/http.sys binding to a loopback+port combination this account isn't
/// pre-authorized for, most likely - it never even reached the first GetContextAsync). Follows
/// this project's established real-loopback-socket testing pattern (see
/// PublicNodeDirectoryServiceTests's TcpListener tests), just one layer lower.
/// </summary>
public class KeryxRpcJsonClientTests
{
    [Fact]
    public async Task GetServerInfoAsync_RealLoopbackServer_ParsesIsSyncedFromResult()
    {
        var port = GetFreeLoopbackPort();
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var serverTask = RawWebSocketTestServer.AcceptOneMessageAndRespondAsync(listener, _ =>
            """{"isSynced":true,"serverVersion":"1.4.4-OPoI","networkId":"keryx-mainnet","hasUtxoIndex":true}""");

        await using var client = new KeryxRpcJsonClient();
        await client.ConnectAsync("127.0.0.1", port, CancellationToken.None);
        var info = await client.GetServerInfoAsync(CancellationToken.None);

        Assert.True(info.IsSynced);
        Assert.Equal("1.4.4-OPoI", info.ServerVersion);
        Assert.True(info.HasUtxoIndex);

        await serverTask;
        listener.Stop();
    }

    [Fact]
    public async Task GetConnectedPeerInfoAsync_RealLoopbackServer_ParsesPeerList()
    {
        var port = GetFreeLoopbackPort();
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var serverTask = RawWebSocketTestServer.AcceptOneMessageAndRespondAsync(listener, _ =>
            """{"peerInfo":[{"id":"abc","address":"203.0.113.5:22110","isOutbound":true,"userAgent":"/keryxd:1.4.4/"}]}""");

        await using var client = new KeryxRpcJsonClient();
        await client.ConnectAsync("127.0.0.1", port, CancellationToken.None);
        var result = await client.GetConnectedPeerInfoAsync(CancellationToken.None);

        var peer = Assert.Single(result.PeerInfo);
        Assert.Equal("203.0.113.5:22110", peer.Address);
        Assert.True(peer.IsOutbound);

        await serverTask;
        listener.Stop();
    }

    [Fact]
    public async Task Call_ServerReturnsErrorField_ThrowsKeryxRpcException()
    {
        var port = GetFreeLoopbackPort();
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var serverTask = RawWebSocketTestServer.AcceptOneMessageAndRespondErrorAsync(listener, "not synced yet");

        await using var client = new KeryxRpcJsonClient();
        await client.ConnectAsync("127.0.0.1", port, CancellationToken.None);

        await Assert.ThrowsAsync<KeryxRpcException>(() => client.GetServerInfoAsync(CancellationToken.None));

        await serverTask;
        listener.Stop();
    }

    private static int GetFreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>Minimal RFC 6455 server: accepts one TCP connection, performs the HTTP Upgrade
    /// handshake, decodes exactly one masked client text frame (client->server frames are always
    /// masked per spec), extracts the "id" field so the response can echo it correctly, and sends
    /// back one unmasked server text frame. Good enough for these single-request/response tests -
    /// not a general-purpose WebSocket server.</summary>
    private static class RawWebSocketTestServer
    {
        public static async Task AcceptOneMessageAndRespondAsync(TcpListener listener, Func<string, string> buildResultJson)
        {
            using var tcpClient = await listener.AcceptTcpClientAsync();
            await using var stream = tcpClient.GetStream();

            await PerformHandshakeAsync(stream);
            var (id, method) = await ReadOneTextFrameAsync(stream);
            var responseJson = $$"""{"id":{{id}},"result":{{buildResultJson(method)}}}""";
            await SendTextFrameAsync(stream, responseJson);
        }

        public static async Task AcceptOneMessageAndRespondErrorAsync(TcpListener listener, string errorText)
        {
            using var tcpClient = await listener.AcceptTcpClientAsync();
            await using var stream = tcpClient.GetStream();

            await PerformHandshakeAsync(stream);
            var (id, _) = await ReadOneTextFrameAsync(stream);
            var responseJson = $$"""{"id":{{id}},"error":"{{errorText}}"}""";
            await SendTextFrameAsync(stream, responseJson);
        }

        private static async Task PerformHandshakeAsync(NetworkStream stream)
        {
            var requestText = await ReadHttpHeadersAsync(stream);
            var keyLine = requestText.Split("\r\n")
                .FirstOrDefault(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Client did not send Sec-WebSocket-Key.");
            var key = keyLine.Split(':', 2)[1].Trim();

            const string magicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + magicGuid)));

            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                           "Upgrade: websocket\r\n" +
                           "Connection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes);
        }

        private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream)
        {
            var sb = new StringBuilder();
            var buffer = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                sb.Append((char)buffer[0]);
                if (sb.Length >= 4 && sb.ToString(sb.Length - 4, 4) == "\r\n\r\n") break;
            }
            return sb.ToString();
        }

        private static async Task<(long Id, string Method)> ReadOneTextFrameAsync(NetworkStream stream)
        {
            var header = new byte[2];
            await ReadExactAsync(stream, header, 2);
            var payloadLenIndicator = header[1] & 0x7F;
            var masked = (header[1] & 0x80) != 0;

            int payloadLen;
            if (payloadLenIndicator == 126)
            {
                var ext = new byte[2];
                await ReadExactAsync(stream, ext, 2);
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLenIndicator == 127)
            {
                var ext = new byte[8];
                await ReadExactAsync(stream, ext, 8);
                payloadLen = (int)((uint)ext[4] << 24 | (uint)ext[5] << 16 | (uint)ext[6] << 8 | ext[7]);
            }
            else
            {
                payloadLen = payloadLenIndicator;
            }

            byte[] mask = new byte[4];
            if (masked)
            {
                await ReadExactAsync(stream, mask, 4);
            }

            var payload = new byte[payloadLen];
            await ReadExactAsync(stream, payload, payloadLen);
            if (masked)
            {
                for (var i = 0; i < payloadLen; i++)
                {
                    payload[i] ^= mask[i % 4];
                }
            }

            var json = Encoding.UTF8.GetString(payload);
            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("id").GetInt64();
            var method = doc.RootElement.GetProperty("method").GetString() ?? "";
            return (id, method);
        }

        private static async Task SendTextFrameAsync(NetworkStream stream, string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            using var ms = new MemoryStream();
            ms.WriteByte(0x81); // FIN + text opcode

            if (payload.Length < 126)
            {
                ms.WriteByte((byte)payload.Length); // no mask bit - server frames are unmasked
            }
            else
            {
                ms.WriteByte(126);
                ms.WriteByte((byte)(payload.Length >> 8));
                ms.WriteByte((byte)(payload.Length & 0xFF));
            }
            ms.Write(payload);

            var frame = ms.ToArray();
            await stream.WriteAsync(frame);
        }

        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
                if (read == 0) throw new IOException("Connection closed before expected bytes were received.");
                offset += read;
            }
        }
    }
}
