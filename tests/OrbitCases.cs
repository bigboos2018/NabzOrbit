using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nabz
{
internal static partial class Tests
{
    static void Healthy(Trace t, double ms)
    {
        for (int i = 0; i < 10; i++)
            t.Samples[i] = ms;
        t.Negative = Kind.Nx;
        t.Web = WebCheck.Parse("HTTP/1.1 200 OK");
        t.General = WebCheck.Parse("HTTP/1.1 200 OK");
    }
    static Finding Healthy(Provider p, double ms)
    {
        var f = new Finding(p);
        Healthy(f.A, ms);
        Healthy(f.B, ms);
        return f;
    }
    sealed class Stub: IProbe
    {
        internal int Queries, Webs;
        internal bool RefuseWeb, PrivateTarget, Intercept;
        public Reply Dns(string ip, string host, CancellationToken ct)
        {
            Interlocked.Increment(ref Queries);
            ct.ThrowIfCancellationRequested();
            if (host.EndsWith(".invalid"))
                return new Reply {Kind = Intercept ? Kind.A : Kind.Nx};
            bool bad = PrivateTarget && host == Profile.All[0].Host;
            return new Reply {Kind = bad ? Kind.NonPublic : Kind.A, Ms = 7, IPs = new[]{IPAddress.Parse(bad ? "10.0.0.1" : "93.184.216.34")}};
        }
        public WebCheck Web(Profile p, IPAddress ip, CancellationToken ct)
        {
            Interlocked.Increment(ref Webs);
            Yes(NetPath.Public(ip));
            return WebCheck.Parse(RefuseWeb ? "HTTP/1.1 451 Denied" : "HTTP/1.1 200 OK");
        }
    }
    sealed class CountProgress: IProgress<Tick>
    {
        internal int N, Maximum;
        public void Report(Tick t)
        {
            Interlocked.Increment(ref N);
            lock (this)
                Maximum = Math.Max(Maximum, t.Done);
        }
    }
    static Finding[] Run(Stub stub, Provider[] list, CountProgress progress = null)
    {
        return Survey.Run(list, Profile.All[0], stub, progress, 0, CancellationToken.None).GetAwaiter().GetResult();
    }
    static Frame Point(bool gateway, bool internet, bool dns)
    {
        return new Frame {At = DateTimeOffset.Now, Gateway = gateway ? (double ?)5 : null, Internet = internet ? (double ?)20 : null, First = dns ? (double ?)7 : null};
    }
    static void OrbitCases()
    {
        Test("default pairs are public and regional pairs are isolated", delegate {
            Yes(Catalog.Available(false).All(x => x.Pair(false).All(ip => NetPath.Public(IPAddress.Parse(ip)))));
            Eq(2, Catalog.All.Count(x => x.Regional));
            Yes(Profile.All.All(p => p.Path.StartsWith("/") && !p.Host.Contains(":")));
        });
        Test("private, loopback, multicast, documentation and benchmark addresses are not candidate web destinations",
        delegate {
            foreach (string a in new[] {
            "0.0.0.0", "10.2.3.4", "100.127.0.2", "127.0.0.1", "169.254.11.1", "172.16.1.1", "192.0.0.1", "192.0.2.10",
            "192.168.0.1", "198.19.9.1", "198.51.100.2", "203.0.113.1", "239.1.1.1", "255.255.255.255", "::1"
        })
            Yes(!NetPath.Public(IPAddress.Parse(a)));
            Yes(NetPath.Public(IPAddress.Parse("1.1.1.1")));
        });
        Test("private A response marked non-public, not timed as useful", delegate {
            var x = Good();
            x[x.Length - 4] = 10;
            Eq(Kind.NonPublic, Wire.Parse(x, x.Length, Id, Host).Kind);
            Yes(!Answer(x));
        });
        Test("mixed public/private A RRset fails closed", delegate {
            var x = Good().Concat(Record(new byte[] {
                0xc0, 12
            }, 1, 192, 168, 0, 1)).ToArray();
            x[7] = 2;
            Eq(Kind.NonPublic, Wire.Parse(x, x.Length, Id, Host).Kind);
        });
        Test("NXDOMAIN accepted only for matching question and id", delegate {
            var x = Wire.Query("one.invalid", Id);
            x[2] = 0x81;
            x[3] = 0x83;
            Eq(Kind.Nx, Wire.Parse(x, x.Length, Id, "one.invalid").Kind);
            Eq(Kind.Bad, Wire.Parse(x, x.Length, Id, "other.invalid").Kind);
            x[1] ^= 1;
            Eq(Kind.Bad, Wire.Parse(x, x.Length, Id, "one.invalid").Kind);
        });
        Test("P90 nearest rank, median and empty set", delegate {
            double?[] x = {1, 2, 3, 4, 5, 6, 7, 8, 9, 100};
            Eq(9d, Survey.P90(x));
            Eq(5.5d, Survey.Median(x));
            Yes(Double.IsInfinity(Survey.P90(new double?[] {null})));
        });
        Test("each server needs nine of ten plus final target answer", delegate {
            var f = Healthy(P, 8);
            f.A.Samples[2] = null;
            Yes(f.Eligible);
            f.A.Samples[4] = null;
            Yes(!f.Eligible);
            Healthy(f.A, 8);
            f.A.Samples[9] = null;
            Yes(!f.Eligible);
        });
        Test("tail latency chooses among equally stable pairs", delegate {
            var fast = Healthy(P, 12);
            var spiky = Healthy(Catalog.All[1], 3);
            foreach (var t in new[] {spiky.A, spiky.B}) {
                t.Samples[7] = 130;
                t.Samples[6] = 100;
            }
            Eq(fast, Survey.Rank(new[] {spiky, fast})[0]);
            Yes(fast.Median > spiky.Median);
        });
        Test("packet success takes precedence over a small median", delegate {
            var stable = Healthy(P, 100);
            var quick = Healthy(Catalog.All[1], 2);
            quick.A.Samples[0] = null;
            Eq(stable, Survey.Rank(new[] {quick, stable})[0]);
        });
        Test("separate DNS integrity and destination HTTP eligibility", delegate {
            var f = Healthy(P, 4);
            f.B.Web = WebCheck.Parse("HTTP/1.1 403 Forbidden");
            Yes(!f.Eligible);
            Eq(1, f.WebCount);
            Eq(2, f.Clean);
            Eq("وب رد شد", f.Verdict);
        });
        Test("redirects, 401, 403, 405, 429, 451, 500 and malformed status are never success", delegate {
            foreach (string s in new[] {
            "HTTP/1.1 302 Found", "HTTP/1.1 401", "HTTP/1.1 403", "HTTP/1.1 405", "HTTP/1.1 429", "HTTP/1.1 451", "HTTP/1.1 500",
            "200 OK", "HTTP/1.1 20", "HTTP/1.1 abc"
        })
            Yes(!WebCheck.Parse(s).Ok);
            Yes(WebCheck.Parse("HTTP/1.0 204 No Content").Ok);
        });
        Test("same provider's healthy standby becomes first", delegate {
            var f = Healthy(P, 30);
            Healthy(f.B, 10);
            Yes(f.Reverse);
            f.B.Samples[0] = null;
            Yes(!f.Reverse);
        });
        Test("full five-round catalog sweep: bounded steps and host-aware TLS memoization", delegate {
            var p = new CountProgress();
            var s = new Stub();
            var r = Run(s, Catalog.All, p);
            Eq(Catalog.All.Length * 22, s.Queries);
            Eq(Catalog.All.Length * 24, p.N);
            Eq(Catalog.All.Length * 24, p.Maximum);
            Eq(2, s.Webs);
            Yes(r.All(x => x.Eligible));
        });
        Test("negative-control interception prevents HTTPS probe and recommendation", delegate {
            var s = new Stub {
                Intercept = true
            };
            var r = Run(s, new[] {P});
            Eq(0, s.Webs);
            Yes(!r[0].Eligible);
            Eq("پاسخ غیرمنتظره", r[0].Verdict);
        });
        Test("private A on target prevents contacting it", delegate {
            var s = new Stub {
                PrivateTarget = true
            };
            var r = Run(s, new[] {P});
            Eq(0, s.Webs);
            Yes(!r[0].Eligible);
        });
        Test("fast DNS with denied web path never recommended", delegate {
            var s = new Stub {
                RefuseWeb = true
            };
            var f = Run(s, new[] {P})[0];
            Eq(20, f.Good);
            Yes(!f.Eligible);
        });
        Test("pre-cancelled survey performs no network operations", delegate {
            var s = new Stub();
            Throws<OperationCanceledException>(() => Survey.Run(Catalog.All, Profile.All[0], s, null, 0,
                                               new CancellationToken(true)).GetAwaiter().GetResult());
            Eq(0, s.Queries);
        });
        Test("stale reports and target/interface changes cannot authorize apply", delegate {
            var t = DateTime.UtcNow;
            Yes(Survey.Fresh("wifi@steam", "wifi@steam", t, t.AddSeconds(299)));
            Yes(!Survey.Fresh("wifi@steam", "wifi@epic", t, t));
            Yes(!Survey.Fresh("wired@steam", "wifi@steam", t, t));
            Yes(!Survey.Fresh("a", "a", t, t.AddSeconds(301)));
            Yes(!Survey.Fresh("a", "a", t, t.AddSeconds(-1)));
        });
        Test("public guard returns before any TCP connection to a private address", delegate {
            Eq("nonpublic", NetPath.Https(Profile.All[0], IPAddress.Loopback, IPAddress.Loopback, 0,
                                          CancellationToken.None).State);
        });
        Test("v1 journal can be restored by v2", delegate {
            var f = new Fake("192.168.2.3");
            f.Raw = "1|" + B + "|" + A + "|cloudflare|0|active|192.168.2.3";
            f.Map[A] = Catalog.Find("cloudflare").Pair(false);
            Eq(Code.Ok, Change.Restore(f, B, false));
            Same(new[] {"192.168.2.3"}, f.Map[A]);
        });
        Test("several switches retain the original automatic mode and renew token", delegate {
            var f = new Fake();
            FixtureApply(f, A, P, false);
            var first = f.Load().Token;
            Eq(Code.Ok, FixtureSwitch(f, first, A, Catalog.Find("shecan"), false));
            Yes(first != f.Load().Token);
            Eq(Code.Ok, FixtureSwitch(f, f.Load().Token, A, Catalog.Find("begzar"), true));
            Eq(Code.Ok, Change.Restore(f, f.Load().Token, false));
            Same(new string[0], f.Map[A]);
        });
        Test("failed switch rolls back to previous pair and keeps first manual list", delegate {
            var f = new Fake("10.0.0.1", "10.0.0.2");
            FixtureApply(f, A, P, false);
            var s = f.Load();
            f.FailNext = true;
            Eq(Code.RolledBack, FixtureSwitch(f, s.Token, A, Catalog.Find("google"), false));
            Same(s.Target, f.Map[A]);
            Eq(s.Token, f.Load().Token);
            Same(s.Original, f.Load().Original);
        });
        Test("switch rejects externally edited and wrong-interface settings", delegate {
            var f = new Fake();
            FixtureApply(f, A, P, false);
            var s = f.Load();
            Eq(Code.NoSession, FixtureSwitch(f, s.Token, B, Catalog.Find("google"), false));
            f.Map[A] = new[] {"10.0.0.7"};
            Eq(Code.OutsideChange, FixtureSwitch(f, s.Token, A, Catalog.Find("google"), false));
            Same(new[] {"10.0.0.7"}, f.Map[A]);
        });
        Test("first two watch samples cannot classify an outage", delegate {
            Eq("warming", MatchWatch.Diagnose(new[] {
                Point(true, true, false), Point(true, true, false)
            }));
        });
        Test("three missing DNS readings with TCP responding imply only a DNS suspicion", delegate {
            Eq("dns", MatchWatch.Diagnose(Enumerable.Repeat(Point(true, true, false), 3).ToList()));
        });
        Test("gateway silence alone is not a local outage", delegate {
            Eq("sample-ok", MatchWatch.Diagnose(Enumerable.Repeat(Point(false, true, true), 3).ToList()));
        });
        Test("local vs upstream requires correlated probes and gateway baseline", delegate {
            var off = Enumerable.Repeat(Point(false, false, false), 3).ToList();
            Yes(MatchWatch.Diagnose(off) != "local");
            off.Insert(0, Point(true, true, true));
            Eq("local", MatchWatch.Diagnose(off));
            Eq("upstream", MatchWatch.Diagnose(Enumerable.Repeat(Point(true, false, false), 3).ToList()));
        });
        Test("no configured IPv4 resolver is unknown rather than an outage", delegate {
            var f = Point(true, true, false);
            f.Resolvers = 0;
            Eq("no-v4-dns", MatchWatch.Diagnose(Enumerable.Repeat(f, 3).ToList()));
        });
        Test("working standby is not labelled total DNS failure", delegate {
            var f = Point(true, true, false);
            f.Second = 22;
            Eq("secondary", MatchWatch.Diagnose(Enumerable.Repeat(f, 3).ToList()));
        });
        Test("CSV keeps timezone, user marker and missing values distinct from zero", delegate {
            var f = new Frame {
                At = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3.5)), Internet = 0
            };
            f.Mark = f.At.AddSeconds(2);
            string csv = MatchWatch.Csv(new[] {f});
            Yes(csv.Contains("+03:30,,0.0,,,"));
            Yes(csv.Contains("00:00:02"));
        });
    }
    static int LocalTls(int port)
    {
        var ep = new IPEndPoint(IPAddress.Loopback, port);
        Func<string, string, WebCheck> test = (host, path) => NetPath.TlsExchange(host, path, ep, IPAddress.Loopback, 0,
                                              CancellationToken.None);
        Test("trusted loopback TLS: correct SNI/Host and HEAD 200", delegate {
            Eq(200, test("orbit-fixture.test", "/ok").Status);
        });
        Test("trusted loopback TLS: 403 stays denied", delegate {
            var r = test("orbit-fixture.test", "/denied");
            Eq(403, r.Status);
            Yes(!r.Ok);
        });
        Test("trusted loopback TLS: redirect not followed", delegate {
            var r = test("orbit-fixture.test", "/redirect");
            Eq(302, r.Status);
            Yes(!r.Ok);
        });
        Test("trusted issuer but wrong DNS name: reject before HTTP", delegate {
            var r = test("wrong-fixture.test", "/ok");
            Yes(!r.Ok);
            Eq(0, r.Status);
        });
        Console.WriteLine((count - failed) + "/" + count + " TLS transport checks passed.");
        return failed == 0 ? 0 : 1;
    }
}
}
