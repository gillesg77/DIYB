using System.Net;
using System.Text;

namespace DIYB.Core.Tests;

/// <summary>Encode des réponses mDNS pour les tests, sans compression de noms.</summary>
internal sealed class DnsMessageBuilder
{
    private readonly List<byte[]> _answers = new();

    public DnsMessageBuilder Ptr(string name, string target, uint ttl = 120) =>
        Add(name, 12, ttl, EncodeName(target));

    public DnsMessageBuilder Srv(string name, string target, ushort port, uint ttl = 120)
    {
        var data = new List<byte>();
        data.AddRange(UInt16(0));
        data.AddRange(UInt16(0));
        data.AddRange(UInt16(port));
        data.AddRange(EncodeName(target));
        return Add(name, 33, ttl, data.ToArray());
    }

    public DnsMessageBuilder Txt(string name, IEnumerable<string> strings, uint ttl = 120)
    {
        var data = new List<byte>();
        foreach (var entry in strings)
        {
            var bytes = Encoding.UTF8.GetBytes(entry);
            data.Add((byte)bytes.Length);
            data.AddRange(bytes);
        }

        return Add(name, 16, ttl, data.ToArray());
    }

    public DnsMessageBuilder A(string name, string address, uint ttl = 120) =>
        Add(name, 1, ttl, IPAddress.Parse(address).GetAddressBytes());

    public byte[] Build()
    {
        var message = new List<byte>();
        message.AddRange(UInt16(0));
        message.AddRange(UInt16(0x8400));
        message.AddRange(UInt16(0));
        message.AddRange(UInt16((ushort)_answers.Count));
        message.AddRange(UInt16(0));
        message.AddRange(UInt16(0));

        foreach (var answer in _answers)
            message.AddRange(answer);

        return message.ToArray();
    }

    private DnsMessageBuilder Add(string name, ushort type, uint ttl, byte[] data)
    {
        var record = new List<byte>();
        record.AddRange(EncodeName(name));
        record.AddRange(UInt16(type));
        record.AddRange(UInt16(1));
        record.AddRange(UInt32(ttl));
        record.AddRange(UInt16((ushort)data.Length));
        record.AddRange(data);
        _answers.Add(record.ToArray());
        return this;
    }

    private static byte[] EncodeName(string name)
    {
        var bytes = new List<byte>();
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var encoded = Encoding.UTF8.GetBytes(label);
            bytes.Add((byte)encoded.Length);
            bytes.AddRange(encoded);
        }

        bytes.Add(0);
        return bytes.ToArray();
    }

    private static byte[] UInt16(ushort value) => new[] { (byte)(value >> 8), (byte)(value & 0xFF) };

    private static byte[] UInt32(uint value) => new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value,
    };
}
