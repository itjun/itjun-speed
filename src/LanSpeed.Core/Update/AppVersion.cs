using System.Globalization;

namespace LanSpeed.Core.Update;

/// <summary>
/// 应用版本，只比较 major.minor.patch。
/// 忽略单个前缀 v/V、预发布后缀，以及 MSIX 的第四段。
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim();
        if (s.Length >= 2 && (s[0] == 'v' || s[0] == 'V') && char.IsDigit(s[1]))
        {
            s = s[1..];
        }

        int cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            s = s[..cut];
        }

        string[] parts = s.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        if (!TryPart(parts[0], out int major))
        {
            return false;
        }

        int minor = 0;
        int patch = 0;
        if (parts.Length >= 2 && !TryPart(parts[1], out minor))
        {
            return false;
        }

        if (parts.Length >= 3 && !TryPart(parts[2], out patch))
        {
            return false;
        }

        if (parts.Length == 4 && !TryPart(parts[3], out _))
        {
            return false;
        }

        version = new AppVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        int major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        int minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        return Patch.CompareTo(other.Patch);
    }

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static bool TryPart(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
