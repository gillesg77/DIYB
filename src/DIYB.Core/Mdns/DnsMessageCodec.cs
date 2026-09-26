using System.Net;
using System.Text;

namespace DIYB.Core.Mdns;

/// <summary>Lecture et écriture des messages DNS échangés en multicast.</summary>
public static class DnsMessageCodec
{
    private const int HeaderLength = 12;
    private const ushort ClassIn = 1;
    private const ushort CacheFlushMask = 0x8000;

    public static DnsMessage Read(byte[] buffer, int length)
    {
        if (length < HeaderLength)
            throw new FormatException("Message DNS tronqué.");

        var cursor = new Cursor(buffer, length);

        var id = cursor.ReadUInt16();
        var flags = cursor.ReadUInt16();
        var questionCount = cursor.ReadUInt16();
        var answerCount = cursor.ReadUInt16();
        var authorityCount = cursor.ReadUInt16();
        var additionalCount = cursor.ReadUInt16();

        var questions = new DnsQuestion[questionCount];
        for (var i = 0; i < questionCount; i++)
        {
            questions[i] = new DnsQuestion
            {
                Name = cursor.ReadName(),
                Type = cursor.ReadUInt16(),
                Class = cursor.ReadUInt16(),
            };
        }

        return new DnsMessage
        {
            Id = id,
            Flags = flags,
            Questions = questions,
            Answers = ReadRecords(ref cursor, answerCount),
            Authorities = ReadRecords(ref cursor, authorityCount),
            Additionals = ReadRecords(ref cursor, additionalCount),
        };
    }

    /// <summary>Requête PTR pour un type de service, envoyée avec réponse multicast
    /// afin que les annonces spontanées suivantes soient également reçues.</summary>
    public static byte[] BuildQuery(string serviceName, ushort type = DnsType.Ptr)
    {
        var payload = new List<byte>(64);

        payload.AddRange(new byte[] { 0, 0 });       // ID nul, imposé par la RFC 6762.
        payload.AddRange(new byte[] { 0, 0 });       // Drapeaux : requête standard.
        payload.AddRange(BigEndian(1));              // QDCOUNT
        payload.AddRange(BigEndian(0));              // ANCOUNT
        payload.AddRange(BigEndian(0));              // NSCOUNT
        payload.AddRange(BigEndian(0));              // ARCOUNT

        foreach (var label in serviceName.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length > 63)
                throw new ArgumentException("Étiquette DNS de plus de 63 octets.", nameof(serviceName));

            payload.Add((byte)bytes.Length);
            payload.AddRange(bytes);
        }

        payload.Add(0);
        payload.AddRange(BigEndian(type));
        payload.AddRange(BigEndian(ClassIn));

        return payload.ToArray();
    }

    private static DnsRecord[] ReadRecords(ref Cursor cursor, int count)
    {
        var records = new List<DnsRecord>(count);
        for (var i = 0; i < count; i++)
        {
            if (!cursor.HasMore)
                break;

            records.Add(ReadRecord(ref cursor));
        }

        return records.ToArray();
    }

    private static DnsRecord ReadRecord(ref Cursor cursor)
    {
        var name = cursor.ReadName();
        var type = cursor.ReadUInt16();
        var rawClass = cursor.ReadUInt16();
        var ttl = cursor.ReadUInt32();
        var dataLength = cursor.ReadUInt16();
        var end = cursor.Position + dataLength;

        var cls = (ushort)(rawClass & ~CacheFlushMask);
        DnsRecord record;

        switch (type)
        {
            case DnsType.Ptr:
                record = new PtrRecord { Name = name, Type = type, Class = cls, Ttl = ttl, Target = cursor.ReadName() };
                break;

            case DnsType.Srv:
                record = new SrvRecord
                {
                    Name = name,
                    Type = type,
                    Class = cls,
                    Ttl = ttl,
                    Priority = cursor.ReadUInt16(),
                    Weight = cursor.ReadUInt16(),
                    Port = cursor.ReadUInt16(),
                    Target = cursor.ReadName(),
                };
                break;

            case DnsType.Txt:
                record = new TxtRecord { Name = name, Type = type, Class = cls, Ttl = ttl, Strings = cursor.ReadTextStrings(end) };
                break;

            case DnsType.A when dataLength == 4:
            case DnsType.Aaaa when dataLength == 16:
                record = new AddressRecord
                {
                    Name = name,
                    Type = type,
                    Class = cls,
                    Ttl = ttl,
                    Address = new IPAddress(cursor.ReadBytes(dataLength)),
                };
                break;

            default:
                record = new UnknownRecord { Name = name, Type = type, Class = cls, Ttl = ttl, Data = cursor.ReadBytes(dataLength) };
                break;
        }

        // Les enregistrements composés peuvent être plus courts que RDLENGTH annoncé.
        cursor.Position = end;
        return record;
    }

    private static byte[] BigEndian(ushort value) => new[] { (byte)(value >> 8), (byte)(value & 0xFF) };

    private struct Cursor
    {
        private readonly byte[] _data;
        private readonly int _length;

        public Cursor(byte[] data, int length)
        {
            _data = data;
            _length = length;
            Position = 0;
        }

        public int Position { get; set; }

        public bool HasMore => Position < _length;

        public ushort ReadUInt16()
        {
            Require(2);
            var value = (ushort)((_data[Position] << 8) | _data[Position + 1]);
            Position += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            Require(4);
            var value = ((uint)_data[Position] << 24) | ((uint)_data[Position + 1] << 16)
                | ((uint)_data[Position + 2] << 8) | _data[Position + 3];
            Position += 4;
            return value;
        }

        public byte[] ReadBytes(int count)
        {
            Require(count);
            var bytes = new byte[count];
            Array.Copy(_data, Position, bytes, 0, count);
            Position += count;
            return bytes;
        }

        public IReadOnlyList<string> ReadTextStrings(int end)
        {
            var strings = new List<string>();
            while (Position < end && Position < _length)
            {
                int length = _data[Position++];
                if (length == 0 || Position + length > end)
                    break;

                strings.Add(Encoding.UTF8.GetString(_data, Position, length));
                Position += length;
            }

            return strings;
        }

        /// <summary>Lit un nom, en suivant les pointeurs de compression. Le nombre de
        /// sauts est borné : un message malformé peut boucler sur lui-même.</summary>
        public string ReadName()
        {
            var labels = new List<string>();
            var position = Position;
            var jumped = false;
            var hops = 0;

            while (true)
            {
                if (position >= _length)
                    throw new FormatException("Nom DNS tronqué.");

                int length = _data[position];
                if (length == 0)
                {
                    position++;
                    break;
                }

                if ((length & 0xC0) == 0xC0)
                {
                    if (position + 1 >= _length)
                        throw new FormatException("Pointeur de compression tronqué.");

                    var target = ((length & 0x3F) << 8) | _data[position + 1];
                    if (!jumped)
                    {
                        Position = position + 2;
                        jumped = true;
                    }

                    if (++hops > 32)
                        throw new FormatException("Boucle de compression DNS.");

                    position = target;
                    continue;
                }

                position++;
                if (position + length > _length)
                    throw new FormatException("Étiquette DNS tronquée.");

                labels.Add(Encoding.UTF8.GetString(_data, position, length));
                position += length;
            }

            if (!jumped)
                Position = position;

            return string.Join('.', labels);
        }

        private readonly void Require(int count)
        {
            if (Position + count > _length)
                throw new FormatException("Message DNS tronqué.");
        }
    }
}
