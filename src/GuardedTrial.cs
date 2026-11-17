using System;
using System.IO;
using System.Linq;

namespace Nabz
{
internal enum TrialChoice { Undo, Keep, Expired, Unhealthy }
internal sealed class TrialGate
{
    internal const int Seconds = 60;
    internal int Good, Bad;
    double lastSuccess = -100;
    internal void Observe(bool healthy, double elapsed)
    {
        if (healthy) {
            Good++;
            Bad = 0;
            lastSuccess = elapsed;
        } else {
            Good = 0;
            Bad++;
        }
    }
    internal bool CanKeep(double now)
    {
        return Good >= 2 && Bad == 0 && now >= lastSuccess && now - lastSuccess <= 12 && now < Seconds;
    }
    internal bool MustUndo(double now)
    {
        return Bad >= 2 || now >= Seconds;
    }
}
internal static class GuardedTrial
{
    // The privileged worker owns one global mutation mutex for staging, confirmation and rollback.
    // Before = state immediately preceding this attempt. Original = before the first app change.
    internal static Code Run(ISettings io, Guid id, Provider p, bool reverse, Guid? oldToken, Func<TrialChoice> decide,
                             string[] expectedBefore = null)
    {
        var old = io.Load();
        if (!oldToken.HasValue && old != null)return Code.HasSession;
        if (oldToken.HasValue && (old == null || old.Token != oldToken.Value || old.Adapter != id))return Code.NoSession;
        var before = io.Read(id);
        if (expectedBefore != null && !before.SequenceEqual(expectedBefore))return Code.Stale;
        if (old != null && (old.Phase != "active" || !before.SequenceEqual(old.Target) ||
                            !io.Verify(id, old.Target)))return Code.OutsideChange;
        if (!io.Verify(id, before))return Code.UnsafeConfig;
        var target = p.Pair(reverse);
        if (before.SequenceEqual(target))return Code.Unchanged;
        var pending = new Session {Adapter = id, Provider = p, Reverse = reverse, Token = Guid.NewGuid(), Original = old == null ? before : old.Original, Phase = "pending"};
        io.Save(pending);
        bool staged = false;
        Code outcome = Code.RolledBack;
        try {
            var owner = io.Load();
            if (owner == null || owner.Token != pending.Token)return Code.NoSession;
            if (!io.Read(id).SequenceEqual(before))return Code.OutsideChange;
            io.Set(id, target);
            if (!io.Verify(id, target))throw new IOException("DNS read-back did not match");
            staged = true;
            var choice = decide();
            var journal = io.Load();
            if (journal == null || journal.Token != pending.Token)return Code.NoSession;
            var current = io.Read(id);
            // The user, VPN or administrator may have made a newer change while the dialog was open.
            if (!current.SequenceEqual(target) && !current.SequenceEqual(before))return Code.OutsideChange;
            if (choice == TrialChoice.Keep && current.SequenceEqual(target) && io.Verify(id, target)) {
                pending.Phase = "active";
                io.Save(pending);
                return Code.Ok;
            }
            if (choice == TrialChoice.Undo)outcome = Code.TrialUndone;
            else if (choice == TrialChoice.Expired)outcome = Code.TrialExpired;
            else if (choice == TrialChoice.Unhealthy)outcome = Code.TrialUnhealthy;
        } catch {
            outcome = Code.RolledBack;
        }
        try {
            var journal = io.Load();
            if (journal == null || journal.Token != pending.Token)return Code.NoSession;
            var current = io.Read(id);
            bool ownedPartial = !staged && current.Length > 0 && current.Length < target.Length &&
                                current.SequenceEqual(target.Take(current.Length));
            if (!current.SequenceEqual(target) && !current.SequenceEqual(before) && !ownedPartial)return Code.OutsideChange;
            if (!io.Verify(id, before)) {
                io.Set(id, before);
                if (!io.Verify(id, before))return Code.Recover;
            }
            if (old == null)io.Clear();
            else io.Save(old);
            return outcome;
        } catch {
            return Code.Recover;
        }
    }
}
}
