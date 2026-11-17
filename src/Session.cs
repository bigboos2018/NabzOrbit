using System;
using System.IO;
using System.Linq;

namespace Nabz
{
internal enum Code { Ok = 0, BadInput = 10, NeedAdmin = 11, Busy = 12, HasSession = 13, NoAdapter = 14, UnsafeConfig = 15, SavedStateError = 16, Unchanged = 17, RolledBack = 18, Recover = 19, OutsideChange = 20, NoSession = 21, NetworkChanged = 22, Cancelled = 23, Failed = 24, TrialUndone = 25, TrialExpired = 26, TrialUnhealthy = 27, RegionalBlocked = 28, Stale = 29, Preflight = 30 }
internal sealed class Issue : Exception
{
    internal readonly Code Reason;
    internal Issue(Code reason): base(reason.ToString())
    {
        Reason = reason;
    }
}
// Empty Original means automatic DNS, not a copy of the current DHCP lease.
internal sealed class Session
{
    internal Guid Token, Adapter;
    internal Provider Provider;
    internal bool Reverse;
    internal string Phase;
    internal string[] Original;
    internal string[] Target
    {
        get {
            return Provider.Pair(Reverse);
        }
    }
    internal string Encode()
    {
        return String.Join("|", new[] {"1", Token.ToString("D"), Adapter.ToString("D"), Provider.Id, Reverse ? "1" : "0", Phase, String.Join(",", Original)});
    }
    internal static Session Decode(string raw)
    {
        if (raw == null || raw.Length > 2048)
            throw new Issue(Code.SavedStateError);
        var f = raw.Split('|');
        Guid t, g;
        if (f.Length != 7 || f[0] != "1" || !Guid.TryParseExact(f[1], "D", out t) || !Guid.TryParseExact(f[2], "D", out g)
                || t == Guid.Empty || g == Guid.Empty || Catalog.Find(f[3]) == null || (f[4] != "0" && f[4] != "1") || !(new[] {"pending", "active", "restoring"}).Contains(
                    f[5]))
            throw new Issue(Code.SavedStateError);
        var old = f[6].Length == 0 ? new string[0] : f[6].Split(',');
        if (old.Length > 64 || old.Any(x => !Catalog.IPv4(x)) || old.Distinct().Count() != old.Length)
            throw new Issue(Code.SavedStateError);
        return new Session {Token = t, Adapter = g, Provider = Catalog.Find(f[3]), Reverse = f[4] == "1", Phase = f[5], Original = old};
    }
}
internal interface ISettings
{
    Session Load();
    void Save(Session s);
    void Clear();
    string[] Read(Guid adapter);
    void Set(Guid adapter, string[] dns);
    bool Verify(Guid adapter, string[] dns);
}
internal static class Change
{
    internal static Code Restore(ISettings io, Guid token, bool confirmed)
    {
        var s = io.Load();
        if (s == null || s.Token != token)
            return Code.NoSession;
        var now = io.Read(s.Adapter);
        if (now.SequenceEqual(s.Original)) {
            // A matching registry value alone is not enough to discard a manual baseline.
            if (!io.Verify(s.Adapter, s.Original))
                return Code.Recover;
            io.Clear();
            return Code.Ok;
        }
        // Unknown external changes are never silently discarded, including after a crash.
        if (!now.SequenceEqual(s.Target) && !confirmed)
            return Code.OutsideChange;
        s.Phase = "restoring";
        io.Save(s);
        try {
            io.Set(s.Adapter, s.Original);
            if (!io.Verify(s.Adapter, s.Original))
                return Code.Recover;
            io.Clear();
            return Code.Ok;
        } catch {
            return Code.Recover;
        }
    }
}
}
