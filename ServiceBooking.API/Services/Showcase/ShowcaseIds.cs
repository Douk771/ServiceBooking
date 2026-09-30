using System.Security.Cryptography;
using System.Text;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.2, §575.4 — stable ids: UUIDv5 (RFC 4122, SHA-1) of a key inside a fixed namespace, for example
/// <c>prod:company:primer-lavanda</c>. The same key gives the same id on every machine and after every re-seed, so links, caches and the demo roles' tokens survive it.
/// </summary>
public static class ShowcaseIds
{
    public static Guid For(string profile, string kind, string key) =>
        V5(ShowcaseCatalog.IdNamespace, $"{profile}:{kind}:{key}");

    public static Guid V5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes); // .NET stores the first three fields little-endian; the RFC hashes them big-endian
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(data, 0);
        nameBytes.CopyTo(data, namespaceBytes.Length);

        var hash = SHA1.HashData(data);
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        SwapByteOrder(bytes);
        return new Guid(bytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
