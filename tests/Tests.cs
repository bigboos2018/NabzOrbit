using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nabz
{
internal static partial class Tests
{
    static int count, failed;
    static void Test(string name, Action test)
    {
        count++;
        try {
            test();
            Console.WriteLine("PASS  " + name);
        } catch (Exception e) {
            failed++;
            Console.WriteLine("FAIL  " + name + ": " + e);
        }
    }
    static void Yes(bool v)
    {
        if (!v)
            throw new Exception("expected true");
    }
    static void Eq<T>(T x, T y)
    {
        if (!Object.Equals(x, y))
            throw new Exception("expected " + x + "; got " + y);
    }
    static void Same(string[] a, string[] b)
    {
        Yes(a.SequenceEqual(b));
    }
    static void Throws<T>(Action a) where T: Exception
    {
        try {
            a();
        } catch (T) {
            return;
        }
        throw new Exception("expected " + typeof(T).Name);
    }
    const string Host = "example.com";
    const ushort Id = 0xface;
    static byte[] Record(byte[] owner, int type, params byte[] data)
    {
        return owner.Concat(new byte[] {0, (byte)type, 0, 1, 0, 0, 0, 30, (byte)(data.Length >> 8), (byte)data.Length}).Concat(
                   data).ToArray();
    }
    static byte[] Ok(byte[] query)
    {
        var r = (byte[])query.Clone();
        r[2] = 0x81;
        r[3] = 0x80;
        r[7] = 1;
        return r.Concat(Record(new byte[] {0xc0, 12}, 1, 93, 184, 216, 34)).ToArray();
    }
    static bool Answer(byte[] b)
    {
        return Wire.Answer(b, b.Length, Id, Host);
    }
    static byte[] Good()
    {
        return Ok(Wire.Query(Host, Id));
    }
    static Provider P
    {
        get {
            return Catalog.All[0];
        }
    }
    static Guid A
    {
        get {
            return new Guid("9cfd948d-c892-40af-813d-0b1228201bf3");
        }
    }
    static Guid B
    {
        get {
            return new Guid("0813474f-2035-4cf4-82ef-689453e80832");
        }
    }
    sealed class Fake: ISettings
    {
        internal string Raw;
        internal readonly Dictionary<Guid, string[]> Map = new Dictionary<Guid, string[]>();
        internal int Writes, Saves, VerifyCalls;
        internal bool FailNext, FailSave, FailAllWrites, FailFirstWrite, NoVerifyAfterWrite;
        internal Guid Last;
        internal Fake(params string[] original)
        {
            Map[A] = original;
            Map[B] = new[] {"10.20.30.1"};
        }
        public Session Load()
        {
            return Raw == null ? null : Session.Decode(Raw);
        }
        public void Save(Session s)
        {
            if (FailSave)
                throw new IOException();
            Raw = s.Encode();
            Saves++;
        }
        public void Clear()
        {
            Raw = null;
        }
        public string[] Read(Guid g)
        {
            return (string[])Map[g].Clone();
        }
        public void Set(Guid g, string[] x)
        {
            Yes(Load() != null);
            Last = g;
            Writes++;
            if (FailNext || FailAllWrites || (FailFirstWrite && Writes == 1)) {
                FailNext = false;
                if (x.Length > 0)Map[g] = x.Take(1).ToArray();
                throw new IOException();
            }
            Map[g] = (string[])x.Clone();
        }
        public bool Verify(Guid g, string[] x)
        {
            VerifyCalls++;
            return !(NoVerifyAfterWrite && Writes > 0) && Map[g].SequenceEqual(x);
        }
    }
    static double? LocalSample(bool badId)
    {
        using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            socket.Client.ReceiveTimeout = 2500;
            var end = (IPEndPoint)socket.Client.LocalEndPoint;
            var responder = Task.Run(delegate {
                var peer = new IPEndPoint(IPAddress.Any, 0);
                byte[] q = socket.Receive(ref peer);
                var r = Ok(q);
                if (badId)
                    r[1] ^= 0xff;
                socket.Send(r, r.Length, peer);
            });
            var ms = Wire.Sample(IPAddress.Loopback, end.Port, IPAddress.Loopback, 0, Host, 1500, CancellationToken.None);
            responder.GetAwaiter().GetResult();
            return ms;
        }
    }
    [STAThread] internal static int Main(string[] args)
    {
        int port;
        if (args.Length == 2 && args[0] == "--tls-loopback" && int.TryParse(args[1], out port))
            return LocalTls(port);
        Test("embedded catalog: 14 distinct catalog pairs, 12 public by default", delegate {
            Eq(14, Catalog.All.Length);
            Eq(12, Catalog.Available(false).Length);
            Eq(28, Catalog.All.SelectMany(p => p.Pair(false)).Distinct().Count());
            Eq("149.112.112.112", Catalog.Find("quad9").B);
        });
        Test("IPv4 inputs are canonical and numeric", delegate {
            Yes(Catalog.IPv4("192.168.1.1"));
            foreach (var s in new[] {"127.1", "001.0.0.1", "1.2.3.4 ", "::1", "abc", "", "1.2.3.4;test"})
                Yes(!Catalog.IPv4(s));
        });
        Test("A IN query header and question", delegate {
            var q = Wire.Query(Host, Id);
            Eq((byte)0xfa, q[0]);
            Eq((byte)0xce, q[1]);
            Eq((byte)1, q[2]);
            Eq((byte)1, q[5]);
            Eq((byte)7, q[12]);
            Eq(29, q.Length);
            Eq((byte)1, q[28]);
        });
        Test("realistic compressed A answer", delegate {
            Yes(Answer(Good()));
        });
        Test("case-insensitive QNAME", delegate {
            Yes(Answer(Ok(Wire.Query("EXAMPLE.COM", Id))));
        });
        Test("wrong response identity rejected", delegate {
            var x = Good();
            x[0] ^= 4;
            Yes(!Answer(x));
            x = Good();
            x[13] = (byte)'z';
            Yes(!Answer(x));
        });
        Test("error, non-answer, opcode and truncation rejected", delegate {
            foreach (int mask in new[] {
            0x8000, 0x0200, 0x0800, 3, 2
        }) {
                var x = Good();
                x[2] ^= (byte)(mask >> 8);
                x[3] ^= (byte)mask;
                Yes(!Answer(x));
            }
        });
        Test("wrong question class/type rejected", delegate {
            var x = Good();
            x[26] = 28;
            Yes(!Answer(x));
            x = Good();
            x[28] = 3;
            Yes(!Answer(x));
        });
        Test("empty/referral answer rejected", delegate {
            var x = Good();
            x[7] = 0;
            Yes(!Answer(x));
        });
        Test("unrelated A record rejected", delegate {
            var x = Good();
            x[30] = 20;
            Yes(!Answer(x));
        });
        Test("bad record length rejected", delegate {
            var x = Good();
            x[x.Length - 5] = 5;
            Yes(!Answer(x));
        });
        Test("self pointer rejected without looping", delegate {
            var x = Good();
            x[30] = 29;
            Yes(!Answer(x));
        });
        Test("all prefix truncations rejected", delegate {
            var x = Good();
            for (int i = 0; i < x.Length; i++)
                Yes(!Wire.Answer(x, i, Id, Host));
        });
        Test("CNAME chain ending in A accepted", delegate {
            var q = Wire.Query(Host, Id);
            q[2] = 0x81;
            q[3] = 0x80;
            q[7] = 2;
            var c = Record(new byte[] {0xc0, 12}, 5, 3, (byte)'w', (byte)'w', (byte)'w', 0xc0, 12);
            var a = Record(new byte[] {0xc0, (byte)(q.Length + 12)}, 1, 93, 184, 216, 34);
            Yes(Answer(q.Concat(c).Concat(a).ToArray()));
        });
        Test("UDP round trip over loopback validates wire response", delegate {
            Yes(LocalSample(false).HasValue);
        });
        Test("UDP round trip with wrong ID does not score", delegate {
            Yes(!LocalSample(true).HasValue);
        });
        Test("no answer times out and is not scored", delegate {
            using (var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
                var ep = (IPEndPoint)u.Client.LocalEndPoint;
                Yes(!Wire.Sample(IPAddress.Loopback, ep.Port, IPAddress.Loopback, 0, Host, 130, CancellationToken.None).HasValue);
            }
        });
        Test("pre-cancellation sends no query", delegate {
            Throws<OperationCanceledException>(() => Wire.Sample(IPAddress.Loopback, 53, IPAddress.Loopback, 0, Host, 300,
                                               new CancellationToken(true)));
        });
        Test("cancel an in-flight receive", delegate {
            using (var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                using (var cts = new CancellationTokenSource()) {
                    var ep = (IPEndPoint)u.Client.LocalEndPoint;
                    cts.CancelAfter(70);
                    Throws<OperationCanceledException>(() => Wire.Sample(IPAddress.Loopback, ep.Port, IPAddress.Loopback, 0, Host, 1500,
                                                       cts.Token));
                }
        });
        OrbitCases();
        GuardCases();
        Test("DHCP is restored as automatic, not a stale lease", delegate {
            var f = new Fake();
            Eq(Code.Ok, FixtureApply(f, A, P, false));
            Eq("active", f.Load().Phase);
            Same(P.Pair(false), f.Read(A));
            Eq(Code.Ok, Change.Restore(f, f.Load().Token, false));
            Same(new string[0], f.Read(A));
            Yes(f.Raw == null);
        });
        Test("three manual servers are restored in original order after restart", delegate {
            string[] orig = {"10.0.0.8", "192.168.0.2", "10.0.0.4"};
            var f = new Fake(orig);
            FixtureApply(f, A, P, true);
            var restart = new Fake();
            restart.Raw = f.Raw;
            restart.Map[A] = f.Read(A);
            Eq(Code.Ok, Change.Restore(restart, restart.Load().Token, false));
            Same(orig, restart.Read(A));
            Same(new[] {"10.20.30.1"}, restart.Read(B));
        });
        Test("existing journal is never overwritten", delegate {
            var f = new Fake();
            FixtureApply(f, A, P, false);
            string raw = f.Raw;
            Eq(Code.HasSession, FixtureApply(f, B, Catalog.All[1], false));
            Eq(raw, f.Raw);
            Eq(1, f.Writes);
        });
        Test("no write for identical configuration", delegate {
            var f = new Fake(P.Pair(false));
            Eq(Code.Unchanged, FixtureApply(f, A, P, false));
            Eq(0, f.Writes);
            Yes(f.Raw == null);
        });
        Test("failure storing backup prevents all changes", delegate {
            var f = new Fake {
                FailSave = true
            };
            Throws<IOException>(() => FixtureApply(f, A, P, false));
            Eq(0, f.Writes);
        });
        Test("failure after partial set rolls back", delegate {
            var f = new Fake("10.0.0.1") {
                FailFirstWrite = true
            };
            Eq(Code.RolledBack, FixtureApply(f, A, P, false));
            Same(new[] {"10.0.0.1"}, f.Read(A));
            Yes(f.Raw == null);
        });
        Test("failed rollback retains recoverable baseline", delegate {
            var f = new Fake("10.0.0.3") {
                FailAllWrites = true
            };
            Eq(Code.Recover, FixtureApply(f, A, P, false));
            Same(new[] {"10.0.0.3"}, f.Load().Original);
            f.FailAllWrites = false;
            Eq(Code.Ok, Change.Restore(f, f.Load().Token, true));
            Same(new[] {"10.0.0.3"}, f.Read(A));
        });
        Test("read-back mismatch never reports success", delegate {
            var f = new Fake {
                NoVerifyAfterWrite = true
            };
            Eq(Code.Recover, FixtureApply(f, A, P, false));
            Yes(f.Raw != null);
        });
        Test("external edit is not overwritten without consent", delegate {
            var f = new Fake();
            FixtureApply(f, A, P, false);
            var token = f.Load().Token;
            f.Map[A] = new[] {"192.168.0.1"};
            Eq(Code.OutsideChange, Change.Restore(f, token, false));
            Same(new[] {"192.168.0.1"}, f.Read(A));
            Eq(Code.Ok, Change.Restore(f, token, true));
        });
        Test("stale session token cannot restore another session", delegate {
            var f = new Fake();
            FixtureApply(f, A, P, false);
            string raw = f.Raw;
            Eq(Code.NoSession, Change.Restore(f, Guid.NewGuid(), true));
            Eq(raw, f.Raw);
            Eq(1, f.Writes);
        });
        Test("already-restored adapter clears backup without a write", delegate {
            var f = new Fake();
            FixtureApply(f, A, P, false);
            f.Map[A] = new string[0];
            Eq(Code.Ok, Change.Restore(f, f.Load().Token, false));
            Eq(1, f.Writes);
            Yes(f.Raw == null);
        });
        Test("original list not yet effective keeps the snapshot and avoids another write", delegate {
            var f = new Fake("192.168.0.2");
            FixtureApply(f, A, P, false);
            f.Map[A] = f.Load().Original;
            f.NoVerifyAfterWrite = true;
            Eq(Code.Recover, Change.Restore(f, f.Load().Token, false));
            Yes(f.Raw != null);
            Eq(1, f.Writes);
        });
        Test("recovery after interruption between apply and commit", delegate {
            var f = new Fake();
            FixtureApply(f, A, P, false);
            var s = f.Load();
            s.Phase = "pending";
            f.Save(s);
            Eq(Code.Ok, Change.Restore(f, s.Token, false));
            Yes(f.Raw == null);
        });
        Test("failed restoration preserves journal for retry", delegate {
            var f = new Fake("10.0.1.1");
            FixtureApply(f, A, P, false);
            var token = f.Load().Token;
            f.FailAllWrites = true;
            Eq(Code.Recover, Change.Restore(f, token, false));
            Eq("restoring", f.Load().Phase);
            Same(new[] {"10.0.1.1"}, f.Load().Original);
        });
        Test("invalid journal fails closed", delegate {
            foreach (string x in new[] {
            "", "corrupt", "1|" + A + "|" + B + "|google|0|active|1.2.3.4 text", "1|" + A + "|" + B + "|unknown|0|pending|"
        })
            Throws<Issue>(() => Session.Decode(x));
        });
        if (Adapters.IsWindows)
            Test("Windows adapter inventory and journal (read only)", delegate {
            Adapters.List();
            new WinSettings().Load();
        });
        if (args.Contains("--ui"))
            Test("main window opens, initializes twelve default rows and closes (read only)", delegate {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var f = new MainWindow(false)) {
                int rows = -1;
                bool scroll = false, opt = false;
                f.Shown += delegate {
                    f.BeginInvoke((MethodInvoker)delegate {
                        var table = Walk(f).OfType<DataGridView>().Single();
                        rows = table.Rows.Count;
                        var last = table.Rows[rows - 1];
                        // WinForms computes Displayed during paint/layout, not on assignment alone.
                        Application.DoEvents();table.Focus();table.CurrentCell = last.Cells[0];
                        table.Refresh();Application.DoEvents();
                        scroll = table.ScrollBars != ScrollBars.None && last.Displayed;
                        opt = Walk(f).OfType<CheckBox>().Single().Checked;
                        f.Close();
                    });
                };
                Application.Run(f);
                Eq(12, rows);
                Yes(scroll);
                Yes(!opt);
            }
        });
        if (args.Contains("--ui"))TrialUiCases();
        Console.WriteLine("\n" + (count - failed) + "/" + count + " passed.");
        return failed == 0 ? 0 : 1;
    }
    static IEnumerable<Control> Walk(Control c)
    {
        foreach (Control x in c.Controls) {
            yield return x;
            foreach (var y in Walk(x))
                yield return y;
        }
    }
}
}
