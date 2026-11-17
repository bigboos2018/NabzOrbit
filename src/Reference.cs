using System;
using System.Collections.Generic;
using System.Linq;

namespace Nabz
{
// A measured, read-only reference. It may consist of just one router/ISP address.
// It is never serialized into a journal and is never a target for a write.
internal sealed class Reference
{
    internal readonly string[] Addresses;
    internal readonly bool Partial;
    internal Trace[] Items = new Trace[0];
    internal Reference(IEnumerable<string> effective, bool iran)
    {
        var slots = (effective ?? new string[0]).Where(Catalog.IPv4).Distinct().Take(2).ToArray();
        Addresses = slots.Where(ip => iran || !Catalog.IsRegionalAddress(ip)).ToArray();
        Partial = Addresses.Length != slots.Length;
    }
    internal void BindTo(IDictionary<string, Trace> pool)
    {
        Items = Addresses.Select(ip => {
            Trace t;
            if (!pool.TryGetValue(ip, out t))pool[ip] = t = new Trace(ip);
            return t;
        }).ToArray();
    }
    internal bool Passed
    {
        get {
            return !Partial && Items.Length > 0 && Items.All(t => t.Eligible);
        }
    }
    internal int Good
    {
        get {
            return Items.Sum(t => t.Good);
        }
    }
    internal double P90
    {
        get {
            return Survey.P90(Items.SelectMany(t => t.Samples));
        }
    }
    internal bool PreferCurrent(Finding challenger)
    {
        if (!Passed)return false;
        if (challenger == null || !challenger.Eligible)return true;
        if (Items.Length == 2 &&
                Items.Select(t => t.Ip).OrderBy(x => x).SequenceEqual(challenger.P.Pair(false).OrderBy(x => x)))return true;
        int referenceFloor = Items.Min(t => t.Good), challengerFloor = Math.Min(challenger.A.Good, challenger.B.Good);
        if (referenceFloor != challengerFloor)return referenceFloor > challengerFloor;
        int refNormalized = 2 * Good, candidateNormalized = challenger.Good * Items.Length;
        if (refNormalized != candidateNormalized)return refNormalized > candidateNormalized;
        double gain = P90 - challenger.P90;
        // Do not pin DHCP to a public pair for a negligible or noisy latency change.
        return !(gain >= 15 && gain >= P90 * 0.20);
    }
}
}
