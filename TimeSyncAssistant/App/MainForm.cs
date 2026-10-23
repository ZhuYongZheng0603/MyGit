using System.Diagnostics;
using System.Text.Json;
using TimeSync.Core;

namespace TimeSyncAssistant;

internal sealed class MainForm : Form
{
    private readonly ComboBox zones = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox servers = new() { Dock = DockStyle.Fill };
    private readonly Label clock = new() { AutoSize = true, Font = new Font("Segoe UI", 34, FontStyle.Bold) };
    private readonly Label date = new() { AutoSize = true };
    private readonly Label system = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label status = new() { AutoSize = true, Text = "就绪 · 尚未网络校时", ForeColor = Color.FromArgb(38, 93, 184) };
    private readonly TextBox logs = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button sync = Button("立即同步", true);
    private readonly Button check = Button("检测网络时间");
    private readonly CheckBox periodic = new() { AutoSize = true, Text = "每 60 分钟自动同步（仅在本程序运行时）" };
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer autoTimer = new() { Interval = 60 * 60 * 1000 };
    private readonly CancellationTokenSource lifetime = new();
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSyncAssistant", "settings.json");
    private bool busy;
    private bool loaded;
    private static readonly string[] DefaultServers = ["ntp.aliyun.com", "ntp.tencent.com", "time.windows.com", "time.cloudflare.com"];
    private sealed record ZoneItem(TimeZoneInfo Zone)
    {
        public override string ToString() => Zone.Id == "China Standard Time" ? "北京时间 · 北京 / 上海 / 重庆（UTC+08:00）" : Zone.DisplayName;
    }
    private sealed record Settings(string ZoneId = "China Standard Time", string Server = "ntp.aliyun.com");

