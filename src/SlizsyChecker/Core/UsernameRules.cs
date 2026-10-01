namespace SlizsyChecker;

public sealed record CustomLoad(List<string> Names, int Invalid, int Skipped);

public static class UsernameRules
{
    public static readonly Dictionary<string, string> Charsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lower"] = "abcdefghijklmnopqrstuvwxyz",
        ["digits"] = "0123456789",
        ["lowernum"] = "abcdefghijklmnopqrstuvwxyz0123456789",
        ["all"] = "abcdefghijklmnopqrstuvwxyz0123456789_."
    };

    public static readonly string[] CharsetNames = { "lower", "digits", "lowernum", "all" };

    public static bool IsValid(string name)
    {
        if (name.Length < 2 || name.Length > 32) return false;
        char prev = '\0';
        foreach (var ch in name)
        {
            bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '.';
            if (!ok) return false;
            if (ch == '.' && prev == '.') return false;
            prev = ch;
        }
        return true;
    }

    public static List<string> Generate(UsernamesCfg cfg, Func<string, bool> skip)
    {
        string chars = Charsets.TryGetValue(cfg.Charset, out var set) ? set : Charsets["lowernum"];
        int length = Math.Clamp(cfg.Length, 2, 32);
        int want = Math.Max(0, cfg.Amount);
        double space = Math.Pow(chars.Length, length);

        if (space <= 1_000_000)
        {
            int total = (int)space;
            var all = new List<string>(total);
            var buffer = new char[length];
            for (int i = 0; i < total; i++)
            {
                int n = i;
                for (int p = length - 1; p >= 0; p--)
                {
                    buffer[p] = chars[n % chars.Length];
                    n /= chars.Length;
                }
                var candidate = new string(buffer);
                if (IsValid(candidate) && !skip(candidate)) all.Add(candidate);
            }
            Shuffle(all);
            if (all.Count > want) all.RemoveRange(want, all.Count - want);
            return all;
        }

        var result = new List<string>(want);
        var seen = new HashSet<string>();
        var buf = new char[length];
        long maxTries = (long)want * 20 + 1000;
        for (long t = 0; t < maxTries && result.Count < want; t++)
        {
            for (int p = 0; p < length; p++) buf[p] = chars[Random.Shared.Next(chars.Length)];
            var candidate = new string(buf);
            if (!IsValid(candidate) || skip(candidate) || !seen.Add(candidate)) continue;
            result.Add(candidate);
        }
        return result;
    }

    public static CustomLoad LoadCustom(Func<string, bool> skip)
    {
        var names = new List<string>();
        int invalid = 0, skipped = 0;
        try
        {
            if (!File.Exists(Paths.Usernames))
            {
                File.WriteAllText(Paths.Usernames, "");
                return new CustomLoad(names, 0, 0);
            }
            var seen = new HashSet<string>();
            foreach (var raw in File.ReadLines(Paths.Usernames))
            {
                var name = raw.Trim().ToLowerInvariant();
                if (name.Length == 0) continue;
                if (!IsValid(name)) { invalid++; continue; }
                if (!seen.Add(name)) continue;
                if (skip(name)) { skipped++; continue; }
                names.Add(name);
            }
        }
        catch { }
        return new CustomLoad(names, invalid, skipped);
    }

    public static (List<string> Names, int Invalid) Parse(IEnumerable<string> lines)
    {
        var names = new List<string>();
        var seen = new HashSet<string>();
        int invalid = 0;
        foreach (var raw in lines)
        {
            var name = raw.Trim().ToLowerInvariant();
            if (name.Length == 0) continue;
            if (!IsValid(name)) { invalid++; continue; }
            if (seen.Add(name)) names.Add(name);
        }
        return (names, invalid);
    }

    private static void Shuffle(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
