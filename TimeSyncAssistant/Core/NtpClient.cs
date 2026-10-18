using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace TimeSync.Core;

public sealed record TimeSample(string Server, DateTimeOffset UtcAtReceipt, long ReceiptTick,
    TimeSpan Offset, TimeSpan RoundTrip)
{
    public DateTimeOffset UtcNow => UtcAtReceipt + Stopwatch.GetElapsedTime(ReceiptTick);
}

public static class NtpClient
{
    private static readonly DateTimeOffset Epoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const double EraSeconds = 4294967296d;

    public static async Task<TimeSample> QueryAsync(string host, CancellationToken token, int port = 123)
    {
        if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("请输入有效的服务器域名或 IP 地址，不要包含协议或端口。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
        // Each address gets its own socket; a failed IPv6 route must not hide a working IPv4 address.
        Exception? last = null;
        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            try
            {
                using var addressTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                addressTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var udp = new UdpClient(address.AddressFamily);
                udp.Connect(new IPEndPoint(address, port));
                byte[] request = new byte[48];
                request[0] = 0x23; // NTP v4, client mode
                var sentUtc = DateTimeOffset.UtcNow;
                WriteTimestamp(request.AsSpan(40), sentUtc);
                long sentTick = Stopwatch.GetTimestamp();
                await udp.SendAsync(request, addressTimeout.Token);
                var reply = await udp.ReceiveAsync(addressTimeout.Token);
                long receiptTick = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(sentTick, receiptTick);
                // Monotonic elapsed time avoids corruption if Windows Time changes the clock mid-request.
                var receiptUtc = sentUtc + elapsed;
                return Parse(reply.Buffer, request, sentUtc, receiptUtc, receiptTick, host);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or InvalidDataException)
            {
                token.ThrowIfCancellationRequested();
                last = ex;
            }
        }
        throw new IOException($"{host} 未返回有效的校时响应（请检查网络及 UDP 123 端口）。", last);
    }

    public static TimeSample Parse(byte[] response, byte[] request, DateTimeOffset t1,
        DateTimeOffset t4, long receiptTick, string server)
    {
        if (response.Length < 48 || request.Length < 48) throw new InvalidDataException("NTP 数据包长度不足。");
        int version = (response[0] >> 3) & 7;
        if ((response[0] & 7) != 4 || version is < 3 or > 4 || (response[0] >> 6) == 3)
            throw new InvalidDataException("服务器未同步或响应类型无效。");
        if (response[1] is 0 or > 15) throw new InvalidDataException("服务器拒绝请求或层级无效。");
        if (!response.AsSpan(24, 8).SequenceEqual(request.AsSpan(40, 8)))
            throw new InvalidDataException("响应与本次请求不匹配。");
        if (BinaryPrimitives.ReadUInt64BigEndian(response.AsSpan(32, 8)) == 0 ||
            BinaryPrimitives.ReadUInt64BigEndian(response.AsSpan(40, 8)) == 0)
            throw new InvalidDataException("服务器时间戳为空。");
        var t2 = ReadTimestamp(response.AsSpan(32), t4);
        var t3 = ReadTimestamp(response.AsSpan(40), t4);
        var delay = (t4 - t1) - (t3 - t2);
        if (t3 < t2 || delay.TotalMilliseconds < -10 || delay > TimeSpan.FromSeconds(3))
            throw new InvalidDataException("服务器响应时序或网络延迟异常。");
        var offset = TimeSpan.FromTicks(((t2 - t1).Ticks + (t3 - t4).Ticks) / 2);
        return new(server, t4 + offset, receiptTick, offset, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
    }

    public static void WriteTimestamp(Span<byte> target, DateTimeOffset value)
    {
        double seconds = (value - Epoch).TotalSeconds;
        double whole = Math.Floor(seconds);
        BinaryPrimitives.WriteUInt32BigEndian(target, unchecked((uint)(long)whole));
        BinaryPrimitives.WriteUInt32BigEndian(target[4..], (uint)((seconds - whole) * EraSeconds));
    }

    public static DateTimeOffset ReadTimestamp(ReadOnlySpan<byte> source, DateTimeOffset near)
    {
        double seconds = BinaryPrimitives.ReadUInt32BigEndian(source);
        seconds += BinaryPrimitives.ReadUInt32BigEndian(source[4..]) / EraSeconds;
        // Resolve the 2036 rollover to the era nearest the local date.
        double era = Math.Round(((near - Epoch).TotalSeconds - seconds) / EraSeconds);
        return Epoch.AddSeconds(seconds + era * EraSeconds);
    }
}
