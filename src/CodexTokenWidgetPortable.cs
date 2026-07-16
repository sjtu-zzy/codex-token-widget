using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CodexTokenWidgetPortable
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TokenWidgetForm());
        }
    }

    internal sealed class TokenSummary
    {
        public long InputTokens;
        public long OutputTokens;
        public long TotalTokens;
        public int Calls;
        public DateTime Updated;
    }

    internal sealed class TokenScanner
    {
        private static readonly Regex TimestampRegex = new Regex("\"timestamp\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex LastUsageRegex = new Regex("\"last_token_usage\"\\s*:\\s*\\{([^}]*)\\}", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new Regex("\"(?<key>input_tokens|cached_input_tokens|output_tokens|reasoning_output_tokens|total_tokens)\"\\s*:\\s*(?<value>\\d+)", RegexOptions.Compiled);

        public TokenSummary Summarize(int days)
        {
            DateTime cutoff = RangeCutoff(days);
            TokenSummary summary = new TokenSummary();
            summary.Updated = DateTime.Now;
            HashSet<string> seen = new HashSet<string>();

            string sessions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "sessions");

            if (!Directory.Exists(sessions))
            {
                return summary;
            }

            foreach (string path in EnumerateJsonlFiles(sessions))
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (info.Length > 80L * 1024L * 1024L)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            ParseLine(line, cutoff, seen, summary);
                        }
                    }
                }
                catch
                {
                }
            }

            return summary;
        }

        private static DateTime RangeCutoff(int range)
        {
            if (range < 0)
            {
                return DateTime.MinValue;
            }
            if (range == 0)
            {
                return DateTime.Today;
            }
            return DateTime.Now.AddDays(-range);
        }

        private static IEnumerable<string> EnumerateJsonlFiles(string root)
        {
            Queue<string> dirs = new Queue<string>();
            dirs.Enqueue(root);
            while (dirs.Count > 0)
            {
                string dir = dirs.Dequeue();
                string[] files = new string[0];
                try { files = Directory.GetFiles(dir, "*.jsonl"); } catch { }
                foreach (string file in files)
                {
                    yield return file;
                }

                string[] children = new string[0];
                try { children = Directory.GetDirectories(dir); } catch { }
                foreach (string child in children)
                {
                    dirs.Enqueue(child);
                }
            }
        }

        private static void ParseLine(string line, DateTime cutoff, HashSet<string> seen, TokenSummary summary)
        {
            if (line.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0 ||
                line.IndexOf("\"last_token_usage\"", StringComparison.Ordinal) < 0)
            {
                return;
            }

            Match timestampMatch = TimestampRegex.Match(line);
            if (!timestampMatch.Success)
            {
                return;
            }

            DateTime timestamp;
            if (!DateTime.TryParse(timestampMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
            {
                return;
            }
            timestamp = timestamp.ToLocalTime();
            if (timestamp < cutoff)
            {
                return;
            }

            Match usageMatch = LastUsageRegex.Match(line);
            if (!usageMatch.Success)
            {
                return;
            }

            long input = 0;
            long cached = 0;
            long output = 0;
            long reasoning = 0;
            long total = 0;
            foreach (Match number in NumberRegex.Matches(usageMatch.Groups[1].Value))
            {
                long value;
                if (!long.TryParse(number.Groups["value"].Value, out value))
                {
                    continue;
                }

                string key = number.Groups["key"].Value;
                if (key == "input_tokens") input = value;
                else if (key == "cached_input_tokens") cached = value;
                else if (key == "output_tokens") output = value;
                else if (key == "reasoning_output_tokens") reasoning = value;
                else if (key == "total_tokens") total = value;
            }

            input += cached;
            output += reasoning;
            if (total <= 0)
            {
                total = input + output;
            }
            if (total <= 0)
            {
                return;
            }

            string dedupeKey = timestamp.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + total.ToString(CultureInfo.InvariantCulture) + ":" + input.ToString(CultureInfo.InvariantCulture);
            if (!seen.Add(dedupeKey))
            {
                return;
            }

            summary.InputTokens += input;
            summary.OutputTokens += output;
            summary.TotalTokens += total;
            summary.Calls += 1;
        }
    }

    internal sealed class TokenWidgetForm : Form
    {
        private const int WidgetWidth = 340;
        private const int WidgetHeight = 276;
        private const int Radius = 22;
        private const int MarginPx = 24;
        private readonly Color Back = Color.FromArgb(15, 23, 42);
        private readonly Color Header = Color.FromArgb(17, 24, 39);
        private readonly Color Card = Color.FromArgb(21, 29, 43);
        private readonly Color Accent = Color.FromArgb(14, 165, 233);
        private readonly TokenScanner scanner = new TokenScanner();
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly Button[] periodButtons;
        private readonly Label totalLabel;
        private readonly Label subLabel;
        private readonly Label inputLabel;
        private readonly Label outputLabel;
        private readonly Label callsLabel;
        private int selectedRange = 0;
        private Point dragOffset;
        private bool refreshing;

        public TokenWidgetForm()
        {
            Text = "Codex Token Widget";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(WidgetWidth, WidgetHeight);
            TopMost = true;
            BackColor = Back;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            Location = new Point(Math.Max(MarginPx, Screen.PrimaryScreen.WorkingArea.Right - WidgetWidth - MarginPx), Screen.PrimaryScreen.WorkingArea.Top + MarginPx);

            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Back;
            root.Padding = Padding.Empty;
            Controls.Add(root);

            Panel header = new Panel();
            header.Location = new Point(0, 0);
            header.Size = new Size(WidgetWidth, 46);
            header.BackColor = Header;
            header.MouseDown += StartDrag;
            header.MouseMove += DragWindow;
            root.Controls.Add(header);

            Panel close = MacDot(Color.FromArgb(255, 95, 87));
            close.Location = new Point(14, 16);
            close.Click += delegate { Application.Exit(); };
            header.Controls.Add(close);

            Panel minimize = MacDot(Color.FromArgb(254, 188, 46));
            minimize.Location = new Point(34, 16);
            minimize.Click += delegate { HideToTray(); };
            header.Controls.Add(minimize);

            Label title = new Label();
            title.AutoSize = true;
            title.Text = "CODEX TOKENS";
            title.ForeColor = Color.FromArgb(229, 237, 248);
            title.BackColor = Header;
            title.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Regular);
            title.Location = new Point(64, 15);
            title.MouseDown += StartDrag;
            title.MouseMove += DragWindow;
            header.Controls.Add(title);

            Panel body = new Panel();
            body.Location = new Point(0, 46);
            body.Size = new Size(WidgetWidth, WidgetHeight - 46);
            body.BackColor = Back;
            body.Padding = new Padding(18, 12, 18, 16);
            root.Controls.Add(body);

            totalLabel = new Label();
            totalLabel.Text = "--";
            totalLabel.ForeColor = Color.White;
            totalLabel.BackColor = Back;
            totalLabel.Font = new Font("Segoe UI", 26f, FontStyle.Bold);
            totalLabel.Location = new Point(18, 14);
            totalLabel.Size = new Size(300, 42);
            body.Controls.Add(totalLabel);

            subLabel = new Label();
            subLabel.Text = "Reading local logs...";
            subLabel.ForeColor = Color.FromArgb(148, 163, 184);
            subLabel.BackColor = Back;
            subLabel.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            subLabel.Location = new Point(20, 55);
            subLabel.Size = new Size(300, 20);
            body.Controls.Add(subLabel);

            inputLabel = AddStat(body, "INPUT", new Point(18, 84));
            outputLabel = AddStat(body, "OUTPUT", new Point(119, 84));
            callsLabel = AddStat(body, "CALLS", new Point(220, 84));

            periodButtons = new Button[]
            {
                AddPeriod(body, "Today", 0, new Point(18, 147)),
                AddPeriod(body, "24h", 1, new Point(79, 147)),
                AddPeriod(body, "7d", 7, new Point(140, 147)),
                AddPeriod(body, "30d", 30, new Point(201, 147)),
                AddPeriod(body, "All", -1, new Point(262, 147))
            };

            Button refresh = new Button();
            refresh.Text = "Refresh";
            refresh.FlatStyle = FlatStyle.Flat;
            refresh.FlatAppearance.BorderSize = 0;
            refresh.BackColor = Color.FromArgb(15, 118, 110);
            refresh.ForeColor = Color.FromArgb(236, 254, 255);
            refresh.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Regular);
            refresh.Location = new Point(18, 184);
            refresh.Size = new Size(294, 30);
            refresh.Click += delegate { RefreshUsage(); };
            body.Controls.Add(refresh);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, delegate { ShowFromTray(); });
            menu.Items.Add("Refresh", null, delegate { RefreshUsage(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Application.Exit(); });
            Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon != null)
            {
                Icon = appIcon;
            }
            tray = new NotifyIcon();
            tray.Icon = Icon ?? SystemIcons.Application;
            tray.Text = "Codex Token Widget";
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowFromTray(); };

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 60000;
            refreshTimer.Tick += delegate { RefreshUsage(); };
            refreshTimer.Start();

            UpdatePeriodStyles();
            Shown += delegate { ApplyRoundedRegion(); RefreshUsage(); };
            Resize += delegate { ApplyRoundedRegion(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Visible = false;
                tray.Dispose();
                refreshTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private Panel MacDot(Color color)
        {
            Panel dot = new Panel();
            dot.Size = new Size(13, 13);
            dot.BackColor = color;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, 12, 12);
                dot.Region = new Region(path);
            }
            dot.Cursor = Cursors.Hand;
            return dot;
        }

        private Label AddStat(Control parent, string caption, Point location)
        {
            Panel panel = new Panel();
            panel.BackColor = Card;
            panel.Location = location;
            panel.Size = new Size(91, 48);
            parent.Controls.Add(panel);

            Label cap = new Label();
            cap.Text = caption;
            cap.ForeColor = Color.FromArgb(125, 211, 252);
            cap.BackColor = Card;
            cap.Font = new Font("Segoe UI", 7f, FontStyle.Bold);
            cap.Location = new Point(8, 5);
            cap.Size = new Size(76, 14);
            panel.Controls.Add(cap);

            Label value = new Label();
            value.Text = "--";
            value.ForeColor = Color.White;
            value.BackColor = Card;
            value.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Regular);
            value.Location = new Point(8, 21);
            value.Size = new Size(76, 20);
            panel.Controls.Add(value);
            return value;
        }

        private Button AddPeriod(Control parent, string text, int days, Point location)
        {
            Button button = new Button();
            button.Text = text;
            button.Tag = days;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Regular);
            button.Location = location;
            button.Size = new Size(52, 30);
            button.Click += delegate
            {
                selectedRange = days;
                UpdatePeriodStyles();
                RefreshUsage();
            };
            parent.Controls.Add(button);
            return button;
        }

        private void UpdatePeriodStyles()
        {
            foreach (Button button in periodButtons)
            {
                int days = (int)button.Tag;
                button.BackColor = days == selectedRange ? Accent : Color.FromArgb(30, 41, 59);
                button.ForeColor = Color.FromArgb(219, 234, 254);
            }
        }

        private void StartDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            }
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y);
            }
        }

        private void ApplyRoundedRegion()
        {
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region = new Region(path);
            }
        }

        private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
            path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
            path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void RefreshUsage()
        {
            if (refreshing)
            {
                return;
            }
            refreshing = true;
            subLabel.Text = "Refreshing...";
            int range = selectedRange;
            ThreadPool.QueueUserWorkItem(delegate
            {
                TokenSummary summary = scanner.Summarize(range);
                BeginInvoke((MethodInvoker)delegate
                {
                    Render(summary, range);
                    refreshing = false;
                });
            });
        }

        private void Render(TokenSummary summary, int range)
        {
            totalLabel.Text = FormatNumber(summary.TotalTokens);
            inputLabel.Text = FormatNumber(summary.InputTokens);
            outputLabel.Text = FormatNumber(summary.OutputTokens);
            callsLabel.Text = FormatNumber(summary.Calls);
            string period = RangeLabel(range);
            subLabel.Text = period + " | local logs | " + summary.Updated.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            tray.Text = ("Codex tokens " + period + ": " + FormatNumber(summary.TotalTokens));
        }

        private static string RangeLabel(int range)
        {
            if (range < 0) return "all";
            if (range == 0) return "today";
            if (range == 1) return "24h";
            return range.ToString(CultureInfo.InvariantCulture) + "d";
        }

        private static string FormatNumber(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private void HideToTray()
        {
            Hide();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Activate();
        }
    }
}
