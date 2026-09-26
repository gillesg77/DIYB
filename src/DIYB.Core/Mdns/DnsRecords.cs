using System.Net;

namespace DIYB.Core.Mdns;

public static class DnsType
{
    public const ushort A = 1;
    public const ushort Ptr = 12;
    public const ushort Txt = 16;
    public const ushort Aaaa = 28;
    public const ushort Srv = 33;
    public const ushort Any = 255;
}

public abstract record DnsRecord
{
    public required string Name { get; init; }

    public required ushort Type { get; init; }

    /// <summary>Classe amputée du bit de poids fort, réservé en mDNS au drapeau
    /// « cache-flush ».</summary>
    public required ushort Class { get; init; }

    public required uint Ttl { get; init; }

    /// <summary>TTL nul : l'émetteur annonce son départ.</summary>
    public bool IsGoodbye => Ttl == 0;
}

public sealed record PtrRecord : DnsRecord
{
    public required string Target { get; init; }
}

public sealed record SrvRecord : DnsRecord
{
    public required ushort Priority { get; init; }

    public required ushort Weight { get; init; }

    public required ushort Port { get; init; }

    public required string Target { get; init; }
}

public sealed record TxtRecord : DnsRecord
{
    /// <summary>Chaînes brutes du TXT, 255 octets au plus chacune.</summary>
    public required IReadOnlyList<string> Strings { get; init; }

    /// <summary>Découpe les chaînes « clé=valeur ». Une chaîne sans « = » devient une
    /// clé à valeur vide (RFC 6763).</summary>
    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Strings)
        {
            var separator = entry.IndexOf('=');
            if (separator < 0)
                map[entry] = string.Empty;
            else
                map[entry[..separator]] = entry[(separator + 1)..];
        }

        return map;
    }
}

public sealed record AddressRecord : DnsRecord
{
    public required IPAddress Address { get; init; }
}

/// <summary>Type non interprété.</summary>
public sealed record UnknownRecord : DnsRecord
{
    public required byte[] Data { get; init; }
}

public sealed record DnsQuestion
{
    public required string Name { get; init; }

    public required ushort Type { get; init; }

    public required ushort Class { get; init; }
}

public sealed record DnsMessage
{
    public required ushort Id { get; init; }

    public required ushort Flags { get; init; }

    public bool IsResponse => (Flags & 0x8000) != 0;

    public IReadOnlyList<DnsQuestion> Questions { get; init; } = Array.Empty<DnsQuestion>();

    public IReadOnlyList<DnsRecord> Answers { get; init; } = Array.Empty<DnsRecord>();

    public IReadOnlyList<DnsRecord> Authorities { get; init; } = Array.Empty<DnsRecord>();

    public IReadOnlyList<DnsRecord> Additionals { get; init; } = Array.Empty<DnsRecord>();

    /// <summary>Réponses et additionnels confondus : un répondeur mDNS place souvent
    /// SRV, TXT et A en section additionnelle.</summary>
    public IEnumerable<DnsRecord> AllRecords => Answers.Concat(Authorities).Concat(Additionals);
}
