using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nabz
{
internal static partial class Tests
{
    // Bypass the interactive prompt only in this test executable, using a simulated store.
    static Code FixtureApply(Fake f, Guid id, Provider p, bool rev)
    {
        return GuardedTrial.Run(f, id, p, rev, null, () => TrialChoice.Keep);
    }
    static Code FixtureSwitch(Fake f, Guid token, Guid id, Provider p, bool rev)
    {
        return GuardedTrial.Run(f, id, p, rev, token, () => TrialChoice.Keep);
    }
    static Reference Baseline(params Trace[] items)
    {
        var r = new Reference(items.Select(t => t.Ip), true);
        r.Items = items;
        return r;
    }
    sealed class SplitProbe: IProbe
    {
        internal bool GeneralDown, ExampleDown;
        internal string DropIp = "";
        internal int DnsCount, WebCount;
        public Reply Dns(string ip, string host, CancellationToken ct)
        {
            Interlocked.Increment(ref DnsCount);
            ct.ThrowIfCancellationRequested();
            return ip == DropIp ? new Reply {Kind = Kind.NoReply}: new Reply {Kind = host.EndsWith(".invalid") ? Kind.Nx : Kind.A, Ms = 18, IPs = new[]{IPAddress.Parse("93.184.216.34")}};
        }
        public WebCheck Web(Profile p, IPAddress ip, CancellationToken ct)
        {
            Interlocked.Increment(ref WebCount);
            return WebCheck.Parse((GeneralDown && p.Id != "steam" || ExampleDown &&
                                   p.Host == "example.com") ? "HTTP/1.1 403 Forbidden" : "HTTP/1.1 200 OK");
        }
    }
    static Reply TruncatedLoop(bool badTcp)
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        try {
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) {
                udp.Client.ReceiveTimeout = 2200;
                var server = Task.Run(delegate {
                    var peer = new IPEndPoint(IPAddress.Any, 0);
                    var q = udp.Receive(ref peer);
                    byte[] trunc = (byte[])q.Clone();
                    trunc[2] = 0x83;
                    trunc[3] = 0x80;
                    udp.Send(trunc, trunc.Length, peer);
                    using (var c = tcp.AcceptTcpClient())using (var ns = c.GetStream()) {
                            ns.ReadTimeout = 1500;
                            var data = new BinaryReader(ns);
                            int n = (data.ReadByte() << 8) | data.ReadByte();
                            var rq = data.ReadBytes(n);
                            Yes(rq.SequenceEqual(q));
                            var response = Ok(q);
                            if (badTcp)response[1] ^= 8;
                            ns.WriteByte((byte)(response.Length >> 8));
                            ns.WriteByte((byte)response.Length);
                            ns.Write(response, 0, response.Length);
                        }
                });
                try {
                    return Wire.Ask(IPAddress.Loopback, port, IPAddress.Loopback, 0, Host, 1800, CancellationToken.None);
                } finally {
                    tcp.Stop();
                    Yes(server.Wait(2500));
                }
            }
        } finally {
            tcp.Stop();
        }
    }
    // Build an in-process DNS_RECORDW list. No host resolver or live Windows NIC is touched.
    sealed class NativeList: IDisposable
    {
        internal IntPtr Head;
        readonly List<IntPtr> alloc = new List<IntPtr>();
        IntPtr Name(string value)
        {
            var n = Marshal.StringToHGlobalUni(value);
            alloc.Add(n);
            return n;
        }
        internal void Add(string owner, string value, int section, bool cname)
        {
            int size = Marshal.SizeOf(typeof(SystemPath.Header));
            var p = Marshal.AllocHGlobal(size + IntPtr.Size);
            alloc.Add(p);
            var h = new SystemPath.Header {Next = Head, Name = Name(owner), Type = (ushort)(cname ? 5 : 1), Length = (ushort)(cname ? IntPtr.Size : 4), Flags = (uint)section};
            Marshal.StructureToPtr(h, p, false);
            if (cname)Marshal.WriteIntPtr(IntPtr.Add(p, size), Name(value));
            else Marshal.Copy(IPAddress.Parse(value).GetAddressBytes(), 0, IntPtr.Add(p, size), 4);
            Head = p;
        }
        public void Dispose()
        {
            foreach (var p in alloc)Marshal.FreeHGlobal(p);
        }
    }
    static void GuardCases()
    {
        Test("exact regional allowlist cannot admit an adjacent private address or web target", delegate {
            Yes(Catalog.ApprovedAddresses("radar", "10.202.10.10", "10.202.10.11"));
            Yes(!Catalog.ApprovedAddresses("ir403", "10.202.10.202", "10.202.10.103"));
            Yes(!Catalog.ApprovedAddresses("other", "10.202.10.10", "10.202.10.11"));
            Yes(Catalog.Available(false).All(p => !p.Regional));
            Yes(Catalog.Available(true).Length == 14);
            Eq(Kind.NoReply, new RealProbe(null, false).Dns("10.202.10.202", Host, CancellationToken.None).Kind);
            Eq("nonpublic", NetPath.Https(Profile.All[0], IPAddress.Parse("10.202.10.10"), IPAddress.Loopback, 0,
                                          CancellationToken.None).State);
        });
        Test("regional subnet collision and unknown mask fail closed", delegate {
            var ip = IPAddress.Parse("10.202.10.10");
            Yes(RegionalAccess.SameSubnet(ip, IPAddress.Parse("10.2.3.4"), IPAddress.Parse("255.0.0.0")));
            Yes(!RegionalAccess.SameSubnet(ip, IPAddress.Parse("192.168.8.4"), IPAddress.Parse("255.255.255.0")));
            Yes(RegionalAccess.SameSubnet(ip, IPAddress.Loopback, null));
            Yes(!RegionalAccess.Allowed(null));
        });
        Test("baseline: cap at two distinct configured IPv4s; do not newly probe private catalog when opted out", delegate {
            var r = new Reference(new[] {
                "::1", "192.168.8.1", "192.168.8.1", "10.202.10.11", "8.8.8.8"
            }, false);
            Same(new[] {"192.168.8.1"}, r.Addresses);
            Same(new[] {"10.202.10.11"}, new Reference(new[] {"10.202.10.11"}, true).Addresses);
        });
        Test("baseline and catalog share identical observations instead of double counting network traffic", delegate {
            var r = new Reference(new[] {
                P.A, "192.168.1.1"
            }, false);
            var probe = new SplitProbe();
            var list = Survey.Run(new[] {P}, Profile.All[0], probe, null, 0, CancellationToken.None, r).GetAwaiter().GetResult();
            Eq(33, probe.DnsCount);
            Eq(2, probe.WebCount);
            Eq(list[0].A, r.Items[0]);
            Yes(r.Passed);
            Eq(20, r.Good);
        });
        Test("web destination and ordinary web have distinct cache keys even on the same IP", delegate {
            var r = Survey.Run(new[] {
                P
            }, Profile.All[0], new SplitProbe {GeneralDown = true}, null, 0, CancellationToken.None).Result[0];
            Eq(2, r.WebCount);
            Yes(!r.Eligible);
            Eq("وب عمومی ناموفق", r.Verdict);
        });
        Test("one denied ordinary site falls back to a separately named control", delegate {
            var r = Survey.Run(new[] {
                P
            }, Profile.All[0], new SplitProbe {ExampleDown = true}, null, 0, CancellationToken.None).Result[0];
            Yes(r.Eligible);
            Eq("www.iana.org", r.A.GeneralHost);
            Yes(Profile.GeneralFor(Profile.FromId("general")).All(x => x.Host != "www.iana.org"));
        });
        Test("retain healthy current DNS for small absolute or relative P90 gains (one-address baseline)", delegate {
            var t = new Trace("192.168.1.1");
            Healthy(t, 42);
            var baseline = Baseline(t);
            Yes(baseline.PreferCurrent(Healthy(P, 30)));
            Healthy(t, 200);
            Yes(baseline.PreferCurrent(Healthy(P, 180)));
            Yes(!baseline.PreferCurrent(Healthy(P, 140)));
            Yes(baseline.PreferCurrent(null));
        });
        Test("recommendation: packet success beats median, matching pair never needs reapplication", delegate {
            var t = new Trace("192.168.1.1");
            Healthy(t, 10);
            var r = Baseline(t);
            var other = Healthy(P, 200);
            t.Samples[0] = null;
            Yes(!r.PreferCurrent(other));
            t.Samples[0] = 8;
            other.B.Samples[0] = null;
            Yes(r.PreferCurrent(other));
            Yes(!new Reference(null, false).PreferCurrent(other));
            Yes(Baseline(other.B, other.A).PreferCurrent(other));
        });
        Test("matched UDP truncation performs real DNS-over-TCP retry on same loopback endpoint", delegate {
            Eq(Kind.A, TruncatedLoop(false).Kind);
        });
        Test("TCP fallback still rejects mismatched response identity", delegate {
            Eq(Kind.Bad, TruncatedLoop(true).Kind);
        });
        Test("foreign question in a truncated packet does not trigger TCP retry", delegate {
            var q = Wire.Query(Host, Id);
            q[2] = 0x83;
            Eq(Kind.Bad, Wire.Parse(q, q.Length, Id, "other.test").Kind);
            Eq(Kind.Bad, Wire.Parse(q, q.Length, 123, Host).Kind);
        });
        Test("worker context rejects changed interface, DNS order, DHCP/manual mode, expired and future timestamps", delegate {
            var a = new Adapter {
                Id = A, Index = 20, Local = IPAddress.Parse("192.168.1.12"), Gateway = "192.168.1.1", Effective = P.Pair(false), Usable = true
            };
            var dhcp = new string[0];
            var t = DateTime.UtcNow;
            string ticks = t.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var hash = ApplyStamp.For(a, dhcp);
            Yes(ApplyStamp.Valid(hash, ticks, a, dhcp, t));
            Yes(!ApplyStamp.Valid(hash, ticks, a, P.Pair(false), t));
            Yes(!ApplyStamp.Valid(hash, ticks, a, dhcp, t.AddSeconds(301)));
            Yes(!ApplyStamp.Valid(hash, ticks, a, dhcp, t.AddSeconds(-1)));
            a.Effective = P.Pair(true);
            Yes(!ApplyStamp.Valid(hash, ticks, a, dhcp, t));
            a.Effective = P.Pair(false);
            a.Index++;
            Yes(!ApplyStamp.Valid(hash, ticks, a, dhcp, t));
        });
        Test("preflight and trial's direct check require both configured resolvers to answer", delegate {
            Yes(SystemPath.PairReplies(P, Profile.All[0], new SplitProbe(), CancellationToken.None));
            Yes(!SystemPath.PairReplies(P, Profile.All[0], new SplitProbe {DropIp = P.B}, CancellationToken.None));
        });
        Test("Windows-record layout follows native pointer sizes; only related answer A/CNAME accepted", delegate {
            Eq(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf(typeof(SystemPath.Header)));
            using (var x = new NativeList()) {
                x.Add("cdn.test.", "93.184.216.34", 1, false);
                x.Add("WWW.test.", "cdn.test.", 1, true);
                x.Add("www.test", "10.0.0.5", 3, false);
                x.Add("other.test", "10.0.0.6", 1, false);
                Same(new[] {"93.184.216.34"}, SystemPath.Addresses(x.Head, "www.test").Select(ip => ip.ToString()).ToArray());
            }
        });
        Test("native DNS mixed RRset, authority-only and CNAME loop cannot authorize a web connection", delegate {
            using (var x = new NativeList()) {
                x.Add(Host, "93.184.216.34", 1, false);
                x.Add(Host, "127.0.0.1", 1, false);
                Eq(0, SystemPath.Addresses(x.Head, Host).Length);
            }
            using (var x = new NativeList()) {
                x.Add(Host, "93.184.216.34", 2, false);
                Eq(0, SystemPath.Addresses(x.Head, Host).Length);
            }
            using (var x = new NativeList()) {
                x.Add(Host, "another.test", 1, true);
                x.Add("another.test", Host, 1, true);
                Eq(0, SystemPath.Addresses(x.Head, Host).Length);
            }
        });
        Test("post-change health needs target, both DNS and every pre-working ordinary site", delegate {
            var r = new PathReport {
                Pair = true, Target = true, GeneralMask = 1
            };
            Yes(r.Healthy(0));
            Yes(r.Healthy(1));
            Yes(!r.Healthy(3));
            r.GeneralMask = 3;
            Yes(r.Healthy(3));
            r.Target = false;
            Yes(!r.Healthy(3));
            r.Target = true;
            r.Pair = false;
            Yes(!r.Healthy(3));
            r.Pair = true;
            r.GeneralMask = 0;
            Yes(!r.Healthy(0));
        });
        Test("confirmation gate needs two consecutive fresh successes; two failures or expiry veto keep", delegate {
            var g = new TrialGate();
            Yes(!g.CanKeep(0));
            g.Observe(true, 10);
            Yes(!g.CanKeep(10));
            g.Observe(true, 14);
            Yes(g.CanKeep(15));
            Yes(!g.CanKeep(29));
            g.Observe(false, 30);
            Yes(!g.CanKeep(30));
            Yes(!g.MustUndo(30));
            g.Observe(false, 34);
            Yes(g.MustUndo(34));
            g.Observe(true, 45);
            g.Observe(true, 51);
            Yes(g.CanKeep(52));
            Yes(!g.CanKeep(60));
            Yes(g.MustUndo(60));
        });
        Test("failed scan separates DNS/route clues from a denied target and preserves uncertainty", delegate {
            var f = new Finding(P);
            Yes(Survey.NoWinner(new[] {f}, true).Contains("TCP"));
            Yes(Survey.NoWinner(new[] {f}, false).Contains("مشخص نمی‌کند"));
            f.A.General = WebCheck.Parse("HTTP/1.1 200 OK");
            Yes(Survey.NoWinner(new[] {f}, null).Contains("قطع کل اینترنت نیست"));
        });
        Test("a change between final preflight and capture cannot be staged", delegate {
            var f = new Fake("192.168.8.99");
            Eq(Code.Stale, GuardedTrial.Run(f, A, P, false, null, () => TrialChoice.Keep, new[] {"192.168.8.1"}));
            Eq(0, f.Writes);
            Yes(f.Raw == null);
        });
        Test("a partially omitted baseline cannot be labelled wholly healthy", delegate {
            var r = new Reference(new[] {
                "192.168.1.1", "10.202.10.11"
            }, false);
            var t = new Trace("192.168.1.1");
            Healthy(t, 9);
            r.Items = new[] {t};
            Yes(r.Partial);
            Yes(!r.Passed);
            Yes(!r.PreferCurrent(null));
        });
        Test("timeout returns initial DHCP without retaining a stale lease", delegate {
            var f = new Fake();
            var c = GuardedTrial.Run(f, A, P, false, null, () => {
                Eq("pending", f.Load().Phase);
                Same(P.Pair(false), f.Read(A));
                return TrialChoice.Expired;
            });
            Eq(Code.TrialExpired, c);
            Same(new string[0], f.Read(A));
            Yes(f.Raw == null);
            Eq(2, f.Writes);
        });
        Test("failed health restores the whole ordered pre-trial manual list", delegate {
            var before = new[] {
                "192.168.0.2", "192.168.0.9", "10.0.0.1"
            };
            var f = new Fake(before);
            Eq(Code.TrialUnhealthy, GuardedTrial.Run(f, A, P, true, null, () => TrialChoice.Unhealthy));
            Same(before, f.Read(A));
            Yes(f.Raw == null);
        });
        Test("cancelled switch restores preceding good pair; later Disconnect still restores the first original", delegate {
            var f = new Fake("10.1.1.1");
            FixtureApply(f, A, P, false);
            var s = f.Load();
            Eq(Code.TrialUndone, GuardedTrial.Run(f, A, Catalog.Find("google"), true, s.Token, () => TrialChoice.Undo));
            Eq(s.Token, f.Load().Token);
            Same(s.Target, f.Read(A));
            Eq(Code.Ok, Change.Restore(f, s.Token, false));
            Same(s.Original, f.Read(A));
        });
        Test("external edits during the confirmation dialog are not overwritten even on timeout", delegate {
            var f = new Fake();
            var code = GuardedTrial.Run(f, A, P, false, null, () => {
                f.Map[A] = new[] {"192.168.0.8"};
                return TrialChoice.Expired;
            });
            Eq(Code.OutsideChange, code);
            Eq(1, f.Writes);
            Same(new[] {"192.168.0.8"}, f.Read(A));
            Eq("pending", f.Load().Phase);
        });
        Test("loss of journal ownership prevents stale trial from committing or rolling back", delegate {
            var f = new Fake();
            var code = GuardedTrial.Run(f, A, P, false, null, () => {
                var s = f.Load();
                s.Token = Guid.NewGuid();
                f.Save(s);
                return TrialChoice.Keep;
            });
            Eq(Code.NoSession, code);
            Eq(1, f.Writes);
            Eq("pending", f.Load().Phase);
        });
        Test("dialog error rolls back; failed rollback retains first original in journal", delegate {
            var f = new Fake("10.0.0.4");
            Eq(Code.RolledBack, GuardedTrial.Run(f, A, P, false, null, delegate {
                throw new IOException();
            }));
            Same(new[] {"10.0.0.4"}, f.Read(A));
            Eq(Code.Recover, GuardedTrial.Run(f, A, P, false, null, () => {
                f.FailAllWrites = true;
                return TrialChoice.Unhealthy;
            }));
            Same(new[] {"10.0.0.4"}, f.Load().Original);
        });
    }
    static void TrialUiCases()
    {
        Test("actual trial dialog times out without confirmation; slow checks never block its countdown", delegate {
            using (var f = new TrialWindow("Fixture", "Web", 1, ct => {
            ct.WaitHandle.WaitOne(4000);
                ct.ThrowIfCancellationRequested();
                return new PathReport();
            }, 1)) {
                Yes(!Walk(f).OfType<Button>().First(b => b.Text.Contains("نگه دار")).Enabled);
                Eq(TrialChoice.Expired, f.Ask());
            }
        });
        Test("unhealthy samples in real dialog trigger verified undo before expiry (fake adapter)", delegate {
            var f = new Fake("10.1.1.1", "192.168.1.1");
            int calls = 0;
            var code = GuardedTrial.Run(f, A, P, false, null, delegate {
                using (var dialog = new TrialWindow("Fixture", "Web", 3, ct => {
                Interlocked.Increment(ref calls);
                    return new PathReport {Pair = true, Target = true, GeneralMask = 1};
                }, 12))return dialog.Ask();
            });
            Eq(Code.TrialUnhealthy, code);
            Eq(2, calls);
            Same(new[] {"10.1.1.1", "192.168.1.1"}, f.Read(A));
            Yes(f.Raw == null);
        });
        Test("closing the temporary trial window means undo, never implicit keep", delegate {
            using (var dialog = new TrialWindow("Fixture", "Web", 0, ct => new PathReport {
            Pair = true, Target = true, GeneralMask = 3
        }, 8)) {
                dialog.Shown += delegate {
                    dialog.BeginInvoke((MethodInvoker)delegate {
                        dialog.Close();
                    });
                };
                Eq(TrialChoice.Undo, dialog.Ask());
            }
        });
        Test("actual trial dialog only permits keep after two healthy check cycles", delegate {
            int calls = 0;
            using (var f = new TrialWindow("Fixture", "Web", 3, ct => {
            Interlocked.Increment(ref calls);
                return new PathReport {Pair = true, Target = true, TargetLabel = "HTTP 200", GeneralMask = 3};
            }, 9))using (var tick = new System.Windows.Forms.Timer {Interval = 160}) {
                var keep = Walk(f).OfType<Button>().Single(b => b.Text.Contains("نگه دار"));
                tick.Tick += delegate {
                    if (keep.Enabled)keep.PerformClick();
                };
                tick.Start();
                Eq(TrialChoice.Keep, f.Ask());
                Yes(calls >= 2);
            }
        });
    }
}
}
