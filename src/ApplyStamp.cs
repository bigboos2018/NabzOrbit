using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nabz
{
// Equality/freshness token, not a credential. All mutable inputs are reread after UAC.
internal static class ApplyStamp
{
    internal static string For(Adapter a, string[] configured)
    {
        var value = String.Join("|", new[] {a.Id.ToString("D"), a.Index.ToString(CultureInfo.InvariantCulture), a.Local.ToString(), a.Gateway, String.Join(",", a.Effective ?? new string[0]), String.Join(",", configured)});
        using (var sha = SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.ASCII.GetBytes(value))).Replace("-", "");
    }
    internal static bool Valid(string hash, string ticks, Adapter a, string[] configured, DateTime now)
    {
        long n;
        if (a == null || !a.Usable || hash == null || hash.Length != 64 ||
                !Int64.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 0 || n > now.Ticks ||
                now.Ticks - n > TimeSpan.FromMinutes(5).Ticks)return false;
        return String.Equals(hash, For(a, configured), StringComparison.Ordinal);
    }
}
}
