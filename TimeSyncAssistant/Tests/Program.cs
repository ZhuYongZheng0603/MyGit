using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using TimeSync.Core;

int passed = 0;
void Assert(bool valid, string name)
{
    if (!valid) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

var t1 = new DateTimeOffset(2026, 10, 18, 1, 0, 0, TimeSpan.Zero);
byte[] request = new byte[48];
NtpClient.WriteTimestamp(request.AsSpan(40), t1);
byte[] Reply(DateTimeOffset received, DateTimeOffset sent)
{
    byte[] r = new byte[48]; r[0] = 0x24; r[1] = 2;
    request.AsSpan(40, 8).CopyTo(r.AsSpan(24));
    NtpClient.WriteTimestamp(r.AsSpan(32), received);
    NtpClient.WriteTimestamp(r.AsSpan(40), sent);
    return r;
}
var valid = Reply(t1.AddSeconds(2.04), t1.AddSeconds(2.06));
var result = NtpClient.Parse(valid, request, t1, t1.AddMilliseconds(100), Stopwatch.GetTimestamp(), "test");
Assert(Math.Abs(result.Offset.TotalSeconds - 2) < 0.001, "four-timestamp offset");
Assert(Math.Abs(result.RoundTrip.TotalMilliseconds - 80) < 0.01, "server processing excluded from round trip");
Assert((result.UtcNow - result.UtcAtReceipt).TotalSeconds < 1, "monotonic sample extrapolation");

void Reject(byte[] packet, string name)
{
    bool rejected = false;
    try { NtpClient.Parse(packet, request, t1, t1.AddMilliseconds(100), 0, "test"); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, name);
}
Reject(new byte[20], "truncated packet rejected");
byte[] Mutate(int index, byte value) { var packet = valid.ToArray(); packet[index] = value; return packet; }
Reject(Mutate(0, 0xE4), "unsynchronized leap indicator rejected");
Reject(Mutate(0, 0x23), "client-mode response rejected");
Reject(Mutate(0, 0x14), "old protocol version rejected");
Reject(Mutate(1, 0), "kiss-of-death rejected");
Reject(Mutate(1, 16), "invalid stratum rejected");
Reject(Mutate(24, 1), "unmatched request rejected");
var zero = valid.ToArray(); Array.Clear(zero, 40, 8); Reject(zero, "zero transmit time rejected");
Reject(Reply(t1.AddSeconds(2.1), t1.AddSeconds(2)), "reversed server timestamps rejected");
Reject(Reply(t1, t1.AddSeconds(1)), "impossible negative delay rejected");

foreach (int year in new[] { 2026, 2036, 2040 })
{
    var timestamp = new DateTimeOffset(year, 10, 18, 1, 0, 0, TimeSpan.Zero).AddMilliseconds(125);
    byte[] buffer = new byte[8]; NtpClient.WriteTimestamp(buffer, timestamp);
    Assert((NtpClient.ReadTimestamp(buffer, timestamp) - timestamp).Duration() < TimeSpan.FromMilliseconds(0.01), $"NTP era roundtrip {year}");
}
var beijing = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
Assert(TimeZoneInfo.ConvertTime(t1, beijing).Hour == 9, "Beijing uses UTC+8");
var ny = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
Assert(ny.GetUtcOffset(t1) == TimeSpan.FromHours(-4), "destination zone observes daylight saving");

// Real UDP exchange against a local controlled server; never writes system time.
using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
var responder = Task.Run(async () =>
{
    var packet = await server.ReceiveAsync();
    var reply = new byte[48]; reply[0] = 0x24; reply[1] = 2;
    packet.Buffer.AsSpan(40, 8).CopyTo(reply.AsSpan(24));
    NtpClient.WriteTimestamp(reply.AsSpan(32), DateTimeOffset.UtcNow.AddSeconds(3));
    NtpClient.WriteTimestamp(reply.AsSpan(40), DateTimeOffset.UtcNow.AddSeconds(3));
    await server.SendAsync(reply, packet.RemoteEndPoint);
});
var networkSample = await NtpClient.QueryAsync("127.0.0.1", CancellationToken.None, port);
await responder;
Assert(Math.Abs(networkSample.Offset.TotalSeconds - 3) < 0.1, "real UDP exchange validates origin and measures offset");
using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
using var cancel = new CancellationTokenSource(100);
bool canceled = false;
try { await NtpClient.QueryAsync("127.0.0.1", cancel.Token, ((IPEndPoint)silent.Client.LocalEndPoint!).Port); }
catch (OperationCanceledException) { canceled = true; }
Assert(canceled, "caller cancellation interrupts unresponsive server");
bool invalidHost = false;
try { await NtpClient.QueryAsync("https://example.com:123", CancellationToken.None); }
catch (ArgumentException) { invalidHost = true; }
Assert(invalidHost, "URL rejected as NTP hostname");
Console.WriteLine($"All {passed} checks passed.");
