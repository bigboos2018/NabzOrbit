using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace Nabz
{
internal sealed class Profile
{
    internal readonly string Id, Name, Host, Path;
    Profile(string id, string name, string host, string path)
    {
        Id = id;
        Name = name;
        Host = host;
        Path = path;
    }
    internal static readonly Profile Example = new Profile("check-example", "Example", "example.com", "/");
    internal static Profile[] GeneralFor(Profile target)
    {
        return new[] {Example, All.First(x => x.Id == "general")}.Where(x => x.Host != target.Host).ToArray();
    }
    internal static Profile FromId(string id)
    {
        return All.FirstOrDefault(x => x.Id == id);
    }
    public override string ToString()
    {
        return Name;
    }
    internal static readonly Profile[] All = {new Profile("steam", "Steam", "store.steampowered.com", "/about/"), new Profile("epic", "Epic Games", "store.epicgames.com", "/en-US/"), new Profile("xbox", "Xbox", "www.xbox.com", "/en-US/"), new Profile("riot", "Riot Games", "www.riotgames.com", "/en"), new Profile("ms", "Microsoft Learn", "learn.microsoft.com", "/en-us/"), new Profile("pypi", "Python · PyPI", "pypi.org", "/project/pip/"), new Profile("general", "General · IANA", "www.iana.org", "/domains/reserved")};
}
internal sealed class WebCheck
{
    internal string State;
    internal int Status;
    internal bool Ok
    {
        get {
            return State == "http" && Status >= 200 && Status < 300;
        }
    }
    internal string Label
    {
        get {
            if (State == "http")
                return "HTTP " + Status;
            if (State == "cert")
                return "خطای TLS";
            if (State == "timeout")
                return "مهلت تمام شد";
            if (State == "skipped")
                return "تست نشد";
            return "بی‌پاسخ";
        }
    }
    internal static WebCheck Of(string s)
    {
        return new WebCheck {State = s};
    }
    internal static WebCheck Parse(string line)
    {
        if (line == null)
            return Of("invalid");
        var f = line.Split(' ');
        int code;
        if (f.Length < 2 || (f[0] != "HTTP/1.1" && f[0] != "HTTP/1.0") || f[1].Length != 3
                || !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out code) || code < 100 || code > 599)
            return Of("invalid");
        return new WebCheck {State = "http", Status = code};
    }
}
internal static class NetPath
{
    internal static bool Public(IPAddress ip)
    {
        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var a = ip.GetAddressBytes();
        int x = a[0], y = a[1], z = a[2];
        return !(x == 0 || x == 10 || x == 127 || x >= 224 || (x == 100 && y >= 64 && y <= 127) || (x == 169 && y == 254)
                 || (x == 172 && y >= 16 && y <= 31) || (x == 192 && (y == 168 || (y == 0 && (z == 0 || z == 2)) || (y == 88
                         && z == 99))) || (x == 198 && (y == 18 || y == 19 || (y == 51 && z == 100))) || (x == 203 && y == 0 && z == 113));
    }
    internal static void Bind(Socket s, IPAddress local, int idx)
    {
        if (idx > 0)
            s.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(idx));
        s.Bind(new IPEndPoint(local, 0));
    }
    // All production callers use the selected interface. No proxy, OS re-resolution, redirect, credential or custom trust callback.
    internal static WebCheck Https(Profile p, IPAddress ip, IPAddress local, int idx, CancellationToken ct)
    {
        if (!Public(ip))
            return WebCheck.Of("nonpublic");
        return TlsExchange(p.Host, p.Path, new IPEndPoint(ip, 443), local, idx, ct);
    }
    // No trust override. Internal transport seam lets tests exercise the same TLS/HTTP exchange on loopback.
    internal static WebCheck TlsExchange(string host, string path, IPEndPoint address, IPAddress local, int idx,
                                         CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (deadline.Token.Register(() => s.Close())) {
                    deadline.CancelAfter(4500);
                    try {
                        Bind(s, local, idx);
                        s.SendTimeout = 3200;
                        s.ReceiveTimeout = 3200;
                        s.Connect(address);
                        using (var ns = new NetworkStream(s, false))
                            using (var tls = new SslStream(ns, false)) {
                                tls.ReadTimeout = 3200;
                                tls.WriteTimeout = 3200;
                                tls.AuthenticateAsClient(host, null, SslProtocols.Tls12, false);
                                var bytes = Encoding.ASCII.GetBytes("HEAD " + path + " HTTP/1.1\r\nHost: " + host +
                                                                    "\r\nUser-Agent: NabzOrbit/2.1\r\nAccept: */*\r\nConnection: close\r\n\r\n");
                                tls.Write(bytes, 0, bytes.Length);
                                tls.Flush();
                                var b = new byte[1024];
                                int n = 0;
                                while (n < b.Length) {
                                    int k = tls.Read(b, n, b.Length - n);
                                    if (k == 0)
                                        break;
                                    n += k;
                                    int end = Array.IndexOf(b, (byte)10, 0, n);
                                    if (end >= 0) {
                                        ct.ThrowIfCancellationRequested();
                                        return WebCheck.Parse(Encoding.ASCII.GetString(b, 0, end).TrimEnd('\r'));
                                    }
                                }
                                return WebCheck.Of("invalid");
                            }
                    } catch (AuthenticationException) {
                        ct.ThrowIfCancellationRequested();
                        return WebCheck.Of(deadline.IsCancellationRequested ? "timeout" : "cert");
                    } catch (Exception e) {
                        if (!(e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidOperationException))
                            throw;
                        ct.ThrowIfCancellationRequested();
                        return WebCheck.Of(deadline.IsCancellationRequested ? "timeout" : "network");
                    }
                }
    }
    internal static double? TcpControl(IPAddress local, int idx, CancellationToken ct)
    {
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (deadline.Token.Register(() => s.Close())) {
                    deadline.CancelAfter(1650);
                    try {
                        Bind(s, local, idx);
                        var sw = Stopwatch.StartNew();
                        s.Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443));
                        sw.Stop();
                        ct.ThrowIfCancellationRequested();
                        return sw.Elapsed.TotalMilliseconds;
                    } catch (SocketException) {
                        ct.ThrowIfCancellationRequested();
                        return null;
                    } catch (ObjectDisposedException) {
                        ct.ThrowIfCancellationRequested();
                        return null;
                    }
                }
    }
}
}
