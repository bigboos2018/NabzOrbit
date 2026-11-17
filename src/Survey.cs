using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nabz
{
internal interface IProbe
{
    Reply Dns(string ip, string host, CancellationToken ct);
    WebCheck Web(Profile profile, IPAddress ip, CancellationToken ct);
}
internal sealed class RealProbe: IProbe
{
    readonly Adapter a;
    readonly bool regional;
    internal RealProbe(Adapter a, bool regional = false)
    {
        this.a = a;
        this.regional = regional;
    }
    public Reply Dns(string ip, string host, CancellationToken ct)
    {
        if (Catalog.IsRegionalAddress(ip) && (!regional || !RegionalAccess.Allowed(a)))return new Reply {Kind = Kind.NoReply};
        return Wire.Ask(IPAddress.Parse(ip), 53, a.Local, a.Index, host, 1250, ct);
    }
    public WebCheck Web(Profile p, IPAddress ip, CancellationToken ct)
    {
        return NetPath.Https(p, ip, a.Local, a.Index, ct);
    }
}
internal sealed class Trace
{
    internal readonly string Ip;
    internal readonly double?[] Samples = new double?[10];
    internal bool NonPublic;
    internal Kind Negative = Kind.NoReply;
    internal IPAddress[] TargetIps = new IPAddress[0];
    internal WebCheck Web = WebCheck.Of("skipped"), General = WebCheck.Of("skipped");
    internal string GeneralHost = "";
    internal readonly Dictionary<string, IPAddress[]> ControlIps = new Dictionary<string, IPAddress[]>
    (StringComparer.OrdinalIgnoreCase);
    internal Trace(string ip)
    {
        Ip = ip;
    }
    internal int Good
    {
        get {
            return Samples.Count(x => x.HasValue);
        }
    }
    internal double P90
    {
        get {
            return Survey.P90(Samples);
        }
    }
    internal double Median
    {
        get {
            return Survey.Median(Samples);
        }
    }
    internal bool Stable
    {
        get {
            return Good >= 9 && Samples[9].HasValue;
        }
    }
    internal bool CleanTest
    {
        get {
            return !NonPublic && Negative == Kind.Nx;
        }
    }
    internal bool Eligible
    {
        get {
            return Stable && CleanTest && Web.Ok && General.Ok;
        }
    }
    internal void Record(int i, Reply r, bool target)
    {
        if (r.Kind == Kind.NonPublic)
            NonPublic = true;
        if (r.Kind == Kind.A) {
            Samples[i] = r.Ms;
            if (target)
                TargetIps = r.IPs;
        }
    }
}
internal sealed class Finding
{
    internal readonly Provider P;
    internal readonly Trace A, B;
    internal Finding(Provider p)
    {
        P = p;
        A = new Trace(p.A);
        B = new Trace(p.B);
    }
    internal bool Eligible
    {
        get {
            return A.Eligible && B.Eligible;
        }
    } internal int Good
    {
        get {
            return A.Good + B.Good;
        }
    } internal int Clean
    {
        get {
            return (A.CleanTest ? 1 : 0) + (B.CleanTest ? 1 : 0);
        }
    } internal int WebCount
    {
        get {
            return (A.Web.Ok ? 1 : 0) + (B.Web.Ok ? 1 : 0);
        }
    }
    internal double P90
    {
        get {
            return Survey.P90(A.Samples.Concat(B.Samples));
        }
    } internal double Median
    {
        get {
            return Survey.Median(A.Samples.Concat(B.Samples));
        }
    } internal double Spread
    {
        get {
            return Good == 0 ? Double.PositiveInfinity : Math.Max(0, P90 - Median);
        }
    }
    internal bool Reverse
    {
        get {
            return B.Good > A.Good || (B.Good == A.Good && (B.P90 < A.P90 || (B.P90 == A.P90 && B.Median < A.Median)));
        }
    }
    internal string Verdict
    {
        get {
            if (Eligible)
                return "قابل انتخاب";
            if (A.NonPublic || B.NonPublic || A.Negative == Kind.A || B.Negative == Kind.A || A.Negative == Kind.NonPublic
                    || B.Negative == Kind.NonPublic)
                return "پاسخ غیرمنتظره";
            if (Good == 0)
                return "بی‌پاسخ";
            if (!A.Stable || !B.Stable)
                return "ناپایدار";
            if (Clean != 2)
                return "کنترل ناموفق";
            if (A.Web.Ok && B.Web.Ok && (!A.General.Ok || !B.General.Ok))return "وب عمومی ناموفق";
            if (A.Web.Status == 403 || A.Web.Status == 451 || B.Web.Status == 403 || B.Web.Status == 451)
                return "وب رد شد";
            return "وب تأیید نشد";
        }
    }
}
internal sealed class Tick
{
    internal int Done, Total, Stage;
}
internal static class Survey
{
    internal const int Rounds = 5, Gap = 2100;
    static readonly string[] Controls = {"example.com", "ietf.org", "www.iana.org"};
    internal static double Median(IEnumerable < double ? > v)
    {
        var a = v.Where(x => x.HasValue).Select(x => x.Value).OrderBy(x => x).ToArray();
        return a.Length == 0 ? Double.PositiveInfinity : (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2;
    }
    internal static double P90(IEnumerable < double ? > v)
    {
        var a = v.Where(x => x.HasValue).Select(x => x.Value).OrderBy(x => x).ToArray();
        return a.Length == 0 ? Double.PositiveInfinity : a[(int)Math.Ceiling(.90 * a.Length) - 1];
    }
    internal static Finding[] Rank(IEnumerable<Finding> r)
    {
        return r.OrderByDescending(x => x.Eligible).ThenByDescending(x => x.WebCount).ThenByDescending(x => Math.Min(x.A.Good,
                x.B.Good)).ThenByDescending(x => x.Good).ThenBy(x => x.P90).ThenBy(x => x.Median).ThenBy(x => x.P.Id,
                        StringComparer.Ordinal).ToArray();
    }
    static async Task Each(Trace[] all, int parallel, Action<Trace> action, CancellationToken ct)
    {
        using (var gate = new SemaphoreSlim(parallel)) {
            var tasks = all.Select(async t => {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try {
                    await Task.Run(() => {
                        ct.ThrowIfCancellationRequested();
                        action(t);
                    }, ct).ConfigureAwait(false);
                } finally {
                    gate.Release();
                }
            }).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
    internal static async Task<Finding[]> Run(Provider[] list, Profile profile, IProbe probe, IProgress<Tick> progress,
            int gap, CancellationToken ct, Reference reference = null)
    {
        var findings = list.Select(p => new Finding(p)).ToArray();
        var pool = findings.SelectMany(f => new[] {f.A, f.B}).ToDictionary(t => t.Ip, StringComparer.Ordinal);
        if (reference != null)reference.BindTo(pool);
        var traces = pool.Values.ToArray();
        int total = traces.Length * 12, done = 0;
        Action<int> step = i => {
            int n = Interlocked.Increment(ref done);
            if (progress != null)
                progress.Report(new Tick {Done = n, Total = total, Stage = i});
        };
        for (int r = 0; r < Rounds; r++) {
            int round = r;
            await Each(traces, 5, t => {
                string cname = Controls[round % Controls.Length];
                var reply = probe.Dns(t.Ip, cname, ct);
                t.Record(round * 2, reply, false);
                if (reply.Kind == Kind.A)t.ControlIps[cname] = reply.IPs;
                step(round);
                t.Record(round * 2 + 1, probe.Dns(t.Ip, profile.Host, ct), true);
                step(round);
            }, ct).ConfigureAwait(false);
            if (r < Rounds - 1 && gap > 0)
                await Task.Delay(gap, ct).ConfigureAwait(false);
        }
        string invalid = "nbz-" + Guid.NewGuid().ToString("N") + ".invalid";
        await Each(traces, 5, t => {
            t.Negative = probe.Dns(t.Ip, invalid, ct).Kind;
            step(5);
        }, ct).ConfigureAwait(false);
        // The same destination returned by several resolvers is checked only once per scan.
        var cache = new ConcurrentDictionary<string, Lazy<WebCheck>>(StringComparer.Ordinal);
        Func<Profile, IPAddress[], WebCheck> web = (site, ips) => {
            WebCheck w = WebCheck.Of("skipped");
            foreach (var ip in ips.Where(NetPath.Public).Distinct().Take(2)) {
                ct.ThrowIfCancellationRequested();
                var address = ip;
                // A shared CDN IP does not imply shared SNI, path or access policy.
                string key = site.Host + site.Path + "|" + ip;
                w = cache.GetOrAdd(key, k => new Lazy<WebCheck>(() => probe.Web(site, address, ct), true)).Value;
                if (w.Ok)break;
            }
            return w;
        };
        await Each(traces, 3, t => {
            if (t.Stable && t.CleanTest) {
                t.Web = web(profile, t.TargetIps);
                foreach (var site in Profile.GeneralFor(profile)) {
                    IPAddress[] ips;
                    if (!t.ControlIps.TryGetValue(site.Host, out ips))continue;
                    t.General = web(site, ips);
                    t.GeneralHost = site.Host;
                    if (t.General.Ok)break;
                }
            }
            step(6);
        }, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return Rank(findings);
    }
    internal static string NoWinner(Finding[] findings, bool? independentTcp)
    {
        var t = findings.SelectMany(f => new[] {f.A, f.B}).ToArray();
        if (t.Any(x => x.General.Ok &&
                  !x.Web.Ok))return
                          "روی بعضی مسیرها وب عمومی باز شد، اما صفحهٔ مقصد تأیید نشد. این نشانهٔ قطع کل اینترنت نیست؛ تنظیم ویندوز تغییر نکرد.";
        if (t.Any(x => x.Web.Ok &&
                  !x.General.Ok))return
                          "صفحهٔ مقصد روی بعضی مسیرها باز شد، اما وب عمومی تأیید نشد. برای حفظ مرور عادی، DNS تغییر نکرد.";
        if (t.All(x => x.Good == 0))return independentTcp == true ?
                                               "مسیر TCP بدون DNS پاسخ داد، ولی هیچ‌کدام از نشانی‌های فهرست پاسخ DNS قابل‌استفاده ندادند. تنظیم عوض نشد؛ در «پایش مسابقه» دوباره بررسی کن."
                                               : "نشانی‌های فهرست پاسخ DNS قابل‌استفاده ندادند و اتصال به مقصد کنترل هم تأیید نشد. این علت قطعی را مشخص نمی‌کند؛ وای‌فای، مودم و مسیر اپراتور را هم بررسی کن.";
        return "هیچ جفتی هم‌زمان از آزمون پایداری، پاسخ DNS، وب مقصد و وب عمومی عبور نکرد. علت هر ردیف را در جزئیات ببین؛ چیزی تغییر نکرد.";
    }
    internal static bool Fresh(string savedKey, string nowKey, DateTime at, DateTime now)
    {
        return savedKey == nowKey && now >= at && now - at <= TimeSpan.FromMinutes(5);
    }
}
}
