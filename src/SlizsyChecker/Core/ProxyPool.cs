using System.Net;

namespace SlizsyChecker;

public sealed class ProxyNode
{
    private HttpClient? _client;
    private readonly object _lock = new();

    public int Fails;
    public long CooldownUntil;

    public ProxyNode(string url)
    {
        Url = url;
    }

    public string Url { get; }

    public string Display
    {
        get
        {
            try
            {
                var uri = new Uri(Url);
                return uri.Host + ":" + uri.Port;
            }
            catch { return "proxy"; }
        }
    }

    public HttpClient GetClient(int timeoutSeconds)
    {
        lock (_lock)
        {
            if (_client != null) return _client;
            var uri = new Uri(Url);
            var proxy = new WebProxy(new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}"));
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                proxy.Credentials = new NetworkCredential(
                    Uri.UnescapeDataString(parts[0]),
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
            }
            var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            return _client;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _client?.Dispose();
            _client = null;
        }
    }
}

public sealed class ProxyPool
{
    private static readonly HashSet<string> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "socks4", "socks4a", "socks5", "socks5h"
    };

    private readonly List<ProxyNode> _nodes = new();

    public int Count => _nodes.Count;
    public IReadOnlyList<ProxyNode> Nodes => _nodes;

    public int Ready()
    {
        long now = Environment.TickCount64;
        return _nodes.Count(n => Volatile.Read(ref n.CooldownUntil) <= now);
    }

    public int Load()
    {
        foreach (var node in _nodes) node.Reset();
        _nodes.Clear();
        try
        {
            if (!File.Exists(Paths.Proxies))
            {
                File.WriteAllText(Paths.Proxies, "");
                return 0;
            }
            var seen = new HashSet<string>();
            foreach (var raw in File.ReadLines(Paths.Proxies))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var url = Normalize(line);
                if (url != null && seen.Add(url)) _nodes.Add(new ProxyNode(url));
            }
        }
        catch { }
        return _nodes.Count;
    }

    public void ResetClients()
    {
        foreach (var node in _nodes) node.Reset();
    }

    public static string? Normalize(string line)
    {
        line = line.Trim();
        if (!line.Contains("://") && line.Contains('@')) line = "http://" + line;

        if (line.Contains("://"))
        {
            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri)) return null;
            if (!Schemes.Contains(uri.Scheme) || string.IsNullOrEmpty(uri.Host) || uri.Port <= 0) return null;
            return line;
        }

        var p = line.Split(':');
        if (p.Length == 2 && IsPort(p[1]) && p[0].Length > 0)
            return $"http://{p[0]}:{p[1]}";
        if (p.Length == 4 && IsPort(p[1]) && p[0].Length > 0)
            return $"http://{Uri.EscapeDataString(p[2])}:{Uri.EscapeDataString(p[3])}@{p[0]}:{p[1]}";
        return null;
    }

    private static bool IsPort(string s) => int.TryParse(s, out var port) && port > 0 && port <= 65535;

    public ProxyNode? Pick()
    {
        int n = _nodes.Count;
        if (n == 0) return null;
        long now = Environment.TickCount64;
        for (int i = 0; i < 8; i++)
        {
            var node = _nodes[Random.Shared.Next(n)];
            if (Volatile.Read(ref node.CooldownUntil) <= now) return node;
        }
        return _nodes[Random.Shared.Next(n)];
    }

    public void Ok(ProxyNode node) => Interlocked.Exchange(ref node.Fails, 0);

    public void Fail(ProxyNode node)
    {
        if (Interlocked.Increment(ref node.Fails) >= 3)
        {
            Interlocked.Exchange(ref node.Fails, 0);
            Volatile.Write(ref node.CooldownUntil, Environment.TickCount64 + 30_000);
        }
    }

    public void Cool(ProxyNode node, int milliseconds)
    {
        Volatile.Write(ref node.CooldownUntil, Environment.TickCount64 + milliseconds);
    }
}
