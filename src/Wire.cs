using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nabz
{
// DNS A/IN, with a fresh transaction ID for every connected UDP socket.
internal static class Wire
{
    internal static byte[] Query(string host, ushort id)
    {
        using (var m = new MemoryStream()) {
            byte[] h = {(byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0};
            m.Write(h, 0, h.Length);
            foreach (string label in host.Split('.')) {
                var a = Encoding.ASCII.GetBytes(label);
                if (a.Length < 1 || a.Length > 63)
                    throw new ArgumentException();
                m.WriteByte((byte)a.Length);
                m.Write(a, 0, a.Length);
            }
            byte[] tail = {0, 0, 1, 0, 1};
            m.Write(tail, 0, tail.Length);
            return m.ToArray();
        }
    }
    static int U16(byte[] b, int i)
    {
        return (b[i] << 8) | b[i + 1];
    }
    // The cursor advances past the encoded name, never past its compression target.
    static bool Name(byte[] b, int n, ref int cursor, out string name)
    {
        name = null;
        int i = cursor, returnAt = -1, steps = 0;
        var s = new StringBuilder();
        while (i < n && ++steps <= 128) {
            int len = b[i++];
            if (len == 0) {
                cursor = returnAt >= 0 ? returnAt : i;
                name = s.ToString();
                return true;
            }
            if ((len & 192) == 192) {
                if (i >= n)
                    return false;
                int ptr = ((len & 63) << 8) | b[i++];
                if (ptr < 12 || ptr >= i - 2)
                    return false; // RFC 1035: prior occurrences only
                if (returnAt < 0)
                    returnAt = i;
                i = ptr;
                continue;
            }
            if (len > 63 || i + len > n || s.Length + len + (s.Length == 0 ? 0 : 1) > 253)
                return false;
            if (s.Length > 0)
                s.Append('.');
            for (int j = 0; j < len; j++) {
                byte ch = b[i++];
                if (ch < 33 || ch > 126 || ch == 46)
                    return false;
                s.Append((char)ch);
            }
        }
        return false;
    }
    internal static Reply Parse(byte[] b, int n, ushort id, string host)
    {
        if (b == null || n < 12 || n > b.Length || U16(b, 0) != id)
            return Reply.Bad();
        int flags = U16(b, 2), count = U16(b, 6), rcode = flags & 15;
        if ((flags & 0x8000) == 0 || (flags & 0x7800) != 0 || U16(b, 4) != 1 || count > 128)
            return Reply.Bad();
        int pos = 12;
        string q;
        if (!Name(b, n, ref pos, out q) || !String.Equals(q, host, StringComparison.OrdinalIgnoreCase) || pos + 4 > n
                || U16(b, pos) != 1 || U16(b, pos + 2) != 1)
            return Reply.Bad();
        // Retry only a matched truncated response, never a timeout or a foreign question.
        if ((flags & 0x0200) != 0)return rcode == 0 ? new Reply {Kind = Kind.Truncated}: Reply.Bad();
        if (rcode == 3 && count == 0)
            return new Reply {Kind = Kind.Nx};
        if (rcode != 0 || count == 0)
            return Reply.Bad();
        pos += 4;
        var sets = new Dictionary<string, List<IPAddress>>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int k = 0; k < count; k++) {
            string owner;
            if (!Name(b, n, ref pos, out owner) || pos + 10 > n)
                return Reply.Bad();
            int type = U16(b, pos), cls = U16(b, pos + 2), len = U16(b, pos + 8);
            pos += 10;
            int end = pos + len;
            if (end > n)
                return Reply.Bad();
            if (cls == 1 && type == 1) {
                if (len != 4)
                    return Reply.Bad();
                List<IPAddress> ips;
                if (!sets.TryGetValue(owner, out ips))
                    sets[owner] = ips = new List<IPAddress>();
                ips.Add(new IPAddress(new[] {b[pos], b[pos + 1], b[pos + 2], b[pos + 3]}));
            }
            if (cls == 1 && type == 5) {
                string next;
                int j = pos;
                if (!Name(b, n, ref j, out next) || j != end)
                    return Reply.Bad();
                aliases[owner] = next;
            }
            pos = end;
        }
        string name = host;
        for (int i = 0; i <= count; i++) {
            List<IPAddress> ips;
            if (sets.TryGetValue(name, out ips))
                return new Reply {Kind = ips.All(NetPath.Public) ? Kind.A : Kind.NonPublic, IPs = ips.Distinct().ToArray()};
            string next;
            if (!aliases.TryGetValue(name, out next))
                break;
            name = next;
        }
        return Reply.Bad();
    }
    internal static bool Answer(byte[] b, int n, ushort id, string host)
    {
        return Parse(b, n, id, host).Kind == Kind.A;
    }
    internal static Reply Ask(IPAddress server, int port, IPAddress local, int idx, string host, int ms,
                              CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var salt = new byte[2];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(salt);
        ushort id = (ushort)((salt[0] << 8) | salt[1]);
        var q = Query(host, id);
        var b = new byte[4096];
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            using (ct.Register(() => s.Close())) {
                try {
                    NetPath.Bind(s, local, idx);
                    s.SendTimeout = ms;
                    s.ReceiveTimeout = ms;
                    s.Connect(new IPEndPoint(server, port));
                    var sw = Stopwatch.StartNew();
                    s.Send(q);
                    int n = s.Receive(b);
                    ct.ThrowIfCancellationRequested();
                    var r = Parse(b, n, id, host);
                    if (r.Kind == Kind.Truncated) {
                        int remaining = ms - (int)sw.ElapsedMilliseconds;
                        r = remaining > 0 ? ViaTcp(server, port, local, idx, host, id, q, remaining, ct) : new Reply {Kind = Kind.NoReply};
                    }
                    sw.Stop();
                    r.Ms = sw.Elapsed.TotalMilliseconds;
                    return r;
                } catch (SocketException) {
                    ct.ThrowIfCancellationRequested();
                    return new Reply {Kind = Kind.NoReply};
                } catch (ObjectDisposedException) {
                    ct.ThrowIfCancellationRequested();
                    return new Reply {Kind = Kind.NoReply};
                }
            }
    }
    static bool Fill(Stream stream, byte[] buf)
    {
        int off = 0;
        while (off < buf.Length) {
            int n = stream.Read(buf, off, buf.Length - off);
            if (n <= 0)return false;
            off += n;
        }
        return true;
    }
    static Reply ViaTcp(IPAddress server, int port, IPAddress local, int index, string host, ushort id, byte[] query, int left,
                        CancellationToken ct)
    {
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))using (var tcp = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp))using (deadline.Token.Register(() => tcp.Close())) {
                    deadline.CancelAfter(left);
                    try {
                        NetPath.Bind(tcp, local, index);
                        tcp.SendTimeout = left;
                        tcp.ReceiveTimeout = left;
                        tcp.Connect(new IPEndPoint(server, port));
                        using (var stream = new NetworkStream(tcp, false)) {
                            byte[] size = {(byte)(query.Length >> 8), (byte)query.Length};
                            stream.Write(size, 0, 2);
                            stream.Write(query, 0, query.Length);
                            if (!Fill(stream, size))return Reply.Bad();
                            int n = (size[0] << 8) | size[1];
                            if (n < 12 || n > 16384)return Reply.Bad();
                            var data = new byte[n];
                            if (!Fill(stream, data))return Reply.Bad();
                            ct.ThrowIfCancellationRequested();
                            var r = Parse(data, n, id, host);
                            return r.Kind == Kind.Truncated ? Reply.Bad() : r;
                        }
                    } catch (IOException) {
                        ct.ThrowIfCancellationRequested();
                        return new Reply {Kind = Kind.NoReply};
                    } catch (SocketException) {
                        ct.ThrowIfCancellationRequested();
                        return new Reply {Kind = Kind.NoReply};
                    } catch (ObjectDisposedException) {
                        ct.ThrowIfCancellationRequested();
                        return new Reply {Kind = Kind.NoReply};
                    }
                }
    }
    internal static double ? Sample(IPAddress server, int port, IPAddress local, int idx, string host, int ms,
                                    CancellationToken ct)
    {
        var r = Ask(server, port, local, idx, host, ms, ct);
        return r.Kind == Kind.A ? (double ?)r.Ms : null;
    }
}
internal enum Kind {Bad, A, Nx, NonPublic, NoReply, Truncated}
internal sealed class Reply
{
    internal Kind Kind;
    internal IPAddress[] IPs = new IPAddress[0];
    internal double Ms;
    internal static Reply Bad()
    {
        return new Reply {Kind = Kind.Bad};
    }
}
}
