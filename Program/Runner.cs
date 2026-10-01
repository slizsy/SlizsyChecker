using System.Collections.Concurrent;
using System.Diagnostics;

namespace SlizsyChecker;

public sealed class RunStats
{
    public int Total;
    public int Done;
    public int Available;
    public int Taken;
    public int Invalid;
    public int Failed;
    public readonly Stopwatch Clock = Stopwatch.StartNew();
}

public sealed class Runner : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly ProxyPool _pool;
    private readonly Blacklist _blacklist;
    private readonly Checker _checker;
    private readonly ConcurrentDictionary<string, int> _reasons = new();
    private long _lastStatus;
    private bool _warnedStall;
    private bool _warnedAllFail;

    public Runner(AppConfig cfg, ProxyPool pool, Blacklist blacklist)
    {
        _cfg = cfg;
        _pool = pool;
        _blacklist = blacklist;
        _checker = new Checker(cfg, pool);
    }

    public void Dispose() => _checker.Dispose();

    public async Task<bool> PreflightAsync()
    {
        Term.Line(("  Checking the connection to Discord...", Tones.Gray));

        ProxyNode?[] samples = _pool.Count == 0
            ? new ProxyNode?[] { null }
            : _pool.Nodes.OrderBy(_ => Random.Shared.Next()).Take(3).Select(n => (ProxyNode?)n).ToArray();

        var results = await Task.WhenAll(samples.Select(n => _checker.ProbeNodeAsync(n, CancellationToken.None)));

        int ok = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            string who = (samples[i]?.Display ?? "direct, your own IP").PadRight(24);
            if (results[i].Ok)
            {
                ok++;
                Term.Line(("  [+] ", Tones.Green), (who, Tones.White), ($"{results[i].Ms} ms", Tones.Gray));
            }
            else
            {
                Term.Line(("  [-] ", Tones.Red), (who, Tones.White), (results[i].Detail, Tones.Gray));
            }
        }

        Term.Blank();
        if (ok > 0)
        {
            if (_pool.Count > 0 && ok < samples.Length)
                Term.Line(("  [!] ", Tones.Yellow), ("Some proxies failed. Proxies > Test proxies can remove them.", Tones.White));
            return true;
        }

        if (_pool.Count > 0)
        {
            Term.Line(("  [!] ", Tones.Yellow), ("Discord could not be reached through your proxies.", Tones.White));
            Term.Line(("      Check the login, your data balance, and whether the plan allows discord.com.", Tones.Gray));
        }
        else
        {
            Term.Line(("  [!] ", Tones.Yellow), ("Discord could not be reached from this connection.", Tones.White));
        }
        return false;
    }

    public async Task RunAsync(IReadOnlyList<string> names)
    {
        var stats = new RunStats { Total = names.Count };
        using var session = new SessionFiles();
        using var cts = new CancellationTokenSource();
        bool stopped = false;

        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            if (!cts.IsCancellationRequested)
            {
                stopped = true;
                cts.Cancel();
                Term.Line(("  [!] ", Tones.Yellow), ("Stopping...", Tones.Gray));
            }
        };
        Console.CancelKeyPress += onCancel;

        using var ticker = new Timer(_ => Tick(stats), null, 1000, 1000);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _cfg.Threads,
            CancellationToken = cts.Token
        };

        try
        {
            await Parallel.ForEachAsync(names, options,
                async (name, ct) => await ProcessAsync(name, stats, session, ct));
        }
        catch (OperationCanceledException) { }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        stats.Clock.Stop();
        await ticker.DisposeAsync();
        Term.ClearStatus();
        Term.Title($"Slizsy Checker | Available {stats.Available} | Taken {stats.Taken} | Failed {stats.Failed + stats.Invalid}");
        Summary(stats, session, stopped);

        var life = LifetimeStats.Load();
        life.Sessions++;
        life.Checked += stats.Done;
        life.Available += stats.Available;
        life.Taken += stats.Taken;
        life.Failed += stats.Failed + stats.Invalid;
        life.Save();
    }

    private async Task ProcessAsync(string name, RunStats s, SessionFiles session, CancellationToken ct)
    {
        var outcome = await _checker.CheckAsync(name, ct);
        int done = Interlocked.Increment(ref s.Done);

        switch (outcome.Verdict)
        {
            case Verdict.Available:
            {
                int found = Interlocked.Increment(ref s.Available);
                _blacklist.Add(name);
                session.AddAvailable(name);
                Emit(session, "[+]", Tones.Green, "available", name, done, s.Total, true, null);
                if (_cfg.Beep) Beep();
                if (!string.IsNullOrWhiteSpace(_cfg.Webhook))
                    await Webhook.SendAsync(_cfg.Webhook, name, found);
                break;
            }
            case Verdict.Taken:
                Interlocked.Increment(ref s.Taken);
                _blacklist.Add(name);
                Emit(session, "[-]", Tones.Red, "taken", name, done, s.Total, _cfg.Verbose, null);
                break;
            case Verdict.Invalid:
                Interlocked.Increment(ref s.Invalid);
                Emit(session, "[~]", Tones.Yellow, "invalid", name, done, s.Total, _cfg.Verbose, null);
                break;
            default:
            {
                Interlocked.Increment(ref s.Failed);
                string why = outcome.Reason ?? "error";
                _reasons.AddOrUpdate(why, 1, (_, n) => n + 1);
                Emit(session, "[!]", Tones.Yellow, "failed", name, done, s.Total, _cfg.Verbose, why);
                break;
            }
        }

        UpdateStatus(s, false);
    }

    private static void Emit(SessionFiles session, string tag, Tone tone, string word, string name, int done, int total, bool show, string? detail)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        session.Log($"{time} {tag} {word} {name} [{done}/{total}]" + (detail != null ? $" ({detail})" : ""));
        if (!show) return;
        Term.Line(
            ($"  {time} ", Tones.Gray),
            ($"{tag} ", tone),
            ($"{word,-10}", tone),
            (name, Tones.White),
            ($"  {done}/{total}", Tones.Gray),
            (detail != null ? $"  ({detail})" : "", Tones.Gray));
    }

    private void UpdateStatus(RunStats s, bool force)
    {
        long now = Environment.TickCount64;
        if (!force)
        {
            long last = Interlocked.Read(ref _lastStatus);
            if (now - last < 150) return;
        }
        Interlocked.Exchange(ref _lastStatus, now);

        int left = Math.Max(0, s.Total - s.Done);
        double seconds = Math.Max(0.001, s.Clock.Elapsed.TotalSeconds);
        double rate = s.Done / seconds;

        var tail = new List<(string, Tone)>
        {
            ($"{s.Done}/{s.Total}  ", Tones.White),
            ($"{rate:0.0}/s  ", Tones.Cyan)
        };
        if (rate > 0 && left > 0)
        {
            var eta = TimeSpan.FromSeconds(Math.Min(left / rate, 359999));
            tail.Add(($"eta {(eta.TotalHours >= 1 ? eta.ToString(@"h\:mm\:ss") : eta.ToString(@"mm\:ss"))}  ", Tones.Gray));
        }
        tail.Add(($"+{s.Available} ", Tones.Green));
        tail.Add(($"-{s.Taken} ", Tones.Red));
        tail.Add(($"!{s.Failed + s.Invalid}", Tones.Yellow));

        int used = 2 + tail.Sum(t => t.Item1.Length);
        int barLen = Math.Clamp(Term.Width - used - 4, 0, 24);
        double frac = s.Total == 0 ? 1 : (double)s.Done / s.Total;
        int filled = (int)Math.Round(frac * barLen);

        var parts = new List<(string Text, Tone Color)> { ("  ", Tones.Gray) };
        if (barLen > 0)
        {
            parts.Add((new string('\u2588', filled), Tones.Purple));
            parts.Add((new string('\u2591', barLen - filled) + " ", Tones.Gray));
        }
        foreach (var t in tail) parts.Add((t.Item1, t.Item2));
        Term.SetStatus(parts.ToArray());
    }

    private void Tick(RunStats s)
    {
        try
        {
            int left = Math.Max(0, s.Total - s.Done);
            Term.Title($"Slizsy Checker | Available {s.Available} | Taken {s.Taken} | Failed {s.Failed + s.Invalid} | Left {left}");
            UpdateStatus(s, true);

            double seconds = s.Clock.Elapsed.TotalSeconds;
            if (!_warnedStall && s.Done == 0 && seconds >= 20)
            {
                _warnedStall = true;
                Term.Line(("  [!] ", Tones.Yellow), ("No results after 20 seconds. The proxies may be slow or blocking Discord.", Tones.White));
                Term.Line(("      Press Ctrl+C, then use Proxies > Test proxies.", Tones.Gray));
            }
            if (!_warnedAllFail && s.Done >= 10 && s.Available + s.Taken == 0)
            {
                _warnedAllFail = true;
                Term.Line(("  [!] ", Tones.Yellow), ("Every check is failing: " + TopReasons(2), Tones.White));
                Term.Line(("      Press Ctrl+C and check your proxies.", Tones.Gray));
            }
        }
        catch { }
    }

    private string TopReasons(int take)
    {
        return string.Join(", ", _reasons
            .OrderByDescending(kv => kv.Value)
            .Take(take)
            .Select(kv => $"{kv.Key} x{kv.Value}"));
    }

    private void Summary(RunStats s, SessionFiles session, bool stopped)
    {
        var elapsed = s.Clock.Elapsed;
        double rate = s.Done / Math.Max(0.001, elapsed.TotalSeconds);
        string time = elapsed.ToString(@"hh\:mm\:ss");

        Term.Blank();
        Term.Line(
            ("  " + new string('\u2500', 6) + " ", Tones.Purple),
            (stopped ? "Stopped" : "Finished", Tones.Pink),
            (" " + new string('\u2500', 22), Tones.Cyan));
        Term.Row("Checked", $"{s.Done}/{s.Total}", Tones.White);
        Term.Row("Available", s.Available.ToString(), Tones.Green);
        Term.Row("Taken", s.Taken.ToString(), Tones.Red);
        Term.Row("Invalid", s.Invalid.ToString(), Tones.Yellow);
        Term.Row("Failed", s.Failed.ToString(), Tones.Yellow);
        if (s.Failed > 0)
            Term.Row("Reasons", TopReasons(3), Tones.Gray);
        Term.Row("Time", time, Tones.Cyan);
        Term.Row("Speed", $"{rate:0.0}/s", Tones.Cyan);
        if (s.Available > 0)
            Term.Row("Saved", Path.GetRelativePath(Paths.Root, session.AvailablePath), Tones.Sky);
        Term.Row("Log", Path.GetRelativePath(Paths.Root, session.LogPath), Tones.Gray);

        session.Log($"summary checked={s.Done}/{s.Total} available={s.Available} taken={s.Taken} invalid={s.Invalid} failed={s.Failed} time={time} speed={rate:0.0}/s");
    }

    private static void Beep()
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsWindows()) Console.Beep(880, 120);
            }
            catch { }
        });
    }
}
