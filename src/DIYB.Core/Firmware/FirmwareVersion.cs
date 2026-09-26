using System.Globalization;

namespace DIYB.Core.Firmware;

/// <summary>Comparaison de versions de firmware. Les modules annoncent des chaînes
/// du type « 3.7.2 », parfois suffixées ; les segments non numériques sont comparés
/// en ordinal après les segments numériques.</summary>
public readonly struct FirmwareVersion : IComparable<FirmwareVersion>, IEquatable<FirmwareVersion>
{
    private readonly int[] _numbers;
    private readonly string _suffix;

    private FirmwareVersion(int[] numbers, string suffix, string raw)
    {
        _numbers = numbers;
        _suffix = suffix;
        Raw = raw;
    }

    public string Raw { get; }

    public bool IsEmpty => _numbers is null || _numbers.Length == 0;

    public static FirmwareVersion Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new FirmwareVersion(Array.Empty<int>(), string.Empty, string.Empty);

        var trimmed = value.Trim().TrimStart('v', 'V');
        var separator = trimmed.IndexOfAny(new[] { '-', '+', '_', ' ' });
        var head = separator < 0 ? trimmed : trimmed[..separator];
        var suffix = separator < 0 ? string.Empty : trimmed[(separator + 1)..];

        var numbers = new List<int>();
        foreach (var part in head.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                numbers.Add(number);
            else
                break;
        }

        return new FirmwareVersion(numbers.ToArray(), suffix, value.Trim());
    }

    public int CompareTo(FirmwareVersion other)
    {
        var left = _numbers ?? Array.Empty<int>();
        var right = other._numbers ?? Array.Empty<int>();

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;
            if (a != b)
                return a.CompareTo(b);
        }

        // Une version sans suffixe est considérée postérieure à sa préversion.
        var leftSuffix = _suffix ?? string.Empty;
        var rightSuffix = other._suffix ?? string.Empty;

        if (leftSuffix.Length == 0 && rightSuffix.Length > 0)
            return 1;

        if (leftSuffix.Length > 0 && rightSuffix.Length == 0)
            return -1;

        return string.CompareOrdinal(leftSuffix, rightSuffix);
    }

    public bool Equals(FirmwareVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is FirmwareVersion other && Equals(other);

    public override int GetHashCode() => Raw?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;

    public override string ToString() => Raw ?? string.Empty;

    public static bool operator <(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) >= 0;

    public static bool operator ==(FirmwareVersion left, FirmwareVersion right) => left.Equals(right);

    public static bool operator !=(FirmwareVersion left, FirmwareVersion right) => !left.Equals(right);
}
