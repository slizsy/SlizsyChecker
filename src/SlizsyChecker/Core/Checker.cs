using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SlizsyChecker;

public enum Verdict { Available, Taken, Invalid, Error }

public readonly record struct Outcome(Verdict Verdict, string? Reason);

public readonly record struct Probe(bool Ok, string Detail, long Ms);

public sealed class Checker : IDisposable
{
    private const string Endpoint = "https://discord.com/api/v9/unique-username/username-attempt-unauthed";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36";

    private readonly AppConfig _cfg;
    private readonly ProxyPool _pool;
    private readonly HttpClient _direct;
    private long _directUntil;

    public Checker(AppConfig cfg, ProxyPool pool)
    {
        _cfg = cfg;
        _pool = pool;
        _direct = new HttpClient { Timeout = TimeSpan.FromSeconds(cfg.Timeout) };
    }

    public async Task<Outcome> CheckAsync(string username, CancellationToken ct)
    {
        int attempts = _cfg.Retry.Enabled ? Math.Max(1, _cfg.Retry.MaxAttempts) : 1;
        string body = JsonSerializer.Serialize(new { username });
        string? reason = null;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var node = _pool.Pick();
            if (node == null) await WaitDirect(ct);
            var client = node?.GetClient(_cfg.Timeout) ?? _direct;

            try
            {
                using var req = Build(body);
                using var res = await client.SendAsync(req, ct);
                int code = (int)res.StatusCode;

                if (code == 429)
                {
                    reason = "rate limited";
                    var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(2 * attempt, 10));
                    if (wait > TimeSpan.FromSeconds(30)) wait = TimeSpan.FromSeconds(30);
                    if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
                    if (node != null) _pool.Cool(node, (int)wait.TotalMilliseconds);
                    else Interlocked.Exchange(ref _directUntil, Environment.TickCount64 + (long)wait.TotalMilliseconds);
                    continue;
                }

                if (code == 400) return new Outcome(Verdict.Invalid, null);

                if (code == 407 || code >= 500 || !res.IsSuccessStatusCode)
                {
                    reason = code == 407 ? "proxy login rejected" : code >= 500 ? "discord server error" : "http " + code;
                    if (node != null) _pool.Fail(node);
                    await Backoff(attempt, attempts, ct);
                    continue;
                }

                string text = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("taken", out var taken) &&
                    (taken.ValueKind == JsonValueKind.True || taken.ValueKind == JsonValueKind.False))
                {
                    if (node != null) _pool.Ok(node);
                    return new Outcome(taken.GetBoolean() ? Verdict.Taken : Verdict.Available, null);
                }

                reason = "bad response";
                if (node != null) _pool.Fail(node);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                reason = Describe(ex);
                if (node != null) _pool.Fail(node);
                await Backoff(attempt, attempts, ct);
            }
        }

        return new Outcome(Verdict.Error, reason ?? "no response");
    }

    public static async Task<Probe> ProbeAsync(HttpClient client, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(10));
            string body = JsonSerializer.Serialize(new { username = "probe" + Random.Shared.Next(100000, 999999) });
            using var req = Build(body);
            using var res = await client.SendAsync(req, limit.Token);
            int code = (int)res.StatusCode;
            if (code == 200 || code == 400) return new Probe(true, "ok", watch.ElapsedMilliseconds);
            if (code == 429) return new Probe(true, "ok (rate limited)", watch.ElapsedMilliseconds);
            if (code == 407) return new Probe(false, "proxy login rejected", watch.ElapsedMilliseconds);
            return new Probe(false, "http " + code, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new Probe(false, Describe(ex), watch.ElapsedMilliseconds);
        }
    }

    public Task<Probe> ProbeNodeAsync(ProxyNode? node, CancellationToken ct)
    {
        return ProbeAsync(node?.GetClient(_cfg.Timeout) ?? _direct, ct);
    }

    private static HttpRequestMessage Build(string body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return req;
    }

    private static string Describe(Exception ex)
    {
        if (ex is OperationCanceledException) return "timeout";
        if (ex is HttpRequestException http)
        {
            if (http.StatusCode == HttpStatusCode.ProxyAuthenticationRequired) return "proxy login rejected";
            return http.HttpRequestError switch
            {
                HttpRequestError.ProxyTunnelError => "proxy refused the site",
                HttpRequestError.NameResolutionError => "dns error",
                HttpRequestError.ConnectionError => "connection failed",
                HttpRequestError.SecureConnectionError => "tls error",
                _ => "network error"
            };
        }
        if (ex is JsonException) return "bad response";
        return "error";
    }

    private static Task Backoff(int attempt, int attempts, CancellationToken ct)
    {
        return attempt < attempts ? Task.Delay(200 * attempt, ct) : Task.CompletedTask;
    }

    private async Task WaitDirect(CancellationToken ct)
    {
        long until = Interlocked.Read(ref _directUntil);
        long now = Environment.TickCount64;
        if (until > now)
            await Task.Delay((int)Math.Min(until - now, 30_000), ct);
    }

    public void Dispose() => _direct.Dispose();
}
