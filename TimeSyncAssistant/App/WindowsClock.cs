using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TimeSyncAssistant;

internal static class WindowsClock
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemTime(ref SystemTime time);

    public static void SetUtc(DateTimeOffset utc)
    {
        var d = utc.UtcDateTime;
        var time = new SystemTime { Year = (ushort)d.Year, Month = (ushort)d.Month,
            Day = (ushort)d.Day, DayOfWeek = (ushort)d.DayOfWeek, Hour = (ushort)d.Hour,
            Minute = (ushort)d.Minute, Second = (ushort)d.Second, Milliseconds = (ushort)d.Millisecond };
        if (!SetSystemTime(ref time)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置系统时间，请检查管理员权限及系统策略。");
    }

    public static async Task SetZoneAsync(string id)
    {
        // Resolve against the installed zone list, never send free-form shell commands.
        _ = TimeZoneInfo.FindSystemTimeZoneById(id);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tzutil.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("/s");
        start.ArgumentList.Add(id);
        using var process = Process.Start(start) ?? throw new IOException("无法启动系统时区工具。");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException($"设置时区失败：{await error} {await output}");
        TimeZoneInfo.ClearCachedData();
    }
}
