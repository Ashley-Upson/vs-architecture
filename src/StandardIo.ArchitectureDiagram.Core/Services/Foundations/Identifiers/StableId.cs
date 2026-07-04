using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StandardIo.ArchitectureDiagram.Core.Models;

internal static class StableId
{
    public static string From(string prefix, string value)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
        var suffix = string.Concat(hash.Take(8).Select(b => b.ToString("x2")));
        return $"{prefix}_{suffix}";
    }
}
