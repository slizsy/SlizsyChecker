namespace SlizsyChecker;

public static class Input
{
    public static string? Ask(string label)
    {
        Term.Text(label, Tones.Cyan);
        return Console.ReadLine()?.Trim();
    }

    public static void Pause()
    {
        Term.Blank();
        Term.Text("  Press Enter to continue", Tones.Gray);
        Console.ReadLine();
    }

    public static List<string> Block()
    {
        var lines = new List<string>();
        while (true)
        {
            var line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) break;
            lines.Add(line);
        }
        return lines;
    }

    public static int Int(string label, int current, int min, int max)
    {
        var s = Ask($"  {label} [{current}]: ");
        if (string.IsNullOrEmpty(s)) return current;
        if (int.TryParse(s, out var n) && n >= min && n <= max) return n;
        Term.Line(("  Use a number from ", Tones.Yellow), ($"{min} to {max}", Tones.White));
        return current;
    }

    public static bool Bool(string label, bool current)
    {
        var s = Ask($"  {label} (y/n) [{(current ? "y" : "n")}]: ")?.ToLowerInvariant();
        return s switch
        {
            "y" or "yes" => true,
            "n" or "no" => false,
            _ => current
        };
    }

    public static string Choice(string label, string current, IReadOnlyList<string> options)
    {
        var s = Ask($"  {label} [{current}] ({string.Join("/", options)}): ")?.ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return current;
        if (options.Contains(s)) return s;
        Term.Line(("  Pick one of: ", Tones.Yellow), (string.Join(", ", options), Tones.White));
        return current;
    }
}
