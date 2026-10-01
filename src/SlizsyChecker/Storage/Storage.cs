using System.Text.Json;

namespace SlizsyChecker;

public sealed class Blacklist
{
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _set.Count; }
    }

    public void Load()
    {
        lock (_lock)
        {
            _set.Clear();
            try
            {
                if (!File.Exists(Paths.Blacklist)) return;
                foreach (var line in File.ReadLines(Paths.Blacklist))
                {
                    var t = line.Trim().ToLowerInvariant();
                    if (t.Length > 0) _set.Add(t);
                }
            }
            catch { }
        }
    }

    public bool Contains(string name)
    {
        lock (_lock) return _set.Contains(name);
    }

    public void Add(string name)
    {
        lock (_lock)
        {
            if (!_set.Add(name)) return;
            try { File.AppendAllText(Paths.Blacklist, name + Environment.NewLine); } catch { }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _set.Clear();
            try { if (File.Exists(Paths.Blacklist)) File.Delete(Paths.Blacklist); } catch { }
        }
    }
}

public sealed class SessionFiles : IDisposable
{
    private readonly StreamWriter _log;
    private readonly object _lock = new();

    public string Id { get; } = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    public string LogPath { get; }
    public string AvailablePath { get; }

    public SessionFiles()
    {
        LogPath = Path.Combine(Paths.Logs, $"session-{Id}.log");
        AvailablePath = Path.Combine(Paths.Results, $"available-{Id}.txt");
        _log = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    public void Log(string line)
    {
        lock (_lock)
        {
            try { _log.WriteLine(line); } catch { }
        }
    }

    public void AddAvailable(string name)
    {
        lock (_lock)
        {
            try
            {
                File.AppendAllText(AvailablePath, name + Environment.NewLine);
                File.AppendAllText(Paths.Valids, name + Environment.NewLine);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _log.Dispose(); } catch { }
        }
    }
}

public sealed class LifetimeStats
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public int Sessions { get; set; }
    public long Checked { get; set; }
    public long Available { get; set; }
    public long Taken { get; set; }
    public long Failed { get; set; }

    public static LifetimeStats Load()
    {
        try
        {
            if (File.Exists(Paths.Stats))
                return JsonSerializer.Deserialize<LifetimeStats>(File.ReadAllText(Paths.Stats)) ?? new LifetimeStats();
        }
        catch { }
        return new LifetimeStats();
    }

    public void Save()
    {
        try { File.WriteAllText(Paths.Stats, JsonSerializer.Serialize(this, Options)); } catch { }
    }
}
