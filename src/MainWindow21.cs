using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using UiTimer = System.Windows.Forms.Timer;

namespace Nabz
{
internal sealed partial class MainWindow
{
    readonly UiTimer passiveTimer = new UiTimer {Interval = 30000};
    readonly List<Frame> passiveFrames = new List<Frame>();
    CancellationTokenSource passiveCts;
    string passiveKey = "", lastAlert = "";
    void ClearRegion()
    {
        bool prev = locking;
        locking = true;
        consentKey = "";
        iran.Checked = false;
        locking = prev;
    }
    bool RegionOptIn()
    {
        if (!RegionalAccess.Allowed(Net)) {
            Say(Explain(Code.RegionalBlocked), true);
            return false;
        }
        return MessageBox.Show(this,
                               "رادار و ۴۰۳ از چهار نشانی خصوصی 10.202.10.x استفاده می‌کنند. روی بعضی اپراتورها این نشانی‌ها مسیر دارند؛ ممکن است در شبکه‌های دیگر به یک سرویس داخلی اشاره کنند.\n\nفقط اگر روی اینترنت شخصی خودت در ایران هستی و VPN یا شبکهٔ سازمانی نداری، این دو جفت را برای همین اتصال به آزمایش اضافه کن. ادامه می‌دهی؟",
                               "۲ جفت اختیاری", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                               MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading) == DialogResult.Yes;
    }
    void ReferenceDetails()
    {
        var r = reference;
        if (r == null)return;
        var b = new StringBuilder();
        if (r.Items.Length == 0)
            b.AppendLine("نشانی IPv4 قابل آزمایش گزارش نشد؛ نمی‌توان گفت DNS فعلی بهتر یا بدتر است.");
        if (r.Partial)
            b.AppendLine("بخشی از DNS فعلی از جفت‌های خصوصی و غیرفعال بود و آزمایش نشد. مقایسهٔ کامل در دسترس نیست.\n");
        foreach (var t in r.Items) {
            b.AppendLine(t.Ip + "   /   " + O.N(t.Good) + " پاسخ از ۱۰");
            b.AppendLine("P90: " + O.Ms(t.P90) + "    /    میانه: " + O.Ms(t.Median));
            b.AppendLine("نام ساختگی: " + (t.Negative == Kind.Nx ? "NXDOMAIN · درست" : t.Negative.ToString()));
            b.AppendLine("وب مقصد: " + t.Web.Label + "    /    وب عمومی: " + t.GeneralHost + " " + t.General.Label + "\n");
        }
        b.AppendLine("حداکثر دو DNS فعلی همین کارت با همان نمونه‌ها، نام‌ها و معیارها سنجیده شده‌اند. اگر فقط یکی تنظیم است، یک بار حساب می‌شود. این ردیف فقط مرجع است و به تنظیم دستی تبدیل نمی‌شود.\n\nاگر پایداری و دسترسی برابر باشند، جابه‌جایی فقط زمانی پیشنهاد می‌شود که P90 دست‌کم ۱۵ میلی‌ثانیه و ۲۰٪ بهتر باشد.");
        Dialog("خط مبنا   /   DNS فعلی", b.ToString());
    }
    void StartPassive()
    {
        passiveTimer.Tick += async delegate {
            if (!ready || IsDisposed || job != Job.Idle || recording || sampling || !active || blocked || saved == null || Net == null ||
                    passiveCts != null)return;
            var s = saved;
            var key = s.Token.ToString() + "/" + WatchId();
            if (key != passiveKey) {
                passiveFrames.Clear();
                lastAlert = "";
                passiveKey = key;
            }
            var c = new CancellationTokenSource();
            passiveCts = c;
            try {
                var a = Adapters.Find(s.Adapter);
                if (!a.Usable || !a.Effective.SequenceEqual(s.Target))return;
                var f = await MatchWatch.Read(a, c.Token, Iran);
                c.Token.ThrowIfCancellationRequested();
                if (!ready || IsDisposed || job != Job.Idle || recording || saved == null || key != saved.Token.ToString() + "/" + WatchId())return;
                passiveFrames.Add(f);
                if (passiveFrames.Count > 8)passiveFrames.RemoveAt(0);
                var cause = MatchWatch.Diagnose(passiveFrames);
                bool warn = cause == "dns" || cause == "secondary" || cause == "local" || cause == "upstream";
                if (warn && cause != lastAlert) {
                    Say(cause == "dns" ?
                        "سه بار مسیر اینترنتی پاسخ داد ولی DNS فعلی پاسخ معتبر نداد. اگر سایت‌ها باز نمی‌شوند، «قطع و بازگردانی» را بزن."
                        : MatchWatch.Message(cause), true);
                    lastAlert = cause;
                } else if (!warn)lastAlert = "";
            } catch (OperationCanceledException) {} catch {/* a missing or disabled route is not a DNS diagnosis */} finally {
                if (passiveCts == c)passiveCts = null;
                c.Dispose();
            }
        };
        passiveTimer.Start();
    }
    void StopPassive(bool closing)
    {
        if (closing) {
            passiveTimer.Stop();
            passiveTimer.Dispose();
        }
        if (passiveCts != null)passiveCts.Cancel();
        passiveFrames.Clear();
        lastAlert = "";
        passiveKey = "";
    }
}
}
