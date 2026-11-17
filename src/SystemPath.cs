using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nabz
{
internal sealed class PathReport
{
    internal bool Target, Pair;
    internal int GeneralMask;
    internal string TargetLabel = "بررسی نشد";
    internal bool Healthy(int beforeMask)
    {
        return Pair && Target && GeneralMask != 0 && (GeneralMask & beforeMask) == beforeMask;
    }
}
internal static class SystemPath
{
    // Microsoft's DNS_RECORDW header precedes its architecture-aligned Data union.
    [StructLayout(LayoutKind.Sequential)]internal struct Header {
        internal IntPtr Next, Name;
        internal ushort Type, Length;
        internal uint Flags, Ttl, Reserved;
    }
    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode,
               ExactSpelling = true)]static extern int DnsQuery(string name, ushort type, uint flags, IntPtr servers, out IntPtr result,
                       IntPtr reserved);
    [DllImport("dnsapi.dll", EntryPoint = "DnsFree", ExactSpelling = true)]static extern void Free(IntPtr list, int kind);
    static string NameOf(IntPtr p)
    {
        return p == IntPtr.Zero ? "" : (Marshal.PtrToStringUni(p) ?? "").TrimEnd('.');
    }
    internal static IPAddress[] Addresses(IntPtr list, string host)
    {
        var sets = new Dictionary<string, List<IPAddress>>(StringComparer.OrdinalIgnoreCase);
        var cnames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<IntPtr>();
        int size = Marshal.SizeOf(typeof(Header));
        for (var cur = list; cur != IntPtr.Zero;) {
            if (visited.Count >= 128 || !visited.Add(cur))return new IPAddress[0];
            var h = (Header)Marshal.PtrToStructure(cur, typeof(Header));
            var data = IntPtr.Add(cur, size);
            cur = h.Next;
            if ((h.Flags & 3) != 1)continue;
            string owner = NameOf(h.Name);
            if (String.IsNullOrEmpty(owner))continue;
            if (h.Type == 1 && h.Length == 4) {
                var raw = new byte[4];
                Marshal.Copy(data, raw, 0, 4);
                List<IPAddress> ips;
                if (!sets.TryGetValue(owner, out ips))sets[owner] = ips = new List<IPAddress>();
                ips.Add(new IPAddress(raw));
            } else if (h.Type == 5 && h.Length >= IntPtr.Size)cnames[owner] = NameOf(Marshal.ReadIntPtr(data));
        }
        string n = host.TrimEnd('.');
        for (int i = 0; i <= cnames.Count; i++) {
            List<IPAddress> ips;
            if (sets.TryGetValue(n, out ips))return ips.All(NetPath.Public) ? ips.Distinct().ToArray() : new IPAddress[0];
            string next;
            if (!cnames.TryGetValue(n, out next))break;
            n = next;
        }
        return new IPAddress[0];
    }
    static IPAddress[] Resolve(string host)
    {
        if (!Adapters.IsWindows)return new IPAddress[0];
        IntPtr head = IntPtr.Zero;
        try {
            // Default Windows DNS server selection, no explicit server list. Bypass resolver cache,
            // hosts, NetBT and multicast; fixed absolute public names only. Do not flush global cache.
            const uint options = 0x8 | 0x40 | 0x80 | 0x100 | 0x800 | 0x1000;
            return DnsQuery(host, 1, options, IntPtr.Zero, out head, IntPtr.Zero) == 0 ? Addresses(head, host) : new IPAddress[0];
        } finally {
            if (head != IntPtr.Zero)Free(head, 1);
        }
    }
    static WebCheck Check(Profile site, Adapter a, CancellationToken ct)
    {
        try {
            ct.ThrowIfCancellationRequested();
            var ips = Resolve(site.Host);
            ct.ThrowIfCancellationRequested();
            var w = WebCheck.Of("network");
            foreach (var ip in ips.Take(2)) {
                w = NetPath.Https(site, ip, a.Local, a.Index, ct);
                if (w.Ok)break;
            }
            return w;
        } catch {
            return WebCheck.Of("network");
        }
    }
    // DnsQuery_W itself is synchronous. A bounded wait never delays rollback and any
    // late native result is freed by its worker and cannot authorize a commit.
    internal static PathReport Measure(Adapter a, Profile target, bool includeTarget, int budgetMs, CancellationToken ct)
    {
        var sites = Profile.GeneralFor(target);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            deadline.CancelAfter(budgetMs);
            var token = deadline.Token;
            var general = sites.Select(site => Task.Run(() => Check(site, a, token))).ToArray();
            var selected = includeTarget ? Task.Run(() => Check(target, a, token)) : null;
            Task[] jobs = selected == null ? general.Cast<Task>().ToArray() : general.Cast<Task>().Concat(new Task[] {selected}).ToArray();
            try {
                Task.WaitAll(jobs, budgetMs, token);
            } catch (OperationCanceledException) {} catch (AggregateException) {}
            deadline.Cancel();
            ct.ThrowIfCancellationRequested();
            var report = new PathReport();
            for (int i = 0; i < general.Length; i++)if (general[i].Status == TaskStatus.RanToCompletion &&
                        general[i].Result.Ok)report.GeneralMask |= 1 << i;
            if (selected != null && selected.Status == TaskStatus.RanToCompletion) {
                report.Target = selected.Result.Ok;
                report.TargetLabel = selected.Result.Label;
            } else if (includeTarget)report.TargetLabel = "مهلت تمام شد";
            return report;
        }
    }
    internal static bool PairReplies(Provider provider, Profile target, IProbe probe, CancellationToken ct)
    {
        // Refuse even fast OS lookups if either DNS in the proposed pair has stopped answering.
        var jobs = provider.Pair(false).Select(ip => Task.Run(() => probe.Dns(ip, "example.com", ct).Kind == Kind.A &&
                                               probe.Dns(ip, target.Host, ct).Kind == Kind.A, ct)).ToArray();
        Task.WaitAll(jobs.Cast<Task>().ToArray(), ct);
        ct.ThrowIfCancellationRequested();
        return jobs.All(j => j.Result);
    }
    internal static bool SameLink(Adapter a)
    {
        try {
            var now = Adapters.Find(a.Id);
            return now.Usable && now.Index == a.Index && now.Local.Equals(a.Local) && now.Gateway == a.Gateway;
        } catch {
            return false;
        }
    }
}
}
