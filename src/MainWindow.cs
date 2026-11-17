using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiTimer = System.Windows.Forms.Timer;

namespace Nabz
{
internal sealed partial class MainWindow: Form
{
    enum Job {Idle, Survey, Write}
    readonly Panel viewport = new Panel(), sheet = new Panel(), strip = new Panel();
    readonly Cover cover = new Cover();
    readonly Scope scope = new Scope();
    readonly Table grid = new Table();
    readonly Selector nic = new Selector(), profile = new Selector();
    readonly WinSettings io = new WinSettings();
    readonly ToolTip tips = new ToolTip {AutoPopDelay = 10000};
    readonly UiTimer kickoff = new UiTimer {Interval = 430}, networkTimer = new UiTimer {Interval = 850}, watchTimer = new
    UiTimer {Interval = 10000}, clock = new UiTimer {Interval = 15000};
    KeyButton explorer, match, help, refresh, run, action, restore, detail, export;
    Label netCap, profileCap, current, section, legend, note, watchDesc;
    Panel rule;
    readonly CheckBox iran = new CheckBox();
    KeyButton referenceButton;
    Reference reference;
    string consentKey = "";
    bool Iran
    {
        get {
            return iran.Checked && consentKey == Key(Net) && Net != null;
        }
    }
    static string CompareKey(Adapter a)
    {
        return Key(a) + "|" + (a == null || a.Effective == null ? "" : String.Join(",", a.Effective));
    }

