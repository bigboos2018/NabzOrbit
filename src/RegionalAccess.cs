using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Nabz
{
internal static class RegionalAccess
{
    [DllImport("iphlpapi.dll")]static extern uint GetBestInterface(uint address, out uint index);
    internal static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
    {
        if (a == null || b == null || mask == null)return true;
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        var m = mask.GetAddressBytes();
        if (x.Length != 4 || y.Length != 4 || m.Length != 4 || m.All(v => v == 0))return true;
        for (int i = 0; i < 4; i++)if ((x[i]&m[i]) != (y[i]&m[i]))return false;
        return true;
    }
    // These four exact RFC1918 destinations are optional. Refuse on-link collisions,
    // wrong outgoing route, tunnel/PPP links or an unverifiable network inventory.
    internal static bool Allowed(Adapter a)
    {
        if (!Adapters.IsWindows || a == null || !a.Usable)return false;
        try {
            var up = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
                     n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
            if (up.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                       n.NetworkInterfaceType == NetworkInterfaceType.Ppp))return false;
            var nets = up.SelectMany(n => n.GetIPProperties().UnicastAddresses.Cast<UnicastIPAddressInformation>()).Where(
                           u => u.Address.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (!nets.Any(u => u.Address.Equals(a.Local)))return false;
            foreach (var ip in Catalog.All.Where(p => p.Regional).SelectMany(p => p.Pair(false)).Select(IPAddress.Parse)) {
                uint route;
                if (GetBestInterface(BitConverter.ToUInt32(ip.GetAddressBytes(), 0), out route) != 0 || route != (uint)a.Index)return false;
                if (nets.Any(u => SameSubnet(ip, u.Address, u.IPv4Mask)))return false;
            }
            return true;
        } catch {
            return false;
        }
    }
}
}
