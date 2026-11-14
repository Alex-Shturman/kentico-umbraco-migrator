using System.Security.Cryptography;
using System.Text;
using KenticoMigrator.Kentico;

namespace KenticoMigrator.Migration;

/// <summary>Stable item keys (Constitution III). Source GUIDs are reused; everything else is a UUID v5.</summary>
public static class Keys
{
    /// <summary>Fixed namespace for <see cref="Derived"/>; never change it, keys depend on it.</summary>
    public static readonly Guid Namespace = new("6f1b6d0e-6c2f-5a8e-9f41-6b2c0b7a4d11");

    public static Guid ForDocument(SourceDocument document) => document.NodeGuid;

    public static Guid ForAttachment(Guid attachmentGuid) => attachmentGuid;

    public static Guid ForMediaFile(Guid fileGuid) => fileGuid;

    /// <summary>UUID v5 (RFC 9562) over <c>"{kind}:{naturalKey}"</c> lowercased.</summary>
    public static Guid Derived(string kind, string naturalKey)
    {
        var name = Encoding.UTF8.GetBytes($"{kind}:{naturalKey}".ToLowerInvariant());
        var input = new byte[16 + name.Length];
        Namespace.TryWriteBytes(input, bigEndian: true, out _); // RFC byte order, not .NET mixed-endian
        name.CopyTo(input, 16);

        var hash = SHA1.HashData(input);
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant
        return new Guid(bytes, bigEndian: true);
    }
}