    Session saved;
    bool active, blocked, locking, matchView, recording, sampling, ready, reflow, closeLater, restoreLater, autoLater;
    Job job;
    float dpi = 1;
    string netKey = "", reportKey = "", watchKey = "";
    DateTime reportAt;
    Finding[] findings;
    Tick last;
    CancellationTokenSource searchCts, watchCts;
    string noteText = "";
    Adapter Net
    {
        get {
            return nic.SelectedItem as Adapter;
        }
    } Profile Target
    {
        get {
            return profile.SelectedItem as Profile ?? Profile.All[0];
        }
    } Finding Pick
    {
        get {
            if (findings == null || grid.CurrentCell == null)
                return null;
            int n = grid.CurrentCell.RowIndex;
            return n >= 0 && n < findings.Length ? findings[n] : null;
        }
    }
    int D(float x)
    {
        return (int)Math.Round(x * dpi);
    } static string Key(Adapter a)
    {
        return a == null ? "" : a.Id.ToString("D") + "/" + a.Index + "/" + a.Local + "/" + a.Gateway;
    } string ReportId
    {
        get {
            return CompareKey(Net) + "@" + Target.Id + (Iran ? "/ir" : "/public");
        }
    }
    T Add<T>(Control p, T c)where T: Control
    {
        p.Controls.Add(c);
        return c;
    }
    Label L(Control p, string text, float size, Color c, bool bold, bool rtl)
    {
        return Add(p, new Label {Text = text, AutoSize = false, Font = O.F(size, bold), ForeColor = c, BackColor = p.BackColor, AutoEllipsis = true, RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No, TextAlign = rtl ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft, UseMnemonic = false});
    }
    KeyButton B(Control p, string text, bool highlight = false)
    {
        return Add(p, new KeyButton(text, highlight ? O.Amber : O.Panel2, highlight ? O.Ground : O.Ink,
                                    highlight ? O.Amber : O.Edge));
    }
    void Pos(Control c, int x, int y, int w, int h)
    {
        c.SetBounds(D(x), D(y), Math.Max(1, D(w)), Math.Max(1, D(h)));
    }
    readonly bool automatic;
    internal MainWindow(): this(true) {}
    internal MainWindow(bool automatic)
    {
        this.automatic = automatic;
        Text = "نبض مدار   /   NABZ ORBIT 2.1";
        BackColor = O.Ground;
        ForeColor = O.Ink;
        Font = O.F(10);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        try {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        } catch {}
        using (var g = CreateGraphics())
            dpi = Math.Max(.8f, g.DpiX / 96f);
        var work = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(D(580), work.Width), Math.Min(D(440), work.Height));
        ClientSize = new Size(Math.Min(D(1032), work.Width - D(16)), Math.Min(D(704), work.Height - D(54)));
        Add(this, viewport);
        viewport.Dock = DockStyle.Fill;
        viewport.AutoScroll = true;
        viewport.BackColor = O.Ground;
        viewport.AutoScrollMinSize = new Size(D(833), D(623));
        Add(viewport, sheet);
        sheet.BackColor = O.Ground;
        Add(sheet, cover);
        explorer = B(sheet, "کاوش DNS");
        match = B(sheet, "پایش مسابقه");
        help = B(sheet, "راهنما");
        current = L(sheet, "", 9, O.Soft, false, true);
        Add(sheet, strip);
        strip.BackColor = O.Panel;
        netCap = L(strip, "کارت شبکه", 8.8f, O.Soft, false, true);
        profileCap = L(strip, "مقصد آزمایش وب", 8.8f, O.Soft, false, true);
        Add(strip, nic);
        Add(strip, profile);
        nic.AccessibleName = "کارت شبکه";
        profile.AccessibleName = "انتخاب مقصد آزمایش";
        nic.ItemHeight = profile.ItemHeight = D(23);
        refresh = B(strip, "تازه‌سازی رابط");
        run = B(strip, "سنجش دوباره");
        watchDesc = L(strip, "هر ۱۰ ثانیه یک نمونه؛ بدون تغییر DNS", 10, O.Lilac, true, true);
        section = L(sheet, "نتایج کاوش", 13, O.Ink, true, true);
        legend = L(sheet, "", 9, O.Soft, false, true);
        Add(sheet, iran);
        iran.Text = "رادار و ۴۰۳ (+۲)";
        iran.Font = O.F(9, true);
        iran.ForeColor = O.Lilac;
        iran.BackColor = O.Ground;
        iran.AutoSize = false;
        iran.RightToLeft = RightToLeft.Yes;
        tips.SetToolTip(iran,
                        "دو جفت با آدرس خصوصی، فقط برای اتصال شخصی در ایران و بدون VPN. پیش‌فرض خاموش است.");
        referenceButton = B(sheet, "مقایسه با DNS فعلی");
        referenceButton.Font = O.F(9.3f);
        referenceButton.Click += delegate {
            ReferenceDetails();
        };
        Add(sheet, grid);
        SetGrid();
        Add(sheet, scope);
        rule = Add(sheet, new Panel {BackColor = O.Edge});
        note = L(sheet, "", 9.8f, O.Soft, false, true);
        note.AccessibleName = "پیام عملیات";
        action = B(sheet, "اعمال DNS منتخب", true);
        restore = B(sheet, "قطع و بازگردانی");
        detail = B(sheet, "جزئیات ردیف");
        export = B(sheet, "خروجی CSV");
        tips.SetToolTip(explorer,
                        "کاوش فقط می‌سنجد؛ اعمال جداگانه و با اجازهٔ شماست.");
        tips.SetToolTip(match,
                        "مقایسهٔ مودم، یک مقصد اینترنتی مستقل از DNS و DNS فعلی؛ نه پینگ خود بازی");
        tips.SetToolTip(restore,
                        "بازیابی تنظیم قبل از اولین اتصال؛ پشتیبان هر دو نسخهٔ قبلی هم خوانده می‌شود.");
        foreach (var p in Profile.All)
            profile.Items.Add(p);
        profile.SelectedItem = LoadProfile();
        explorer.Click += delegate {
            View(false);
        };
        match.Click += delegate {
            View(true);
        };
        help.Click += delegate {
            ShowHelp();
        };
        refresh.Click += delegate {
            Inventory(true);
        };
        run.Click += async delegate {
            if (job == Job.Survey) {
                autoLater = false;
                if (searchCts != null)
                    searchCts.Cancel();
            } else
                await Scan();
        };
        action.Click += async delegate {
            if (matchView) {
                if (recording)
                    StopRecord("پایش متوقف شد. نمونه‌ها تا شروع پایش بعدی در دسترس‌اند.");
                else
                    await StartRecord();
            } else
                await Apply();
        };
        restore.Click += async delegate {
            if (job == Job.Survey) {
                restoreLater = true;
                if (searchCts != null)
                    searchCts.Cancel();
            } else
                await Restore();
        };
        detail.Click += delegate {
            if (matchView) {
                if (scope.Frames.Count > 0) {
                    scope.Frames[scope.Frames.Count - 1].Mark = DateTimeOffset.Now;
                    scope.Invalidate();
                    Say("زمان علامت‌گذاری در آخرین نمونه ثبت شد و در CSV هم می‌آید.");
                }
            } else
                Details();
        };
        export.Click += delegate {
            Export();
        };
        iran.CheckedChanged += delegate {
            if (locking || !ready)return;
            if (iran.Checked) {
                bool ok = RegionOptIn();
                if (!ok) {
                    locking = true;
                    iran.Checked = false;
                    locking = false;
                    return;
                }
                consentKey = Key(Net);
            } else consentKey = "";
            if (job == Job.Survey) {
                autoLater = true;
                searchCts.Cancel();
            }
            ClearResults();
            Buttons();
            QueueScan();
        };
        grid.SelectionChanged += delegate {
            if (ready && !locking)
                Buttons();
        };
        grid.CellDoubleClick += delegate(object o, DataGridViewCellEventArgs e) {
            if (e.RowIndex >= 0)
                Details();
        };
        nic.SelectedIndexChanged += delegate {
            if (!locking && ready && job != Job.Write) {
                if (job == Job.Survey) {
                    autoLater = true;
                    searchCts.Cancel();
                }
                ClearRegion();
                netKey = Key(Net);
                ClearResults();
                scope.Frames.Clear();
                ReadActive();
                Buttons();
                DefaultNote();
                QueueScan();
            }
        };
        profile.SelectedIndexChanged += delegate {
            if (!ready || locking)
                return;
            if (job == Job.Survey) {
                autoLater = true;
                searchCts.Cancel();
            }
            SaveProfile();
            ClearResults();
            Buttons();
            QueueScan();
        };
        kickoff.Tick += async delegate {
            kickoff.Stop();
            if (!matchView && job == Job.Idle && !recording && Net != null && Net.Usable)
                await Scan();
        };
        networkTimer.Tick += delegate {
            networkTimer.Stop();
            if (job == Job.Idle && !nic.DroppedDown && !profile.DroppedDown)
                Inventory(false);
        };
        watchTimer.Tick += async delegate {
            await Sample();
        };
        clock.Tick += delegate {
            if (ready && !IsDisposed)
                Buttons();
        };
        viewport.Resize += delegate {
            LayoutAll();
        };
        FormClosing += Leaving;
        FormClosed += delegate {
            ready = false;
            kickoff.Dispose();
            networkTimer.Dispose();
            watchTimer.Dispose();
            clock.Dispose();
            if (watchCts != null)
                watchCts.Cancel();
            if (Adapters.IsWindows)
                NetworkChange.NetworkAddressChanged -= NetworkEvent;
            StopPassive(true);
            tips.Dispose();
        };
        Shown += delegate {
            ready = true;
            LayoutAll();
            Inventory(true);
            clock.Start();
            StartPassive();
            if (saved != null) {
                kickoff.Stop();
                Say("نشست قبلی پیدا شد. اگر سایت‌ها باز نمی‌شوند، اول «قطع و بازگردانی» را بزن؛ برای مقایسه «سنجش دوباره» را بزن.",
                    true);
            }
            if (Adapters.IsWindows)
                try {
                    NetworkChange.NetworkAddressChanged += NetworkEvent;
                } catch (NetworkInformationException) {};
        };
        SetRows();
        LayoutAll();
        Buttons();
    }
    static string PrefPath
    {
        get {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NabzDNS",
                                "Orbit.profile");
        }
    }
    static Profile LoadProfile()
    {
        try {
            string id = File.ReadAllText(PrefPath).Trim();
            return Profile.All.FirstOrDefault(p => p.Id == id) ?? Profile.All[0];
        } catch {
            return Profile.All[0];
        }
    }
    void SaveProfile()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefPath));
            File.WriteAllText(PrefPath, Target.Id);
        } catch {}
    }
    void NetworkEvent(object sender, EventArgs e)
    {
        if (!ready || IsDisposed || !IsHandleCreated)
            return;
        try {
            BeginInvoke((MethodInvoker)delegate {
                if (ready && !IsDisposed) {
                    networkTimer.Stop();
                    networkTimer.Start();
                }
            });
        } catch (InvalidOperationException) {}
    }
    void LayoutAll()
    {
        if (reflow || export == null)
            return;
        reflow = true;
        try {
            int w = Math.Max(833, (int)(viewport.ClientSize.Width / dpi)), h = Math.Max(623,
                    (int)(viewport.ClientSize.Height / dpi)), p = 18, b = w - 2 * p;
            sheet.Size = new Size(D(w), D(h));
            Pos(cover, p, 13, b, 116);
            Pos(explorer, w - p - 131, 138, 131, 34);
            Pos(match, w - p - 267, 138, 128, 34);
            Pos(help, p, 139, 64, 32);
            Pos(current, p + 75, 138, b - 355, 34);
            Pos(strip, p, 182, b, 66);
            int nw = (int)(b * .29), tw = (int)(b * .27);
            Pos(netCap, b - nw - 14, 2, nw, 23);
            Pos(nic, b - nw - 14, 27, nw, 30);
            Pos(profileCap, b - nw - tw - 30, 2, tw, 23);
            Pos(profile, b - nw - tw - 30, 27, tw, 30);
            Pos(refresh, 14, 23, 103, 32);
            Pos(run, 127, 23, 120, 32);
            Pos(watchDesc, 134, 13, b - nw - 167, 43);
            Pos(section, w - p - 245, 251, 245, 33);
            Pos(iran, p, 251, 150, 33);
            Pos(legend, matchView ? p + 3 : p + 160, 251, matchView ? b - 258 : b - 418, 33);
            Pos(referenceButton, p, 286, b, 30);
            Pos(grid, p, 322, b, h - 431);
            Pos(scope, p, 287, b, h - 396);
            RowsHeight();
            Pos(rule, p, h - 101, b, 1);
            Pos(note, p + 3, h - 97, b - 6, 36);
            Pos(action, w - p - 192, h - 57, 192, 40);
            Pos(restore, w - p - 353, h - 57, 151, 40);
            Pos(export, p, h - 55, 109, 36);
            Pos(detail, p + 119, h - 55, 128, 36);
        } finally {
            reflow = false;
        }
    }
    void SetGrid()
    {
        grid.BackgroundColor = O.Panel;
        grid.ForeColor = O.Ink;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeColumns = grid.AllowUserToResizeRows =
                                      grid.AllowUserToOrderColumns = false;
        grid.ScrollBars = ScrollBars.Vertical;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = O.Edge;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = D(30);
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.AccessibleName = "جدول کیفیت DNS و دسترسی وب";
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
        var hd = grid.ColumnHeadersDefaultCellStyle;
        hd.BackColor = O.Panel2;
        hd.ForeColor = O.Soft;
        hd.Font = O.F(9, true);
        hd.Alignment = DataGridViewContentAlignment.MiddleCenter;
        hd.SelectionBackColor = O.Panel2;
        hd.SelectionForeColor = O.Ink;
        hd.WrapMode = DataGridViewTriState.False;
        var c = grid.DefaultCellStyle;
        c.BackColor = O.Panel;
        c.ForeColor = O.Ink;
        c.Font = O.F(9.8f);
        c.Alignment = DataGridViewContentAlignment.MiddleCenter;
        c.SelectionBackColor = O.Selection;
        c.SelectionForeColor = O.Ink;
        grid.AlternatingRowsDefaultCellStyle.BackColor = O.H("#211D28");
        string[] heads = {"وب مقصد", "P90", "میانه", "پاسخ موفق", "DNS اول   /   جایگزین", "ارزیابی", "سرویس", "وب عمومی"};
        int[] weights = {8, 7, 7, 11, 30, 15, 13, 9};
        for (int i = 0; i < heads.Length; i++) {
            var col = new DataGridViewTextBoxColumn {Name = "c" + i, HeaderText = heads[i], SortMode = DataGridViewColumnSortMode.NotSortable, FillWeight = weights[i], MinimumWidth = D(36)};
            grid.Columns.Add(col);
        }
        foreach (int i in new[] {1, 2, 4})
            grid.Columns[i].DefaultCellStyle.Font = O.F(9.2f, false, true);
        grid.Columns[6].DefaultCellStyle.Font = O.F(10.2f, true, true);
        grid.Columns[7].DisplayIndex = 1;
        grid.CellPainting += Signals;
    }
    void Signals(object sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.ColumnIndex != 3 || e.RowIndex < 0 || findings == null || e.RowIndex >= findings.Length)
            return;
        var f = findings[e.RowIndex];
        var r = e.CellBounds;
        bool selected = grid.Rows[e.RowIndex].Selected;
        using (var bg = new SolidBrush(selected ? O.Selection : (e.RowIndex % 2 == 0 ? O.Panel : O.H("#211D28"))))
            e.Graphics.FillRectangle(bg, r);
        using (var pen = new Pen(O.Edge))
            e.Graphics.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
        O.Txt(e.Graphics, O.N(f.Good) + " / ۲۰", new Rectangle(r.X, r.Y, r.Width, Math.Max(10, r.Height - D(8))), O.F(9.2f),
              O.Ink, true, true);
        int gap = D(2), unit = Math.Max(2, Math.Min(D(6), (r.Width - D(10)) / 10 - gap)),
            x = r.X + (r.Width - 10 * (unit + gap) + gap) / 2;
        for (int i = 0; i < 10; i++) {
            int n = (f.A.Samples[i].HasValue ? 1 : 0) + (f.B.Samples[i].HasValue ? 1 : 0);
            using (var b = new SolidBrush(n == 2 ? O.Amber : n == 1 ? O.Lilac : O.Rose))
                e.Graphics.FillRectangle(b, x + i * (unit + gap), r.Bottom - D(6), unit, D(n == 0 ? 1 : 3));
        }
        e.Handled = true;
    }
    void RowsHeight()
    {
        if (grid.Rows.Count == 0)
            return;
        int rowHeight = Math.Max(D(35), Math.Min(D(46), (grid.ClientSize.Height - grid.ColumnHeadersHeight - 1) / grid.Rows.Count));
        foreach (DataGridViewRow row in grid.Rows)row.Height = rowHeight;
    }
    void SetRows()
    {
        bool previous = locking;
        locking = true;
        try {
            grid.SuspendLayout();
            grid.Rows.Clear();
            var ps = findings == null ? Catalog.Available(Iran) : findings.Select(x => x.P).ToArray();
            for (int i = 0; i < ps.Length; i++) {
                var p = ps[i];
                var r = findings == null ? null : findings[i];
                var pair = p.Pair(r != null && r.Reverse);
                int n = grid.Rows.Add(r == null ? "—" : r.A.Web.State == "skipped"
                                      && r.B.Web.State == "skipped" ? "تست نشد" : O.N(r.WebCount) + " / ۲", r == null ? "—" : O.Ms(r.P90),
                                      r == null ? "—" : O.Ms(r.Median), r == null ? "—" : O.N(r.Good) + " / ۲۰", String.Join("  /  ", pair),
                                      r == null ? "سنجیده نشده" : r.Verdict, p.Name, r == null ? "—" : r.A.General.State == "skipped" &&
                                      r.B.General.State == "skipped" ? "تست نشد" : O.N((r.A.General.Ok ? 1 : 0) + (r.B.General.Ok ? 1 : 0)) + " / ۲");
                var row = grid.Rows[n];
                foreach (DataGridViewCell cell in row.Cells)
                    cell.ToolTipText = p.Note + (r == null ? "" : "\n" + r.A.Web.Label + "  /  " + r.B.Web.Label +
                                                 "\nبرای جزئیات دوبار کلیک کن.");
                row.Cells[5].Style.ForeColor = r == null ? O.Muted : r.Eligible ? O.Amber : O.Rose;
            }
            if (findings == null || (reference != null && reference.PreferCurrent(findings.FirstOrDefault(x => x.Eligible)))) {
                grid.ClearSelection();
                grid.CurrentCell = null;
            } else if (grid.Rows.Count > 0)
                grid.CurrentCell = grid.Rows[0].Cells[0];
            grid.ResumeLayout();
            if (grid.Columns.Count > 0) {
                grid.FirstDisplayedScrollingColumnIndex = 0;
                grid.HorizontalScrollingOffset = 0;
            }
            RowsHeight();
        } finally {
            locking = previous;
        }
    }
    void ClearResults()
    {
        findings = null;
        reference = null;
        reportKey = "";
        last = null;
        SetRows();
    }
    void Inventory(bool manual)
    {
        if (job != Job.Idle)
            return;
        string old = netKey;
        var previous = Net;
        string oldActive = saved == null ? "" : saved.Token.ToString();
        locking = true;
        blocked = false;
        try {
            saved = Adapters.IsWindows ? io.Load() : null;
            var choices = Adapters.List().Where(a => saved == null ? a.Usable : a.Id == saved.Adapter).ToList();
            if (saved != null && choices.Count == 0)
                choices.Add(new Adapter {Id = saved.Adapter, Name = "رابط ذخیره‌شده · در دسترس نیست", Effective = new string[0]});
            var sel = previous == null ? choices.FirstOrDefault() : choices.FirstOrDefault(a => a.Id == previous.Id) ??
                      choices.FirstOrDefault();
            nic.BeginUpdate();
            try {
                nic.Items.Clear();
                foreach (var a in choices)
                    nic.Items.Add(a);
                if (sel != null)
                    nic.SelectedItem = sel;
                else {
                    nic.Items.Add("رابط فعالی پیدا نشد");
                    nic.SelectedIndex = 0;
                }
            } finally {
                nic.EndUpdate();
            }
            netKey = Key(Net);
            ReadActive();
        } catch {
            blocked = true;
            saved = null;
            active = false;
            netKey = "";
        } finally {
            locking = false;
        }
        bool changed = old != netKey;
        if (changed)ClearRegion();
        if (changed || manual || CompareKey(Net) != CompareKey(previous)) {
            ClearResults();
        }
        if (recording && WatchId() != watchKey)
            StopRecord("رابط یا DNS تغییر کرد؛ پایش متوقف شد تا نمونه‌های دو اتصال مخلوط نشوند.");
        Buttons();
        if (changed || manual || oldActive != (saved == null ? "" : saved.Token.ToString()) || blocked
                || String.IsNullOrEmpty(noteText))
            DefaultNote();
        if ((changed || manual || CompareKey(Net) != CompareKey(previous)) && saved == null)
            QueueScan();
    }
    void ReadActive()
    {
        active = false;
        var a = Net;
        if (saved == null || a == null || !a.Usable)
            return;
        try {
            active = saved.Phase == "active" && io.Read(a.Id).SequenceEqual(saved.Target)
                     && a.Effective.SequenceEqual(saved.Target);
        } catch {}
    }
    void QueueScan()
    {
        kickoff.Stop();
        if (automatic && ready && !matchView && !blocked && Net != null && Net.Usable && job == Job.Idle)
            kickoff.Start();
    }
    void View(bool watch)
    {
        if (matchView == watch || job == Job.Write)
            return;
        matchView = watch;
        LayoutAll();
        kickoff.Stop();
        if (watch && job == Job.Survey) {
            if (searchCts != null)
                searchCts.Cancel();
        }
        if (!watch && recording)
            StopRecord("پایش متوقف شد؛ گزارش همچنان در برگهٔ پایش موجود است.");
        if (!watch && (findings == null || !Survey.Fresh(reportKey, ReportId, reportAt, DateTime.UtcNow))) {
            if (job == Job.Idle)
                QueueScan();
            else
                autoLater = true;
        }
        DefaultNote();
        Buttons();
    }
    void Say(string text, bool warn = false)
    {
        noteText = text;
        note.Text = text;
        note.ForeColor = warn ? O.Rose : O.Soft;
        tips.SetToolTip(note, text);
    }
    void DefaultNote()
    {
        if (blocked) {
            Say("خواندن تنظیمات یا پشتیبان ممکن نشد؛ برای حفظ تنظیمات، اعمال غیرفعال است.",
                true);
            return;
        }
        if (Net == null || !Net.Usable) {
            Say(saved != null ?
                "پشتیبان موجود است. همان کارت شبکه را وصل کن تا تنظیم قبلی برگردد."
                : "وای‌فای یا کابل را وصل کن؛ با شناسایی رابط IPv4، کاوش خودکار شروع می‌شود.",
                true);
            return;
        }
        if (matchView) {
            Say(recording ? MatchWatch.Message(MatchWatch.Diagnose(scope.Frames)) :
                "در زمان بازی «شروع پایش» را بزن. کاوش سنگین و تعویض خودکار DNS در این حالت انجام نمی‌شود.");
            return;
        }
        if (saved != null && !active) {
            Say("پشتیبان موجود است، اما تنظیم فعلی با آن نشست یکی نیست. برای بازیابی از «قطع و بازگردانی» استفاده کن.",
                true);
            return;
        }
        Say(findings == null ?
            "کاوش فقط اندازه می‌گیرد. تا «آزمون موقت» را نزنی، تنظیم DNS ویندوز دست‌نخورده می‌ماند."
            : "اولویت با پاسخ‌های پایدار و وبِ تأییدشده است، سپس زمان پاسخ در نمونه‌های کندتر.");
    }
    void Buttons()
    {
        if (export == null)
            return;
        var a = Net;
        var r = Pick;
        bool idle = job == Job.Idle, can = a != null && a.Usable && !blocked, fresh = findings != null
                                           && Survey.Fresh(reportKey, ReportId, reportAt, DateTime.UtcNow);
        bool equal = saved != null && r != null && saved.Target.SequenceEqual(r.P.Pair(r.Reverse)) && active;
        if (a != null && r != null && a.Effective != null && a.Effective.Length == 2 &&
                a.Effective.OrderBy(x => x).SequenceEqual(r.P.Pair(false).OrderBy(x => x)))equal = true;
        iran.Visible = !matchView;
        iran.Enabled = job != Job.Write && !recording && can;
        referenceButton.Visible = !matchView;
        referenceButton.Enabled = reference != null && job == Job.Idle;
        referenceButton.Text = reference == null ? "خط مبنا: DNS فعلی هم در همین کاوش آزمایش می‌شود" :
                               reference.Items.Length == 0 ? "خط مبنا: نشانی IPv4 قابل مقایسه یافت نشد" :
                               (reference.Partial ? "مقایسهٔ ناقص    /    " : "DNS فعلی    /    ") + O.N(reference.Good) + " از " + O.N(
                                   reference.Items.Length * 10) + " پاسخ     ·     P90: " + O.Ms(reference.P90) + "     ·     وب مقصد: " + O.N(
                                   reference.Items.Count(t => t.Web.Ok)) + " از " + O.N(reference.Items.Length) + "     ·     جزئیات";
        nic.Enabled = job != Job.Write && !recording && !blocked && saved == null && a != null;
        profile.Enabled = job != Job.Write && !recording;
        profile.Visible = profileCap.Visible = !matchView;
        watchDesc.Visible = matchView;
        run.Visible = !matchView;
        run.Enabled = job == Job.Survey || idle && can;
        run.Text = job == Job.Survey ? "لغو کاوش" : "سنجش دوباره";
        refresh.Enabled = idle && !recording;
        explorer.Enabled = match.Enabled = job != Job.Write;
        explorer.Fill = !matchView ? O.Lilac : O.Panel2;
        explorer.ColorText = !matchView ? O.Ground : O.Ink;
        explorer.Stroke = !matchView ? O.Lilac : O.Edge;
        match.Fill = matchView ? O.Lilac : O.Panel2;
        match.ColorText = matchView ? O.Ground : O.Ink;
        match.Stroke = matchView ? O.Lilac : O.Edge;
        explorer.Invalidate();
        match.Invalidate();
        grid.Visible = !matchView;
        scope.Visible = matchView;
        section.Text = matchView ? "ردیاب قطعی مسابقه" : "جدول کیفیت DNS";
        legend.Text = matchView ? "× بی‌پاسخ     /     خط نقطه‌چین عمودی: علامت کاربر" :
                      O.N(Catalog.Available(Iran).Length) + " جفت    /    ۵ دور    /    آزمون دو مسیر وب";
        current.Text = a == null ? "رابط آفلاین" : (active ? "فعال: " + saved.Provider.Name + "   ·   " :
                       "DNS فعلی: ") + (a.Effective == null || a.Effective.Length == 0 ? "—" : String.Join("   /   ", a.Effective));
        tips.SetToolTip(current, current.Text);
        action.Text = matchView ? (recording ? "توقف پایش" : "شروع پایش مسابقه") : job == Job.Write ?
                      "در انتظار ویندوز…" : equal ? "همین DNS فعال است" : r != null
                      && r.Eligible ? "آزمون موقت " + r.P.Name : "آزمون موقت DNS";
        action.Enabled = matchView ? idle && can : idle && can && fresh && r != null && r.Eligible && !equal
                         && (saved == null || active);
        restore.Enabled = saved != null && !blocked && job != Job.Write;
        detail.Text = matchView ? "ثبت لحظهٔ قطعی" : "جزئیات ردیف";
        detail.Enabled = matchView ? recording && scope.Frames.Count > 0 : r != null;
        export.Enabled = matchView ? scope.Frames.Count > 0 : findings != null;
        cover.Watching = matchView;
        cover.Done = -1;
        cover.Total = 1;
        if (matchView) {
            cover.Head = "پایش مسابقه";
            cover.Stamp = recording ? "RECORDING    /    " + scope.Frames.Count.ToString("00") : "FIELD NOTES";
            cover.Sub = "مودم، مسیر اینترنت و DNS؛ سه رد مستقل، نه پینگ خود بازی.";
        } else if (job == Job.Survey) {
            cover.Head = last == null
                         || last.Stage < 5 ? "در حال سنجش پایداری" : last.Stage == 5 ? "کنترل پاسخ ساختگی" :
                         "بررسی دسترسی وب";
            cover.Sub = last == null ? "آغاز اندازه‌گیری روی رابط انتخابی…" : last.Stage < 5 ?
                        "دور " + O.N(last.Stage + 1) + " از ۵  ·  " + Target.Host : "مقصد: " + Target.Host +
                        "  ·  بدون تغییر DNS سیستم";
            cover.Done = last == null ? 0 : last.Done;
            cover.Total = last == null ? 1 : last.Total;
            cover.Stamp = "SAMPLING    /    " + (100 * cover.Done / Math.Max(1, cover.Total)).ToString() + "%";
        } else if (findings != null) {
            var best = findings.FirstOrDefault(x => x.Eligible);
            cover.Head = best == null ? "جفت واجد شرایط پیدا نشد" : "پیشنهاد این کاوش: " +
                         best.P.Name;
            cover.Sub = !fresh ?
                        "نتیجه بیش از ۵ دقیقه قدمت دارد؛ برای اعمال، دوباره سنجش کن." : best ==
                        null ? "دلیل رد هر گزینه در جدول و جزئیات آن آمده است." : O.N(findings.Count(
                                    x => x.Eligible)) + " جفت قابل انتخاب برای " + Target.Name +
                        "  ·  تأیید وب، نه تضمین ورود به بازی";
            if (reference != null && reference.PreferCurrent(best)) {
                cover.Head = "پیشنهاد: DNS فعلی را نگه دار";
                if (fresh)cover.Sub = "در این آزمون مزیت کافی برای عوض‌کردن DNS دیده نشد.";
            }
            cover.Stamp = (fresh ? "REPORT" : "STALE") + "   /   " + reportAt.ToLocalTime().ToString("HH:mm:ss");
        } else {
            cover.Head = "رصدخانهٔ اتصال";
            cover.Sub = "پایداری DNS و دسترسی به مقصد را پیش از اتصال بسنج.";
            cover.Stamp = "RESOLVER ATLAS";
        }
        cover.AccessibleDescription = cover.Head + "؛ " + cover.Sub;
        cover.Invalidate();
    }
    async Task Scan()
    {
        if (job != Job.Idle || matchView || recording || Net == null || !Net.Usable || blocked)
            return;
        kickoff.Stop();
        var a = Net;
        var p = Target;
        string stamp = ReportId;
        ClearResults();
        var cts = new CancellationTokenSource();
        searchCts = cts;
        job = Job.Survey;
        Buttons();
        StopPassive(false);
        Say("در حال مقایسه با DNS فعلی، بدون تغییر تنظیم ویندوز.");
        string msg = "";
        bool warn = false;
        try {
            var feedback = new Progress<Tick>(t => {
                if (searchCts == cts && job == Job.Survey && (last == null || t.Done >= last.Done)) {
                    last = t;
                    Buttons();
                }
            });
            bool ir = Iran;
            if (ir && !RegionalAccess.Allowed(a))throw new Issue(Code.RegionalBlocked);
            var baseline = new Reference(a.Effective, ir);
            var list = await Survey.Run(Catalog.Available(ir), p, new RealProbe(a, ir), feedback, Survey.Gap, cts.Token, baseline);
            bool? independent = null;
            if (!baseline.Passed &&
                    list.All(f => f.Good == 0))independent = (await Task.Run(() => NetPath.TcpControl(a.Local, a.Index, cts.Token))).HasValue;
            cts.Token.ThrowIfCancellationRequested();
            var now = Adapters.Find(a.Id);
            if (!now.Usable || CompareKey(a) != CompareKey(now) || stamp != ReportId)
                throw new Issue(Code.NetworkChanged);
            reference = baseline;
            findings = list;
            reportKey = stamp;
            reportAt = DateTime.UtcNow;
            SetRows();
            var winner = list.FirstOrDefault(x => x.Eligible);
            msg = Survey.NoWinner(list, independent);
            if (baseline.PreferCurrent(winner))msg =
                    "DNS فعلی همان مقصد و وب عمومی را باز کرد؛ در این کاوش تغییر لازم نیست.";
            else if (winner != null)msg = "پیشنهاد: " + winner.P.Name +
                                              ". برای امتحان روی ویندوز «آزمون موقت» را بزن؛ بدون تأیید، تنظیم قبلی برمی‌گردد.";
            warn = winner == null && !baseline.Passed;
        } catch (OperationCanceledException) {
            msg = "کاوش لغو شد؛ DNS سیستم تغییر نکرد.";
        } catch (Issue issue) {
            msg = issue.Reason == Code.RegionalBlocked ? Explain(issue.Reason) :
                  "شبکه هنگام کاوش تغییر کرد. نتیجهٔ قبلی کنار گذاشته شد.";
            warn = true;
        } catch {
            msg = "کاوش کامل نشد؛ اتصال را بررسی و دوباره امتحان کن.";
            warn = true;
        } finally {
            searchCts = null;
            cts.Dispose();
            job = Job.Idle;
            if (!IsDisposed) {
                Inventory(false);
                if (!blocked && !matchView)
                    Say(msg, warn);
                Buttons();
                if (closeLater)
                    Close();
                else if (!restoreLater && autoLater) {
                    autoLater = false;
                    QueueScan();
                }
            }
        }
        if (!IsDisposed && restoreLater && !closeLater) {
            restoreLater = false;
            autoLater = false;
            await Restore();
        }
    }
    async Task Apply()
    {
        var r = Pick;
        var a = Net;
        if (job != Job.Idle || matchView || blocked || a == null || r == null || !r.Eligible)
            return;
        if (!Survey.Fresh(reportKey, ReportId, reportAt, DateTime.UtcNow)) {
            Say("نتیجه بیش از ۵ دقیقه قدمت دارد. اول «سنجش دوباره» را بزن.", true);
            Buttons();
            return;
        }
        if (saved != null && !active)
            return;
        if (r.P.Regional && (!Iran || !RegionalAccess.Allowed(a))) {
            Say(Explain(Code.RegionalBlocked), true);
            return;
        }
        try {
            if (CompareKey(Adapters.Find(a.Id)) != CompareKey(a)) {
                Inventory(true);
                Say(Explain(Code.NetworkChanged), true);
                return;
            }
        } catch {
            Say(Explain(Code.NoAdapter), true);
            return;
        }
        string hash;
        try {
            hash = ApplyStamp.For(a, io.Read(a.Id));
        } catch {
            Say(Explain(Code.UnsafeConfig), true);
            return;
        }
        string args = (saved != null ? "--trial-switch " : "--trial ") + a.Id.ToString("D") + " " + r.P.Id + " " +
                      (r.Reverse ? "1" : "0") + " " + a.Local + " " + a.Gateway + " " + Target.Id + " " + (r.P.Regional ? "ir" : "public") + " " + hash +
                      " " + reportAt.Ticks.ToString(CultureInfo.InvariantCulture) + (saved != null ? " " + saved.Token.ToString("D") : "");
        await Write(args, "تنظیم " + r.P.Name + " روی همین کارت شبکه تأیید شد.");
    }
    async Task Restore()
    {
        if (job != Job.Idle || saved == null || blocked)
            return;
        kickoff.Stop();
        if (recording)
            StopRecord("");
        var s = saved;
        bool force = false;
        try {
            var now = io.Read(s.Adapter);
            if (!now.SequenceEqual(s.Target) && !now.SequenceEqual(s.Original)) {
                force = MessageBox.Show(this,
                                        "DNS بیرون از برنامه تغییر کرده است. تنظیم فعلی با نسخهٔ قبل از اولین اتصال جایگزین شود؟",
                                        "بازگردانی", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2,
                                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == DialogResult.Yes;
                if (!force)
                    return;
            }
        } catch {
            Say("خواندن تنظیم فعلی ممکن نشد. همان رابط را وصل کن. پشتیبان حفظ شد.",
                true);
            return;
        }
        await Write("--restore " + s.Token.ToString("D") + " " + (force ? "confirmed" : "normal"),
                    s.Original.Length == 0 ? "بازگردانی انجام شد؛ DNS دوباره خودکار است." :
                    "فهرست دستی اولیه با همان ترتیب بازگردانده شد.");
    }
    async Task Write(string args, string success)
    {
        StopPassive(false);
        job = Job.Write;
        kickoff.Stop();
        Buttons();
        Say(args.StartsWith("--trial", StringComparison.Ordinal) ?
            "پس از مجوز مدیر، پنجرهٔ ۶۰ ثانیه‌ای باز می‌شود. اینترنت را امتحان کن؛ بدون تأیید، تنظیم قبل برمی‌گردد."
            : "درخواست دسترسی مدیر ویندوز را برای بازگردانی تأیید کن.");
        var result = await Program.Elevated(args);
        job = Job.Idle;
        Inventory(false);
        kickoff.Stop();
        if (!blocked)
            Say(result == Code.Ok ? success : Explain(result), result != Code.Ok && result != Code.Unchanged);
        Buttons();
    }
    static string Explain(Code c)
    {
        switch (c) {
        case Code.TrialUndone:
            return "آزمون لغو شد؛ تنظیم درست قبل از این آزمون بازگردانده و بررسی شد.";
        case Code.TrialExpired:
            return "مهلت تأیید تمام شد. تنظیم قبل از آزمون بازگردانده و بررسی شد.";
        case Code.Preflight:
            return "پیش از اعمال، یکی از دو DNS دیگر به آزمون پاسخ نداد. تنظیم ویندوز دست‌نخورده ماند؛ دوباره بسنج.";
        case Code.Stale:
            return "تنظیم کارت تغییر کرده یا مهلت گزارش گذشته است؛ چیزی اعمال نشد. دوباره بسنج.";
        case Code.TrialUnhealthy:
            return "پاسخ هر دو DNS، وب عمومی و مقصد با هم تأیید نشدند. تنظیم قبلی بازگردانده و بررسی شد.";
        case Code.RegionalBlocked:
            return "مسیر دو سرویس خصوصی قابل تأیید نیست یا با رابط محلی هم‌پوشانی دارد. گزینهٔ رادار و ۴۰۳ را خاموش کن.";
        case Code.Unchanged:
            return "این جفت DNS از قبل تنظیم شده بود.";
        case Code.Cancelled:
            return "درخواست مدیر لغو شد؛ تغییری داده نشد.";
        case Code.NeedAdmin:
            return "برای تغییر یا بازگردانی DNS، مجوز مدیر لازم است.";
        case Code.NetworkChanged:
        case Code.NoAdapter:
            return "رابط یا اتصال عوض شده است. همان رابط را وصل و تازه‌سازی کن.";
        case Code.OutsideChange:
            return "DNS خارج از برنامه تغییر کرده است. برای بازگشت از دکمهٔ قطع استفاده کن.";

        case Code.UnsafeConfig:
            return "تنظیم مدیریت‌شده یا ناشناخته است؛ برنامه آن را جایگزین نکرد.";

        case Code.RolledBack:
            return "تغییر موفق نبود؛ تنظیم پیش از این عملیات بازگردانده و بررسی شد.";

        case Code.Recover:
        case Code.SavedStateError:
            return "تأیید بازیابی ممکن نشد. پشتیبان را پاک نکن؛ همان کارت را وصل و بازگردانی را بررسی کن.";

        case Code.HasSession:
        case Code.NoSession:
        case Code.Busy:
            return "نشست تغییر کرده یا عملیات دیگری در جریان است. تازه‌سازی کن.";
        default:
            return "ویندوز تغییر را تأیید نکرد. کارت شبکه، مجوز مدیر و پشتیبان را بررسی کن.";
        }
    }
    string WatchId()
    {
        var a = Net;
        return Key(a) + "/" + (a == null || a.Effective == null ? "" : String.Join(",", a.Effective));
    }
    async Task StartRecord()
    {
        if (job != Job.Idle || !matchView || !Adapters.IsWindows || Net == null || !Net.Usable || recording || sampling)
            return;
        kickoff.Stop();
        StopPassive(false);
        scope.Frames.Clear();
        scope.Invalidate();
        watchCts = new CancellationTokenSource();
        watchKey = WatchId();
        recording = true;
        Buttons();
        DefaultNote();
        watchTimer.Start();
        await Sample();
    }
    void StopRecord(string msg)
    {
        watchTimer.Stop();
        recording = false;
        var c = watchCts;
        watchCts = null;
        if (c != null) {
            c.Cancel();
            if (!sampling)
                c.Dispose();
        }
        if (!String.IsNullOrEmpty(msg))
            Say(msg);
        Buttons();
    }
    async Task Sample()
    {
        if (!recording || sampling || watchCts == null || Net == null)
            return;
        var c = watchCts;
        sampling = true;
        try {
            var a = Adapters.Find(Net.Id);
            if (!a.Usable || Key(a) + "/" + String.Join(",", a.Effective) != watchKey) {
                StopRecord("تنظیم شبکه تغییر کرد؛ پایش متوقف شد. برای اتصال جدید دوباره شروع کن.");
                return;
            }
            var f = await MatchWatch.Read(a, c.Token, Iran);
            c.Token.ThrowIfCancellationRequested();
            if (!recording || c != watchCts || IsDisposed)
                return;
            scope.Frames.Add(f);
            if (scope.Frames.Count > 180)
                scope.Frames.RemoveAt(0);
            scope.Invalidate();
            Say(MatchWatch.Message(MatchWatch.Diagnose(scope.Frames)));
            Buttons();
        } catch (OperationCanceledException) {} catch {
            if (recording && c == watchCts)
                StopRecord("نمونه‌گیری روی این رابط ممکن نشد. اتصال را بررسی کن.");
        } finally {
            sampling = false;
            if (c != watchCts)
                c.Dispose();
            if (!IsDisposed && ready)
                Buttons();
        }
    }
    void Export()
    {
        using (var d = new SaveFileDialog {Filter = "CSV (*.csv)|*.csv", FileName = "Nabz-" + (matchView ? "Match" : "DNS-" + Target.Id) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"}) {
            if (d.ShowDialog(this) != DialogResult.OK)
                return;
            try {
                string data;
                if (matchView)
                    data = MatchWatch.Csv(scope.Frames);
                else if (findings != null) {
                    var b = new
                    StringBuilder("time_local,target,provider,address,samples_ok,total,median_ms,p90_ms,negative,non_public_seen,target_web,target_http,general_host,general_web,general_http,eligible\r\n");
                    Action<string, string, Trace, bool> row = (provider, ip, t, ok) => b.AppendLine(String.Join(",", new[] {reportAt.ToLocalTime().ToString("o"), Target.Host, provider, ip, t.Good.ToString(), "10", Fmt(t.Median), Fmt(t.P90), t.Negative.ToString(), t.NonPublic.ToString(), t.Web.State, t.Web.Status == 0 ? "" : t.Web.Status.ToString(), t.GeneralHost, t.General.State, t.General.Status == 0 ? "" : t.General.Status.ToString(), ok.ToString()}));
                    foreach (var f in findings)foreach (var t in new[] {f.A, f.B})row(f.P.Id, t.Ip, t, f.Eligible);
                    if (reference != null)for (int i = 0; i < reference.Items.Length;
                                                   i++)row("current", "configured-" + (i + 1), reference.Items[i], reference.Passed);
                    data = b.ToString();
                } else
                    return;
                File.WriteAllText(d.FileName, data, new UTF8Encoding(true));
                Say("گزارش روی دستگاه ذخیره شد.");
            } catch {
                Say("ذخیرهٔ گزارش ممکن نشد. پوشهٔ دیگری انتخاب کن.", true);
            }
        }
    }
    static string Fmt(double n)
    {
        return Double.IsInfinity(n) ? "" : n.ToString("0.0", CultureInfo.InvariantCulture);
    }
    void Details()
    {
        var f = Pick;
        if (f == null)
            return;
        var b = new StringBuilder();
        b.AppendLine("مقصد وب: " + Target.Host + Target.Path + "\n");
        foreach (var t in new[] {f.A, f.B}) {
            b.AppendLine(t.Ip + "    /    " + O.N(t.Good) + " پاسخ از ۱۰");
            b.AppendLine("میانه: " + O.Ms(t.Median) + "      P90: " + O.Ms(t.P90) +
                         "      پراکندگی P90−میانه: " + O.Ms(t.Good == 0 ? Double.PositiveInfinity : t.P90 - t.Median));
            b.AppendLine("نام ساختگی: " + (t.Negative == Kind.Nx ? "NXDOMAIN · درست" : t.Negative.ToString()) +
                         "    /    IP غیرعمومی: " + (t.NonPublic ? "دیده شد" : "دیده نشد"));
            b.AppendLine("وب مقصد: " + t.Web.Label + "    /    وب عمومی: " + t.GeneralHost + " " + t.General.Label + "\n");
        }
        b.AppendLine("برای پیشنهاد شدن، هر نشانی به ۹ پاسخ از ۱۰، پاسخ مقصد در دور آخر، کنترل نام ساختگی و وبِ تأییدشده نیاز دارد. این آزمون دسترسی به حساب یا سرور مسابقه را تضمین نمی‌کند.");
        Dialog(f.P.Name + "   /   " + f.Verdict, b.ToString());
    }
    void ShowHelp()
    {
        Dialog("راهنمای مدار",
               "کاوش هنگام باز شدن برنامه و انتخاب رابط یا مقصد آغاز می‌شود. ۱۲ جفت و DNS فعلی در ۵ دور سنجیده می‌شوند؛ دو جفت مخصوص مسیرهای ایران فقط با انتخاب خودت اضافه می‌شوند. فقط اعمال یا بازگردانی به مجوز مدیر نیاز دارد.\n\nآزمون موقت: بعد از اعمال تا ۶۰ ثانیه فرصت داری سایت‌ها را امتحان کنی. نگه‌داشتن فقط پس از دو بررسی سالمِ مقصد و وب عمومی از مسیر ویندوز ممکن است. پایان مهلت یا دو بررسی ناموفق، تنظیم پیش از آزمون را برمی‌گرداند. بعد از تأیید، برگشت با دکمهٔ قطع است.\n\n«قابل انتخاب» یعنی هر دو DNS در همین آزمون پایدار بوده‌اند، برای نام ساختگی آدرس نداده‌اند و صفحهٔ مقصد و دست‌کم یک وب‌سایت عمومی دیگر از IP برگشتی آن‌ها با گواهی معتبر و HTTP 2xx پاسخ داده است؛ نه تضمینِ بدون‌پالایش‌بودن یا اجرای بازی.\n\nپایش مسابقه: هر ۱۰ ثانیه پاسخ گیت‌وی، اتصال TCP به 1.1.1.1:443 و درخواست از دو DNS فعلی را ثبت می‌کند. به بازی وصل نمی‌شود و تنظیمی را تغییر نمی‌دهد. هنگام قطعی، «ثبت لحظهٔ قطعی» را بزن.\n\nدر حالت تأییدشده، تا وقتی پنجره باز است هر ۳۰ ثانیه یک بررسی سبک انجام می‌شود؛ فقط هشدار می‌دهد، DNS را عوض نمی‌کند.\n\nبستن برنامه DNS تأییدشده را برنمی‌گرداند. «قطع و بازگردانی» تنظیم قبل از اولین اتصال را برمی‌گرداند؛ حتی بعد از تعویض جفت یا اجرای دوباره. فقط IPv4 تغییر می‌کند."
               + (saved == null ? "" : "\n\nتنظیم اولیهٔ ذخیره‌شده: " + (saved.Original.Length == 0 ?
                       "خودکار (DHCP)" : String.Join(" / ", saved.Original))));
    }
    void Dialog(string title, string text)
    {
        using (var f = new Form {Text = title, StartPosition = FormStartPosition.CenterParent, BackColor = O.Ground, ForeColor = O.Ink, ClientSize = new Size(D(614), D(415)), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false}) {
            var box = new RichTextBox {ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = O.Panel, ForeColor = O.Ink, Font = O.F(11), RightToLeft = RightToLeft.Yes, DetectUrls = false, Text = text, Dock = DockStyle.Fill};
            var top = new Label {Text = title, Font = O.F(15, true), ForeColor = O.Amber, Height = D(54), Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleRight};
            var done = new KeyButton("بستن", O.Panel2, O.Ink, O.Edge) {
                Dock = DockStyle.Bottom, Height = D(37), DialogResult = DialogResult.OK
            };
            f.Padding = new Padding(D(16));
            f.Controls.Add(box);
            f.Controls.Add(top);
            f.Controls.Add(done);
            f.CancelButton = done;
            f.ShowDialog(this);
        }
    }
    void Leaving(object sender, FormClosingEventArgs e)
    {
        kickoff.Stop();
        if (job == Job.Survey) {
            closeLater = true;
            restoreLater = false;
            e.Cancel = true;
            if (searchCts != null)
                searchCts.Cancel();
        } else if (job == Job.Write) {
            e.Cancel = true;
            Say("ویندوز مشغول تغییر تنظیم است؛ پس از پایان عملیات پنجره را ببند.",
                true);
        } else if (recording)
            StopRecord("");
    }
}
}
