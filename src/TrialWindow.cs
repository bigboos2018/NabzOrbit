using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Nabz
{
// Independent elevated dialog, no scheduled task, autostart, IPC listener or background service.
internal sealed class TrialWindow : Form
{
    readonly Label time = new Label(), status = new Label(), intro = new Label(), summary = new Label();
    readonly KeyButton keep = new KeyButton("این DNS را نگه دار", O.Amber, O.Ground, O.Amber),
    undo = new KeyButton("همین حالا برگردان", O.Panel2, O.Ink, O.Edge);
    readonly Timer timer = new Timer {Interval = 200};
    readonly Stopwatch life = new Stopwatch();
    readonly CancellationTokenSource cancel = new CancellationTokenSource();
    readonly TrialGate gate = new TrialGate();
    readonly Func<CancellationToken, PathReport> measure;
    readonly int before;
    readonly int span;
    bool checking, ended;
    double next;
    TrialChoice choice = TrialChoice.Undo;
    internal TrialWindow(string name, string target, int beforeMask, Func<CancellationToken, PathReport> measure, int seconds)
    {
        this.before = beforeMask;
        this.measure = measure;
        span = Math.Min(seconds, TrialGate.Seconds);
        Text = "مدار ۲٫۱   /   آزمون محافظت‌شده";
        BackColor = O.Ground;
        ForeColor = O.Ink;
        Font = O.F(10);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(565, 302);
        var head = new Label {Text = "اتصال را امتحان کن", Font = O.F(19, true), ForeColor = O.Ink, BackColor = O.Ground, TextAlign = ContentAlignment.MiddleRight};
        head.SetBounds(150, 10, 394, 48);
        Controls.Add(head);
        time.SetBounds(18, 15, 120, 41);
        time.TextAlign = ContentAlignment.MiddleCenter;
        time.Font = O.F(18, true, true);
        time.ForeColor = O.Amber;
        time.BackColor = O.Panel;
        Controls.Add(time);
        AddText(intro, 62, 47, "«" + name +
                "» موقتاً روی کارت شبکه اعمال شده. اگر تأیید نکنی، تنظیم پیش از این آزمون برمی‌گردد.",
                O.Soft);
        AddText(status, 118, 54, "در حال بررسی بازشدن وب عمومی و " + target +
                " از مسیر DNS واقعی ویندوز…", O.Lilac);
        status.BackColor = O.Panel;
        AddText(summary, 176, 61,
                "حالا یک سایت و لانچر خودت را هم باز کن. دکمهٔ نگه‌داشتن فقط پس از دو بررسی موفق فعال می‌شود.",
                O.Soft);
        keep.SetBounds(289, 249, 254, 39);
        undo.SetBounds(20, 249, 254, 39);
        keep.Enabled = false;
        Controls.Add(keep);
        Controls.Add(undo);
        undo.TabIndex = 0;
        keep.TabIndex = 1;
        CancelButton = undo;
        keep.Click += delegate {
            if (gate.CanKeep(life.Elapsed.TotalSeconds) && life.Elapsed.TotalSeconds < span)End(TrialChoice.Keep);
        };
        undo.Click += delegate {
            End(TrialChoice.Undo);
        };
        Shown += delegate {
            undo.Select();
            life.Start();
            timer.Start();
            Pulse();
        };
        timer.Tick += delegate {
            Pulse();
        };
        FormClosing += delegate {
            ended = true;
            timer.Stop();
            cancel.Cancel();
        };
        FormClosed += delegate {
            timer.Dispose();
            cancel.Dispose();
        };
    }
    void AddText(Label l, int top, int height, string text, Color c)
    {
        l.SetBounds(20, top, 524, height);
        l.Font = O.F(10.4f);
        l.TextAlign = ContentAlignment.MiddleRight;
        l.RightToLeft = RightToLeft.Yes;
        l.ForeColor = c;
        l.Text = text;
        Controls.Add(l);
    }
    void End(TrialChoice result)
    {
        if (ended)return;
        choice = result;
        ended = true;
        Close();
    }
    async void Pulse()
    {
        if (ended)return;
        double now = life.Elapsed.TotalSeconds;
        time.Text = Math.Max(0, (int)Math.Ceiling(span - now)) + " s";
        if (now >= span) {
            End(TrialChoice.Expired);
            return;
        }
        keep.Enabled = gate.CanKeep(now);
        if (checking || now < next)return;
        checking = true;
        var ct = cancel.Token;
        try {
            var result = await Task.Run(() => measure(ct));
            if (ended || IsDisposed || ct.IsCancellationRequested)return;
            now = life.Elapsed.TotalSeconds;
            if (now >= span) {
                End(TrialChoice.Expired);
                return;
            }
            bool good = result.Healthy(before);
            gate.Observe(good, now);
            keep.Enabled = gate.CanKeep(now);
            string general = result.GeneralMask == 0 ? "بی‌پاسخ" : (result.GeneralMask & before) != before ?
                             "افت نسبت به قبل" : "پاسخ‌گو";
            status.Text = "وب عمومی: " + general + "   /   مقصد: " + result.TargetLabel + "   /   هر دو DNS: " +
                          (result.Pair ? "پاسخ‌گو" : "تأیید نشد") + "\nبررسی موفق متوالی: " + O.N(gate.Good);
            if (gate.MustUndo(now))End(TrialChoice.Unhealthy);
        } catch (OperationCanceledException) {} catch {
            if (!ended)End(TrialChoice.Unhealthy);
        } finally {
            checking = false;
            next = life.Elapsed.TotalSeconds + 4;
        }
    }
    internal TrialChoice Ask()
    {
        ShowDialog();
        return choice;
    }
}
}
