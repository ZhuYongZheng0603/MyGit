using TimeSyncAssistant;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // No app manifest and no button clicks: these checks never modify system time.
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory("dist/layout-checks");
        foreach (float textScale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            using var form = new MainForm();
            form.AutoScaleMode = AutoScaleMode.None;
            form.Font = new Font(form.Font.FontFamily, 10 * textScale);
            form.Show();
            foreach (var size in new[] { new Size(440, 420), new Size(720, 640), new Size(1000, 850) })
            {
                form.ClientSize = size;
                var status = form.Controls.Find("status", true).Single();
                status.Text = "检测完成 · 校正量 +123.456 秒（未修改系统） · time.cloudflare.com";
                for (int i = 0; i < 4; i++) { form.PerformLayout(); Application.DoEvents(); }
                var content = form.Controls.Find("content", true).Single();
                Check(content);
                var viewport = (Panel)form.Controls.Find("viewport", true).Single();
                if (content.Height > viewport.ClientSize.Height && !viewport.VerticalScroll.Visible)
                    throw new Exception("Short window must provide a vertical scrollbar.");
                if (viewport.HorizontalScroll.Visible) throw new Exception("Layout overflowed horizontally.");
                using var bitmap = new Bitmap(content.Width, content.Height);
                content.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save($"dist/layout-checks/text-{textScale:0.00}-{size.Width}x{size.Height}.png");
                Console.WriteLine($"PASS: text scale {textScale:0.00}, window {size.Width}x{size.Height}");
            }
            form.Close();
        }
    }

    private static void Check(Control parent)
    {
        var children = parent.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
        foreach (var child in children)
        {
            if (child.Left < parent.Padding.Left || child.Right > parent.ClientSize.Width - parent.Padding.Right + 1)
                throw new Exception($"Horizontal clipping: {child.Name} / {child.Text}");
            if (child.Bottom > parent.ClientSize.Height - parent.Padding.Bottom + 1)
                throw new Exception($"Vertical clipping: {child.Name} / {child.Text}");
            if (child is Label or CheckBox or Button)
            {
                var wanted = child.GetPreferredSize(new Size(child.Width, 0));
                if (wanted.Height > child.Height + 1)
                    throw new Exception($"Text height clipped: {child.Text}; actual {child.Height}, required {wanted.Height}");
            }
            Check(child);
        }
        for (int i = 0; i < children.Length; i++)
            for (int j = i + 1; j < children.Length; j++)
                if (children[i].Bounds.IntersectsWith(children[j].Bounds))
                    throw new Exception($"Controls overlap: {children[i].Text} / {children[j].Text}");
    }
}
