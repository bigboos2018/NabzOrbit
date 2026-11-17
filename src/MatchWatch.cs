using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nabz
{
internal sealed class Frame
{
    internal int Resolvers = 1;
    internal DateTimeOffset At;
    internal DateTimeOffset? Mark;
    internal double? Gateway, Internet, First, Second;
    internal double? Dns
    {
        get {
            return First ?? Second;
        }
    }
}
internal static class MatchWatch
{
    [DllImport("iphlpapi.dll", SetLastError = true)]static extern IntPtr IcmpCreateFile();
    [DllImport("iphlpapi.dll")]static extern bool IcmpCloseHandle(IntPtr handle);
    [DllImport("iphlpapi.dll", SetLastError = true)]static extern uint IcmpSendEcho2Ex(IntPtr handle, IntPtr evt,
            IntPtr apc, IntPtr context, uint source, uint target, IntPtr payload, ushort payloadSize, IntPtr opts, IntPtr reply,
            uint replySize, uint timeout);
    static double ? Router(Adapter a)
    {
        IntPtr h = IcmpCreateFile();
        if (h == IntPtr.Zero || h == new IntPtr(-1))
            return null;
        IntPtr buf = IntPtr.Zero, data = IntPtr.Zero;
        try {
            buf = Marshal.AllocHGlobal(128);
            data = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(data, 0x4e425a32);
            uint count = IcmpSendEcho2Ex(h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                                         BitConverter.ToUInt32(a.Local.GetAddressBytes(), 0),
                                         BitConverter.ToUInt32(IPAddress.Parse(a.Gateway).GetAddressBytes(), 0), data, 4, IntPtr.Zero, buf, 128, 1100);
            return count > 0 && Marshal.ReadInt32(buf, 4) == 0 ? (double ?)Math.Max(0, Marshal.ReadInt32(buf, 8)) : null;
        } finally {
            if (buf != IntPtr.Zero)
                Marshal.FreeHGlobal(buf);
            if (data != IntPtr.Zero)
                Marshal.FreeHGlobal(data);
            IcmpCloseHandle(h);
        }
    }
    internal static async Task<Frame> Read(Adapter a, CancellationToken ct, bool regional = false)
    {
        var f = new Frame {At = DateTimeOffset.Now};
        var names = (a.Effective ?? new string[0]).Where(Catalog.IPv4).Distinct().Take(2).ToArray();
        if (names.Any(Catalog.IsRegionalAddress) && (!regional || !RegionalAccess.Allowed(a)))names = new string[0];
        f.Resolvers = names.Length;
        Func < int, double ? > dns = i => i < names.Length ? Wire.Sample(IPAddress.Parse(names[i]), 53, a.Local, a.Index,
                                     "example.com", 1250, ct) : null;
        var gw = Task.Run(() => Router(a), ct);
        var link = Task.Run(() => NetPath.TcpControl(a.Local, a.Index, ct), ct);
        var primary = Task.Run(() => dns(0), ct);
        var backup = Task.Run(() => dns(1), ct);
        await Task.WhenAll(gw, link, primary, backup).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        f.Gateway = gw.Result;
        f.Internet = link.Result;
        f.First = primary.Result;
        f.Second = backup.Result;
        return f;
    }
    internal static string Diagnose(IList<Frame> all)
    {
        if (all.Count < 3)
            return "warming";
        var x = all.Skip(all.Count - 3).ToArray();
        if (x.All(f => f.Resolvers == 0))
            return "no-v4-dns";
        bool route = x.All(f => !f.Internet.HasValue), router = x.All(f => !f.Gateway.HasValue),
             dns = x.All(f => !f.Dns.HasValue);
        if (router && route && all.Any(f => f.Gateway.HasValue) && dns)
            return "local";
        if (route && dns && x.All(f => f.Gateway.HasValue))
            return "upstream";
        if (dns && x.All(f => f.Internet.HasValue))
            return "dns";
        if (x.All(f => !f.First.HasValue && f.Second.HasValue))
            return "secondary";
        if (route)
            return "control";
        if (x.All(f => f.Dns.HasValue && f.Internet.HasValue))
            return "sample-ok";
        return "mixed";
    }
    internal static string Message(string cause)
    {
        switch (cause) {
        case "no-v4-dns":
            return "هیچ DNS نسخهٔ چهارِ مجاز برای این نمونه نبود؛ نبود نمونه به معنی قطع DNS نیست.";

        case "local":
            return "مودم، مسیر بیرونی و DNS سه بار با هم بی‌پاسخ ماندند؛ پیوند محلی را هم بررسی کن.";

        case "upstream":
            return "مودم پاسخ داد ولی مسیر بیرونی و DNS سه بار بی‌پاسخ بودند. مسیر بعد از مودم را بررسی کن.";

        case "dns":
            return "مسیر اینترنتی پاسخ داد ولی DNS سه بار متوالی پاسخ معتبر نداد. پس از بازی دوباره DNSها را بسنج.";

        case "secondary":
            return "سه نمونه فقط از DNS دوم پاسخ گرفتند. پس از مسابقه جفت دیگری را مقایسه کن.";

        case "control":
            return "مسیر آزمایشی بیرونی پاسخ نداده؛ این به‌تنهایی نشانهٔ قطع اینترنت نیست.";

        case "sample-ok":
            return "مسیر آزمایشی اینترنت و DNS پاسخ دادند. قطعی بازی هنوز می‌تواند از مسیر یا سرور خود بازی باشد.";

        case "mixed":
            return "نتیجهٔ نمونه‌ها یکسان نیست. زمان قطعی بازی را علامت بزن و با نمودار مقایسه کن.";

        default:
            return "برای تفسیر، حداقل سه نمونه لازم است. یک مهلت‌تمام‌شده به‌تنهایی علت قطعی را مشخص نمی‌کند.";
        }
    }
    static string Val(double ? n)
    {
        return n.HasValue ? n.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
    }
    internal static string Csv(IEnumerable<Frame> frames)
    {
        var s = new
        StringBuilder("time_local,gateway_icmp_ms,control_tcp443_ms,primary_dns_ms,secondary_dns_ms,user_mark_local,dns_servers_tested\r\n");

        foreach (var f in frames)
            s.AppendLine(String.Join(",", new[] {f.At.ToString("o"), Val(f.Gateway), Val(f.Internet), Val(f.First), Val(f.Second), f.Mark.HasValue ? f.Mark.Value.ToString("o") : "", f.Resolvers.ToString(CultureInfo.InvariantCulture)}));
        return s.ToString();
    }
}
}
