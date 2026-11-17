using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nabz
{
internal sealed class Adapter
{
    internal Guid Id;
    internal int Index;
    internal string Name, Gateway;
    internal IPAddress Local;
    internal string[] Effective;
    internal bool Usable, Up, Preferred;
    public override string ToString()
    {
        return Name;
    }
}
internal static class Adapters
{
    [DllImport("iphlpapi.dll")] static extern uint GetBestInterface(uint address, out uint index);
    internal static bool IsWindows
    {
        get {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }
    }
    static bool PhysicalType(NetworkInterfaceType t)
    {
        return t == NetworkInterfaceType.Wireless80211 || t == NetworkInterfaceType.Ethernet
               || t == NetworkInterfaceType.GigabitEthernet || t == NetworkInterfaceType.FastEthernetFx
               || t == NetworkInterfaceType.FastEthernetT || t == NetworkInterfaceType.Ethernet3Megabit;
    }
    internal static List<Adapter> List()
    {
        var result = new List<Adapter>();
        if (!IsWindows)
            return result;
        uint preferred = 0;
        try {
            GetBestInterface(BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes(), 0), out preferred);
        } catch (EntryPointNotFoundException) {}
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
            try {
                Guid id;
                if (!Guid.TryParse(ni.Id, out id) || !ni.Supports(NetworkInterfaceComponent.IPv4)
                        || !PhysicalType(ni.NetworkInterfaceType))
                    continue;
                var pr = ni.GetIPProperties();
                var v4 = pr.GetIPv4Properties();
                if (v4 == null || v4.Index <= 0)
                    continue;
                var local = pr.UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork
                                                      && !IPAddress.IsLoopback(x.Address) && !x.Address.Equals(IPAddress.Any)).Select(x => x.Address).FirstOrDefault();
                var gateway = pr.GatewayAddresses.Select(x => x.Address).FirstOrDefault(x => x.AddressFamily ==
                              AddressFamily.InterNetwork && !x.Equals(IPAddress.Any));
                bool up = ni.OperationalStatus == OperationalStatus.Up;
                result.Add(new Adapter {Id = id, Index = v4.Index, Name = ni.Name, Local = local, Gateway = gateway == null ? "" : gateway.ToString(), Up = up, Usable = up && local != null && gateway != null, Preferred = v4.Index == preferred, Effective = pr.DnsAddresses.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Select(x => x.ToString()).ToArray()});
            } catch (NetworkInformationException) { /* disconnected while enumerating */ }
        }
        return result.OrderByDescending(x => x.Preferred).ThenBy(x => x.Name,
                StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    internal static Adapter Find(Guid id)
    {
        var a = List().FirstOrDefault(x => x.Id == id);
        if (a == null)
            throw new Issue(Code.NoAdapter);
        return a;
    }
}
internal sealed class WinSettings : ISettings
{
    internal const string JournalKey = @"SOFTWARE\NabzDNS";
    const string Val = "SessionV1";
    static RegistryKey Machine()
    {
        return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                                       Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
    }
    public Session Load()
    {
        try {
            using (var root = Machine())
                using (var k = root.OpenSubKey(JournalKey, false)) {
                    if (k == null)
                        return null;
                    var o = k.GetValue(Val, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (o == null)
                        return null;
                    if (!(o is string))
                        throw new Issue(Code.SavedStateError);
                    return Session.Decode((string)o);
                }
        } catch (Issue) {
            throw;
        } catch {
            throw new Issue(Code.SavedStateError);
        }
    }
    public void Save(Session s)
    {
        string raw = s.Encode();
        Session.Decode(raw); // also validate our own serialization before committing
        using (var root = Machine())
            using (var k = root.CreateSubKey(JournalKey)) {
                k.SetValue(Val, raw, RegistryValueKind.String);
                k.Flush();
            }
        var saved = Load();
        if (saved == null || saved.Encode() != raw)
            throw new Issue(Code.SavedStateError);
    }
    public void Clear()
    {
        using (var root = Machine())
            using (var k = root.OpenSubKey(JournalKey, true)) {
                if (k != null) {
                    k.DeleteValue(Val, false);
                    k.Flush();
                }
            }
        if (Load() != null)
            throw new Issue(Code.SavedStateError);
    }
    internal static bool HasGlobalOverride()
    {
        using (var r = Machine())
            foreach (string path in new[] {@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"})

                using (var k = r.OpenSubKey(path, false)) {
                    if (k != null && !String.IsNullOrWhiteSpace(Convert.ToString(k.GetValue("NameServer", ""))))
                        return true;
                }
        return false;
    }
    public string[] Read(Guid adapter)
    {
        using (var root = Machine())
            using (var k = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" +
                                           adapter.ToString("B"), false)) {
                if (k == null)
                    throw new Issue(Code.NoAdapter);
                var raw = k.GetValue("NameServer", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (raw == null)
                    throw new Issue(Code.UnsafeConfig);
                var a = raw.Split(new[] {' ', ',', ';', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
                if (a.Length > 64 || a.Any(x => !Catalog.IPv4(x)) || a.Distinct().Count() != a.Length)
                    throw new Issue(Code.UnsafeConfig);
                return a;
            }
    }
    // Only netsh's IPv4 DNS context is used. No shell and no free-form user strings.
    static void Netsh(Guid id, string action, string options)
    {
        var a = Adapters.Find(id);
        string args = "interface ipv4 " + action + " dnsservers name=" + a.Index.ToString(
                          CultureInfo.InvariantCulture) + " " + options;
        var si = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args) {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory
        };
        using (var p = Process.Start(si)) {
            if (!p.WaitForExit(8500)) {
                try {
                    p.Kill();
                    p.WaitForExit(2000);
                } catch (InvalidOperationException) {} throw new IOException("netsh timeout");
            }
            if (p.ExitCode != 0)
                throw new IOException("netsh did not accept the change");
        }
    }
    public void Set(Guid id, string[] addresses)
    {
        if (addresses.Length > 64 || addresses.Any(x => !Catalog.IPv4(x)))
            throw new Issue(Code.UnsafeConfig);
        if (addresses.Length == 0) {
            Netsh(id, "set", "source=dhcp");
            return;
        }
        Netsh(id, "set", "source=static address=" + addresses[0] + " validate=no");
        for (int i = 1; i < addresses.Length; i++)
            Netsh(id, "add", "address=" + addresses[i] + " index=" + (i + 1).ToString(CultureInfo.InvariantCulture) +
                  " validate=no");
    }
    public bool Verify(Guid id, string[] expected)
    {
        for (int i = 0; i < 15; i++) {
            if (Read(id).SequenceEqual(expected)) {
                // For automatic mode, verify that the override is gone; lease data may renew later.
                if (expected.Length == 0)
                    return true;
                var a = Adapters.Find(id);
                if (!a.Up || a.Effective.SequenceEqual(expected))
                    return true;
            }
            if (i < 14)
                Thread.Sleep(120);
        }
        return false;
    }
}
internal static class Worker
{
    internal static Code Run(string[] args)
    {
        if (!Adapters.IsWindows)
            return Code.BadInput;
        try {
            using (var user = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))
                    return Code.NeedAdmin;
            using (var gate = new Mutex(false, @"Global\NabzDNS_6AE117D5_Mutation")) {
                bool acquired;
                try {
                    acquired = gate.WaitOne(0);
                } catch (AbandonedMutexException) {
                    acquired = true;
                }
                if (!acquired)
                    return Code.Busy;
                try {
                    var io = new WinSettings();
                    Guid id;
                    if (args.Length == 3 && args[0] == "--restore" && Guid.TryParseExact(args[1], "D", out id) && (args[2] == "normal"
                            || args[2] == "confirmed"))
                        return Change.Restore(io, id, args[2] == "confirmed");
                    Guid prior = Guid.Empty;
                    bool swap = args.Length == 11 && args[0] == "--trial-switch" && Guid.TryParseExact(args[10], "D", out prior);
                    if (!swap && (args.Length != 10 || args[0] != "--trial"))return Code.BadInput;
                    var provider = Catalog.Find(args[2]);
                    var profile = Profile.FromId(args[6]);
                    if (!Guid.TryParseExact(args[1], "D", out id) || id == Guid.Empty || provider == null || profile == null || (args[3] != "0" &&
                            args[3] != "1") || !Catalog.IPv4(args[4]) || !Catalog.IPv4(args[5]) ||
                            args[7] != (provider.Regional ? "ir" : "public"))return Code.BadInput;
                    var a = Adapters.Find(id);
                    if (!a.Usable || a.Local.ToString() != args[4] || a.Gateway != args[5])return Code.NetworkChanged;
                    if (WinSettings.HasGlobalOverride())return Code.UnsafeConfig;
                    if (provider.Regional && !RegionalAccess.Allowed(a))return Code.RegionalBlocked;
                    var priorSession = io.Load();
                    if (!swap && priorSession != null)return Code.HasSession;
                    if (swap && (priorSession == null || priorSession.Token != prior || priorSession.Adapter != id))return Code.NoSession;
                    if (!ApplyStamp.Valid(args[8], args[9], a, io.Read(id), DateTime.UtcNow))return Code.Stale;
                    if (a.Effective.Length == 2 &&
                            a.Effective.OrderBy(x => x).SequenceEqual(provider.Pair(false).OrderBy(x => x)))return Code.Unchanged;
                    var direct = new RealProbe(a, provider.Regional);
                    if (!SystemPath.PairReplies(provider, profile, direct, CancellationToken.None))return Code.Preflight;
                    int before = SystemPath.Measure(a, profile, false, 7000, CancellationToken.None).GeneralMask;
                    var beforeConfig = io.Read(id);
                    if (!ApplyStamp.Valid(args[8], args[9], Adapters.Find(id), beforeConfig, DateTime.UtcNow))return Code.Stale;
                    if (WinSettings.HasGlobalOverride())return Code.UnsafeConfig;
                    if (provider.Regional && !RegionalAccess.Allowed(a))return Code.RegionalBlocked;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    return GuardedTrial.Run(io, id, provider, args[3] == "1", swap ? (Guid ?)prior : null, delegate {
                        Func<CancellationToken, PathReport> probe = ct => {
                            if (!SystemPath.SameLink(a))return new PathReport {
                                TargetLabel = "اتصال عوض شد"
                            };
                            var now = Adapters.Find(id);
                            if (!now.Effective.SequenceEqual(provider.Pair(args[3] == "1")))return new PathReport {TargetLabel = "DNS کارت عوض شد"};
                            var pair = Task.Run(() => SystemPath.PairReplies(provider, profile, direct, ct), ct);
                            var r = SystemPath.Measure(a, profile, true, 8500, ct);
                            r.Pair = pair.GetAwaiter().GetResult();
                            return r;
                        };
                        using (var dialog = new TrialWindow(provider.Name, profile.Name, before, probe, TrialGate.Seconds)) {
                            var answer = dialog.Ask();
                            return answer == TrialChoice.Keep && !SystemPath.SameLink(a) ? TrialChoice.Unhealthy : answer;
                        }
                    }, beforeConfig);
                } finally {
                    gate.ReleaseMutex();
                }
            }
        } catch (Issue e) {
            return e.Reason;
        } catch (UnauthorizedAccessException) {
            return Code.NeedAdmin;
        } catch {
            return Code.Failed;
        }
    }
}
}
