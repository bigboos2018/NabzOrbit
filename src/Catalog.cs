using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Nabz
{
internal sealed class Provider
{
    internal readonly string Id, Name, A, B, Note;
    internal Provider(string id, string name, string a, string b, string note)
    {
        Id = id;
        Name = name;
        A = a;
        B = b;
        Note = note;
    }
    internal bool Regional
    {
        get {
            return Id == "radar" || Id == "ir403";
        }
    }
    internal string[] Pair(bool reverse)
    {
        return reverse ? new[] {B, A} : new[] {A, B};
    }
}
internal static class Catalog
{
    internal static readonly Provider[] All = Read();
    internal static bool IPv4(string s)
    {
        IPAddress ip;
        return s != null && IPAddress.TryParse(s, out ip) && ip.AddressFamily == AddressFamily.InterNetwork
               && ip.ToString() == s;
    }
    static Provider[] Read()
    {
        var list = new List<Provider>();
        using (var r = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Nabz.Catalog"),
                                        Encoding.UTF8)) {
            string line;
            while ((line = r.ReadLine()) != null) {
                if (line.Length == 0)
                    continue;
                var x = line.Split('\t');
                if (x.Length != 5 || !IPv4(x[2]) || !IPv4(x[3]) || x[2] == x[3] || x[0].Length == 0 || !x[0].All(c => (c >= 'a'
                        && c <= 'z') || (c >= '0' && c <= '9')) || list.Any(p => p.Id == x[0]))
                    throw new InvalidDataException("Invalid embedded catalog.");
                if (!ApprovedAddresses(x[0], x[2], x[3])) throw new InvalidDataException("Not an approved resolver pair.");
                list.Add(new Provider(x[0], x[1], x[2], x[3], x[4]));
            }
        }
        if (list.Count != 14 || list.SelectMany(p => p.Pair(false)).Distinct().Count() != 28)
            throw new InvalidDataException("Incomplete catalog.");
        return list.ToArray();
    }
    // No runtime downloads, IP range discovery or user-supplied private destinations.
    internal static bool ApprovedAddresses(string id, string a, string b)
    {
        if (id == "radar") return a == "10.202.10.10" && b == "10.202.10.11";
        if (id == "ir403") return a == "10.202.10.202" && b == "10.202.10.102";
        return IPv4(a) && IPv4(b) && NetPath.Public(IPAddress.Parse(a)) && NetPath.Public(IPAddress.Parse(b));
    }
    internal static Provider[] Available(bool regional)
    {
        return All.Where(p => regional || !p.Regional).ToArray();
    }
    internal static bool IsRegionalAddress(string ip)
    {
        return All.Any(p => p.Regional && (p.A == ip || p.B == ip));
    }
    internal static Provider Find(string id)
    {
        return All.FirstOrDefault(p => p.Id == id);
    }
}
}