    public MainForm()
    {
        Text = "时间同步助手";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 10);
        // Establish the 96-DPI design baseline before creating any child controls.
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 760);
        MinimumSize = new Size(440, 420);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 251);
        SuspendLayout();

        var viewport = new Panel { Name = "viewport", Dock = DockStyle.Fill, AutoScroll = true };
        var root = new ContentColumn { Name = "content", Padding = new Padding(20) };
        viewport.Controls.Add(root);
        Controls.Add(viewport);

        root.Add(new Label { Name = "heading", AutoSize = true, Dock = DockStyle.Top,
            Text = "时间同步助手", Font = new Font(Font.FontFamily, 22, FontStyle.Bold) });
        root.Add(Note("让电脑时间，与世界保持一致。"));

        var card = new ContentColumn { Name = "clockCard", BackColor = Color.White,
            Padding = new Padding(16), Margin = new Padding(0, 12, 0, 16) };
        card.Add(Note("所选时区时间 · 根据本机时钟预览"));
        clock.Name = "clock";
        card.Add(clock);
        card.Add(date);
        card.Add(system);
        root.Add(card);

        // Stack field labels above their inputs: neither column can steal label space.
        root.Add(FieldLabel("目标时区"));
        zones.Margin = new Padding(0, 4, 0, 12);
        root.Add(zones);
        root.Add(FieldLabel("时间服务器"));
        servers.Margin = new Padding(0, 4, 0, 6);
        root.Add(servers);
        root.Add(Note("可输入 NTP 域名或 IP；连接失败会尝试备用服务器。"));
        periodic.Text = "每小时自动同步";
        periodic.Margin = new Padding(0, 12, 0, 4);
        root.Add(periodic);
        root.Add(Note("仅在本程序运行时生效，关闭后停止。"));

        var actions = new FlowLayoutPanel { Name = "actions", Dock = DockStyle.Top,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Margin = new Padding(0, 16, 0, 6) };
        actions.Controls.Add(sync);
        actions.Controls.Add(check);
        root.Add(actions);
        status.Name = "status";
        status.Margin = new Padding(0, 4, 0, 8);
        root.Add(status);
        root.Add(Note("立即同步将修改 Windows 系统时区和系统时间。切换时区会自动采用该地区的夏令时规则。"));
        logs.Name = "logs";
        logs.Dock = DockStyle.Top;
        logs.Height = 160;
        logs.Margin = new Padding(0, 12, 0, 8);
        root.Add(logs);
        root.Add(Note("Windows 11 · 本地应用 · v1.0.1"));
        ResumeLayout(true);

        var allZones = TimeZoneInfo.GetSystemTimeZones();
        foreach (var zone in allZones.OrderBy(z => z.Id == "China Standard Time" ? 0 : 1).ThenBy(z => z.BaseUtcOffset))
            zones.Items.Add(new ZoneItem(zone));
        servers.Items.AddRange(DefaultServers);
        LoadSettings();
        loaded = true;
        zones.SelectedIndexChanged += (_, _) => { UpdateClock(); SaveSettings(); };
        servers.Leave += (_, _) => SaveSettings();
        sync.Click += async (_, _) => await RunAsync(true);
        check.Click += async (_, _) => await RunAsync(false);
        periodic.CheckedChanged += (_, _) =>
        {
            autoTimer.Enabled = periodic.Checked;
            Log(periodic.Checked ? "已启用每 60 分钟校时；首次校时请点击“立即同步”。" : "已关闭自动同步。");
        };
        displayTimer.Tick += (_, _) => UpdateClock();
        autoTimer.Tick += async (_, _) => await RunAsync(true, true);
        displayTimer.Start();
        UpdateClock();
        Log("默认目标为北京时间；启动和选择时区不会修改系统设置。");
    }

    private static Label Note(string text) => new()
    {
        AutoSize = true, Dock = DockStyle.Top, Text = text, ForeColor = Color.DimGray,
        Margin = new Padding(0, 2, 0, 4)
    };

    private static Label FieldLabel(string text) => new()
    {
        AutoSize = true, Dock = DockStyle.Top, Text = text, Margin = Padding.Empty
    };

    // Every row follows its content height. Only the viewport scrolls when space is short.
    private sealed class ContentColumn : TableLayoutPanel
    {
        public ContentColumn()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Margin = Padding.Empty;
        }

        public void Add(Control control)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(control, 0, RowCount++);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            // Label.MaximumSize makes long Chinese text wrap before its row is measured.
            foreach (Control child in Controls)
            {
                if (child is not Label && child is not CheckBox) continue;
                var maximum = new Size(Math.Max(1, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal), 0);
                if (child.MaximumSize != maximum) child.MaximumSize = maximum;
            }
            base.OnLayout(e);
        }
    }

    private static Button Button(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(16, 8, 16, 8), Margin = new Padding(0, 0, 12, 8),
        FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(38, 93, 184) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(40, 50, 65), Cursor = Cursors.Hand
    };

    private TimeZoneInfo SelectedZone => ((ZoneItem)zones.SelectedItem!).Zone;

    private void LoadSettings()
    {
        Settings settings = new();
        try { if (File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? settings; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Log("读取设置失败，已恢复默认设置。"); }
        zones.SelectedItem = zones.Items.Cast<ZoneItem>().FirstOrDefault(z => z.Zone.Id == settings.ZoneId)
            ?? zones.Items.Cast<ZoneItem>().First(z => z.Zone.Id == "China Standard Time");
        servers.Text = settings.Server;
    }

    private void SaveSettings()
    {
        if (!loaded) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(new Settings(SelectedZone.Id, servers.Text.Trim())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("设置未保存：" + ex.Message); }
    }

    private void UpdateClock()
    {
        if (zones.SelectedItem is null) return;
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SelectedZone);
        clock.Text = now.ToString("HH:mm:ss");
        date.Text = now.ToString("yyyy年MM月dd日  dddd") + "  UTC" + now.ToString("zzz");
        system.Text = $"当前系统：{DateTime.Now:HH:mm:ss} · {TimeZoneInfo.Local.DisplayName}";
    }

    private async Task<TimeSample> GetSampleAsync()
    {
        var preferred = servers.Text.Trim();
        if (Uri.CheckHostName(preferred) == UriHostNameType.Unknown || preferred.Length == 0)
            throw new ArgumentException("请输入有效的 NTP 服务器域名或 IP 地址，不含协议和端口。");
        foreach (var host in new[] { preferred }.Concat(DefaultServers).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            lifetime.Token.ThrowIfCancellationRequested();
            Log($"正在连接 {host}…");
            try { return await NtpClient.QueryAsync(host, lifetime.Token); }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                Log($"{host}：连接失败，尝试下一服务器。{ex.Message}");
            }
        }
        throw new IOException("所有时间服务器均不可用。请检查网络、防火墙的 UDP 123 端口，或填写可访问的 NTP 服务器。");
    }

    private async Task RunAsync(bool apply, bool automatic = false)
    {
        if (busy) return;
        busy = true;
        sync.Enabled = check.Enabled = zones.Enabled = servers.Enabled = false;
        status.Text = "正在获取网络时间…";
        try
        {
            SaveSettings();
            var zone = SelectedZone;
            var sample = await GetSampleAsync();
            var networkLocal = TimeZoneInfo.ConvertTime(sample.UtcNow, zone);
            Log($"网络时间：{networkLocal:yyyy-MM-dd HH:mm:ss.fff zzz}；本机偏差校正量 {sample.Offset.TotalMilliseconds:+0.0;-0.0;0} ms，往返 {sample.RoundTrip.TotalMilliseconds:0.0} ms。");
            if (!apply)
            {
                status.Text = $"检测完成 · 校正量 {sample.Offset.TotalSeconds:+0.000;-0.000;0} 秒（未修改系统）";
                return;
            }
            if (sample.Offset.Duration() > TimeSpan.FromMinutes(5))
            {
                if (automatic)
                {
                    Log("偏差超过 5 分钟，已跳过自动校时，请手动检测后同步。");
                    status.Text = "自动同步已跳过 · 时间偏差较大";
                    return;
                }
                // The busy flag blocks timer reentry while the confirmation is displayed.
                if (MessageBox.Show(this, $"系统时间需要校正 {sample.Offset.TotalMinutes:+0.0;-0.0;0} 分钟。\n将同步到 {networkLocal:yyyy-MM-dd HH:mm:ss zzz}，是否继续？", "确认较大时间调整", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    status.Text = "已取消 · 系统未修改";
                    return;
                }
                // Requery after the user confirms: don't apply an old observation.
                var confirmedOffset = sample.Offset;
                sample = await GetSampleAsync();
                if ((sample.Offset - confirmedOffset).Duration() > TimeSpan.FromSeconds(30))
                    throw new IOException("再次检测的时间偏差变化过大，请检查时间服务器后重新同步。");
            }
            string oldZone = TimeZoneInfo.Local.Id;
            bool zoneChanged = oldZone != zone.Id;
            if (zoneChanged) await WindowsClock.SetZoneAsync(zone.Id);
            try { WindowsClock.SetUtc(sample.UtcNow); }
            catch
            {
                if (zoneChanged)
                {
                    try { await WindowsClock.SetZoneAsync(oldZone); Log("校时失败，已恢复原系统时区。"); }
                    catch (Exception rollback) { Log("恢复时区失败，请到 Windows 设置手动检查：" + rollback.Message); }
                }
                throw;
            }
            TimeZoneInfo.ClearCachedData();
            UpdateClock();
            status.Text = $"同步成功 · {DateTime.Now:HH:mm:ss} · {sample.Server}";
            Log($"同步成功。系统时区：{zone.DisplayName}；校正量 {sample.Offset.TotalMilliseconds:+0.0;-0.0;0} ms。");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                status.Text = "操作失败 · 请查看下方日志";
                Log(ex.Message);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed) sync.Enabled = check.Enabled = zones.Enabled = servers.Enabled = true;
        }
    }

    private void Log(string message)
    {
        if (IsDisposed) return;
        if (logs.TextLength > 50000) logs.Clear();
        logs.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy)
        {
            e.Cancel = true;
            status.Text = "操作进行中 · 请稍候再关闭";
            return;
        }
        SaveSettings();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel(); lifetime.Dispose(); displayTimer.Dispose(); autoTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
