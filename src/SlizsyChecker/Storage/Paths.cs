namespace SlizsyChecker;

public static class Paths
{
    public static readonly string Root = ResolveRoot();
    public static readonly string Data = Path.Combine(Root, "data");
    public static readonly string Results = Path.Combine(Root, "results");
    public static readonly string Logs = Path.Combine(Root, "logs");
    public static readonly string Config = Path.Combine(Data, "config.json");
    public static readonly string Proxies = Path.Combine(Data, "proxies.txt");
    public static readonly string Usernames = Path.Combine(Data, "usernames.txt");
    public static readonly string Blacklist = Path.Combine(Data, "blacklist.txt");
    public static readonly string Valids = Path.Combine(Data, "valids.txt");
    public static readonly string Stats = Path.Combine(Data, "stats.json");

    private static string ResolveRoot()
    {
        var process = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(process))
        {
            var name = Path.GetFileNameWithoutExtension(process);
            var dir = Path.GetDirectoryName(process);
            if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(dir))
                return dir;
        }
        return AppContext.BaseDirectory;
    }

    public static void EnsureAll()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Results);
        Directory.CreateDirectory(Logs);
    }
}
