using System.Collections.Concurrent;
using System.Diagnostics;

namespace SlizsyChecker;

public sealed class App
{
    private readonly record struct Note(string Text, bool Good);

    private AppConfig _cfg = new();
    private readonly ProxyPool _pool = new();
    private readonly Blacklist _blacklist = new();
    private List<string> _names = new();
    private int _invalid;
    private int _skipped;

    public async Task RunAsync()
    {
        Term.Init();
        Paths.EnsureAll();
        _cfg = ConfigStore.Load(out var warning);
        _blacklist.Load();
        _pool.Load();
        Prepare();

        Note? note = warning != null ? new Note(warning, false) : null;
        while (true)
        {
            DrawMain(note);
            note = null;

            var choice = Input.Ask("  > ");
            if (choice == null) return;

            switch (choice)
            {
                case "1":
                    note = await StartAsync();
                    break;
                case "2":
                    UsernamesMenu();
                    break;
                case "3":
                    await ProxiesMenuAsync();
                    break;
                case "4":
                    SettingsMenu();
                    break;
                case "5":
                    FilesMenu();
                    break;
                case "6":
                    return;
                default:
                    note = new Note("Pick a number from the menu.", false);
                    break;
            }
        }
    }

    private void Prepare()
    {
        _invalid = 0;
        _skipped = 0;
        if (_cfg.Usernames.Custom)
        {
            var load = UsernameRules.LoadCustom(_blacklist.Contains);
            _names = load.Names;
            _invalid = load.Invalid;
            _skipped = load.Skipped;
        }
        else
        {
            _names = UsernameRules.Generate(_cfg.Usernames, _blacklist.Contains);
        }
    }

    private string Source()
    {
        return _cfg.Usernames.Custom
            ? "your own list"
            : $"random, {_cfg.Usernames.Length} chars, {_cfg.Usernames.Charset}";
    }

    private static void Item(string key, string label)
    {
        Term.Line(("  [", Tones.Gray), (key, Tones.Pink), ("] ", Tones.Gray), (label, Tones.White));
    }

    private static void Header(string title)
    {
        Banner.Show();
        Term.Line(("  " + title, Tones.Pink));
        Term.Blank();
    }

    private static void ShowNote(Note? note)
    {
        if (note == null) return;
        Term.Blank();
        var n = note.Value;
        Term.Line(n.Good ? ("  [+] ", Tones.Green) : ("  [!] ", Tones.Yellow), (n.Text, Tones.White));
    }

    private void DrawMain(Note? note)
    {
        Banner.Show();

        Term.Line(("  Usernames  ", Tones.Gray), (_names.Count.ToString("N0"), Tones.Cyan), ("   " + Source(), Tones.Gray));
        Term.Line(
            ("  Proxies    ", Tones.Gray), (_pool.Count.ToString(), Tones.Cyan),
            ("   Threads  ", Tones.Gray), (_cfg.Threads.ToString(), Tones.Cyan),
            ("   Checked before  ", Tones.Gray), (_blacklist.Count.ToString("N0"), Tones.Cyan));
        Term.Blank();

        Item("1", "Start checking");
        Item("2", "Usernames");
        Item("3", "Proxies");
        Item("4", "Settings");
        Item("5", "Stats and files");
        Item("6", "Exit");

        if (_names.Count == 0)
        {
            Term.Blank();
            Term.Line(("  Tip: ", Tones.Purple), ("no usernames ready. Open Usernames to generate or paste a list.", Tones.Gray));
        }
        else if (_pool.Count == 0)
        {
            Term.Blank();
            Term.Line(("  Tip: ", Tones.Purple), ("no proxies added. Checks will use your own IP. Add some under Proxies.", Tones.Gray));
        }

        ShowNote(note);
        Term.Blank();
    }

    private async Task<Note?> StartAsync()
    {
        if (_names.Count == 0)
            return new Note("No usernames to check. Open Usernames first.", false);

        Banner.Show();
        using var runner = new Runner(_cfg, _pool, _blacklist);

        bool connected = await runner.PreflightAsync();
        if (!connected && !Input.Bool("Start anyway", false))
            return new Note("Cancelled. Fix the connection under Proxies, then try again.", false);

        Term.Blank();
        Term.Line(
            ("  Running ", Tones.White),
            ($"{_names.Count:N0} usernames", Tones.Cyan),
            (" | ", Tones.Gray),
            ($"{_cfg.Threads} threads", Tones.Cyan),
            (" | ", Tones.Gray),
            ($"{_pool.Count} proxies", Tones.Cyan));
        Term.Line(("  Ctrl+C stops the run", Tones.Gray));
        Term.Blank();

        await runner.RunAsync(_names);

        Input.Pause();
        Prepare();
        return null;
    }

    private void UsernamesMenu()
    {
        Note? note = null;
        while (true)
        {
            Header("Usernames");
            Term.Line(("  Ready  ", Tones.Gray), (_names.Count.ToString("N0"), Tones.Cyan), ("   " + Source(), Tones.Gray));
            if (_cfg.Usernames.Custom && (_invalid > 0 || _skipped > 0))
                Term.Line(("         ", Tones.Gray), ($"{_invalid} invalid, {_skipped} already checked", Tones.Gray));
            Term.Blank();
            Item("1", "Random generator");
            Item("2", "Paste my own list");
            Item("3", "Forget checked names");
            Item("4", "Back");
            ShowNote(note);
            Term.Blank();
            note = null;

            var choice = Input.Ask("  > ");
            if (choice == null || choice == "4") return;

            switch (choice)
            {
                case "1":
                    Term.Blank();
                    _cfg.Usernames.Custom = false;
                    _cfg.Usernames.Amount = Input.Int("Amount", _cfg.Usernames.Amount, 1, 1_000_000);
                    _cfg.Usernames.Length = Input.Int("Length", _cfg.Usernames.Length, 2, 32);
                    _cfg.Usernames.Charset = Input.Choice("Charset", _cfg.Usernames.Charset, UsernameRules.CharsetNames);
                    _cfg.Normalize();
                    ConfigStore.Save(_cfg);
                    Prepare();
                    note = new Note($"{_names.Count:N0} random usernames ready.", true);
                    break;
                case "2":
                    note = PasteUsernames();
                    break;
                case "3":
                    Term.Blank();
                    if (Input.Bool("Forget every name checked before", false))
                    {
                        _blacklist.Clear();
                        Prepare();
                        note = new Note("Cleared. Those names can be checked again.", true);
                    }
                    break;
                default:
                    note = new Note("Pick a number from the menu.", false);
                    break;
            }
        }
    }

    private Note PasteUsernames()
    {
        Term.Blank();
        Term.Line(("  Paste usernames, one per line. Press Enter on an empty line to finish.", Tones.Gray));
        var pasted = Input.Block();
        var parsed = UsernameRules.Parse(pasted);
        if (parsed.Names.Count == 0)
            return new Note(pasted.Count == 0 ? "Nothing pasted." : "No valid usernames found. Use 2-32 letters, digits, _ or .", false);

        try
        {
            var existing = File.Exists(Paths.Usernames) ? UsernameRules.Parse(File.ReadAllLines(Paths.Usernames)).Names : new List<string>();
            bool add = existing.Count == 0 || Input.Bool("Add to the existing list", true);
            var all = add ? existing.Concat(parsed.Names).Distinct().ToList() : parsed.Names;
            File.WriteAllLines(Paths.Usernames, all);
            _cfg.Usernames.Custom = true;
            ConfigStore.Save(_cfg);
            Prepare();
            string extra = parsed.Invalid > 0 ? $", {parsed.Invalid} invalid skipped" : "";
            return new Note($"{parsed.Names.Count} added. {_names.Count:N0} ready to check{extra}.", true);
        }
        catch (Exception ex)
        {
            return new Note("Could not save: " + ex.Message, false);
        }
    }

    private async Task ProxiesMenuAsync()
    {
        Note? note = null;
        while (true)
        {
            Header("Proxies");
            Term.Line(("  Loaded  ", Tones.Gray), (_pool.Count.ToString(), Tones.Cyan), ("   ready  ", Tones.Gray), (_pool.Ready().ToString(), Tones.Cyan));
            Term.Blank();
            Item("1", "Paste proxies");
            Item("2", "Test proxies");
            Item("3", "Edit proxies.txt");
            Item("4", "Remove all proxies");
            Item("5", "Back");
            ShowNote(note);
            Term.Blank();
            note = null;

            var choice = Input.Ask("  > ");
            if (choice == null || choice == "5") return;

            switch (choice)
            {
                case "1":
                    note = PasteProxies();
                    break;
                case "2":
                    await TestProxiesAsync();
                    break;
                case "3":
                    try
                    {
                        if (!File.Exists(Paths.Proxies)) File.WriteAllText(Paths.Proxies, "");
                        Process.Start(new ProcessStartInfo { FileName = Paths.Proxies, UseShellExecute = true });
                        Term.Blank();
                        Term.Text("  Save the file, then press Enter to reload it", Tones.Gray);
                        Console.ReadLine();
                        _pool.Load();
                        note = new Note($"{_pool.Count} proxies loaded.", true);
                    }
                    catch
                    {
                        note = new Note("Could not open it. Path: " + Paths.Proxies, false);
                    }
                    break;
                case "4":
                    Term.Blank();
                    if (Input.Bool("Remove every proxy", false))
                    {
                        try { File.WriteAllText(Paths.Proxies, ""); } catch { }
                        _pool.Load();
                        note = new Note("All proxies removed.", true);
                    }
                    break;
                default:
                    note = new Note("Pick a number from the menu.", false);
                    break;
            }
        }
    }

    private Note PasteProxies()
    {
        Term.Blank();
        Term.Line(("  Paste proxies, one per line. Press Enter on an empty line to finish.", Tones.Gray));
        Term.Line(("  Formats: host:port   host:port:user:pass   http://user:pass@host:port", Tones.Gray));
        var pasted = Input.Block();
        if (pasted.Count == 0) return new Note("Nothing pasted.", false);

        var good = new List<string>();
        int bad = 0;
        foreach (var line in pasted)
        {
            if (line[0] == '#') continue;
            if (ProxyPool.Normalize(line) != null) good.Add(line);
            else bad++;
        }
        if (good.Count == 0)
            return new Note($"No valid proxies found ({bad} unreadable).", false);

        try
        {
            var existing = File.Exists(Paths.Proxies) ? File.ReadAllLines(Paths.Proxies).ToList() : new List<string>();
            bool hasExisting = existing.Any(l => l.Trim().Length > 0 && l.TrimStart()[0] != '#');
            bool add = !hasExisting || Input.Bool("Add to the existing proxies", true);
            var output = add ? existing.Concat(good).Distinct().ToList() : good.Distinct().ToList();
            File.WriteAllLines(Paths.Proxies, output);
            _pool.Load();
            string extra = bad > 0 ? $", {bad} unreadable skipped" : "";
            return new Note($"{good.Count} added. {_pool.Count} proxies loaded{extra}.", true);
        }
        catch (Exception ex)
        {
            return new Note("Could not save: " + ex.Message, false);
        }
    }

    private async Task TestProxiesAsync()
    {
        Term.Blank();
        if (_pool.Count == 0)
        {
            Term.Line(("  [!] ", Tones.Yellow), ("No proxies to test.", Tones.White));
            Input.Pause();
            return;
        }

        Term.Line(("  Testing ", Tones.White), (_pool.Count.ToString(), Tones.Cyan), (" proxies against Discord", Tones.White));
        Term.Blank();

        var dead = new ConcurrentBag<string>();
        var reasons = new ConcurrentBag<string>();
        int alive = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = 20 };

        await Parallel.ForEachAsync(_pool.Nodes, options, async (node, ct) =>
        {
            var probe = await Checker.ProbeAsync(node.GetClient(_cfg.Timeout), ct);
            if (probe.Ok)
            {
                Interlocked.Increment(ref alive);
                Term.Line(("  [+] ", Tones.Green), (node.Display.PadRight(24), Tones.White), ($"{probe.Ms} ms", Tones.Gray));
            }
            else
            {
                dead.Add(node.Url);
                reasons.Add(probe.Detail);
                Term.Line(("  [-] ", Tones.Red), (node.Display.PadRight(24), Tones.White), (probe.Detail, Tones.Gray));
            }
        });

        Term.Blank();
        Term.Line(("  Working ", Tones.Gray), (alive.ToString(), Tones.Green), ("   Dead ", Tones.Gray), (dead.Count.ToString(), Tones.Red));

        if (alive == 0 && !reasons.IsEmpty)
        {
            string top = reasons.GroupBy(r => r).OrderByDescending(g => g.Count()).First().Key;
            Term.Blank();
            Term.Line(("  [!] ", Tones.Yellow), ("Every proxy failed: " + top, Tones.White));
            Term.Line(("      Check the login, your data balance, and whether the plan allows discord.com.", Tones.Gray));
        }

        if (!dead.IsEmpty && alive > 0 && Input.Bool("Remove the dead proxies from the file", false))
        {
            RemoveDead(new HashSet<string>(dead));
            Term.Line(("  [+] ", Tones.Green), ("Removed", Tones.White));
        }
        Input.Pause();
    }

    private void RemoveDead(HashSet<string> dead)
    {
        try
        {
            var keep = new List<string>();
            foreach (var raw in File.ReadAllLines(Paths.Proxies))
            {
                var line = raw.Trim();
                if (line.Length > 0 && line[0] != '#')
                {
                    var url = ProxyPool.Normalize(line);
                    if (url != null && dead.Contains(url)) continue;
                }
                keep.Add(raw);
            }
            File.WriteAllLines(Paths.Proxies, keep);
        }
        catch { }
        _pool.Load();
    }

    private void SettingsMenu()
    {
        Header("Settings");
        var c = _cfg;

        Term.Line(("  Presets", Tones.Gray));
        Term.Line(("    auto      ", Tones.Cyan), ("threads follow your proxy count", Tones.Gray));
        Term.Line(("    safe      ", Tones.Cyan), ("5 threads, slow and gentle", Tones.Gray));
        Term.Line(("    balanced  ", Tones.Cyan), ("20 threads", Tones.Gray));
        Term.Line(("    fast      ", Tones.Cyan), ("50 threads, needs good proxies", Tones.Gray));
        Term.Line(("    custom    ", Tones.Cyan), ("set every value yourself", Tones.Gray));
        Term.Blank();

        string preset = Input.Choice("Preset", "keep", new[] { "auto", "safe", "balanced", "fast", "custom" });
        switch (preset)
        {
            case "auto":
                c.Threads = _pool.Count == 0 ? 3 : Math.Clamp(_pool.Count * 2, 3, 40);
                c.Timeout = 15; c.Retry.Enabled = true; c.Retry.MaxAttempts = 4;
                break;
            case "safe":
                c.Threads = 5; c.Timeout = 20; c.Retry.Enabled = true; c.Retry.MaxAttempts = 5;
                break;
            case "balanced":
                c.Threads = 20; c.Timeout = 15; c.Retry.Enabled = true; c.Retry.MaxAttempts = 4;
                break;
            case "fast":
                c.Threads = 50; c.Timeout = 10; c.Retry.Enabled = true; c.Retry.MaxAttempts = 3;
                break;
            case "custom":
                c.Threads = Input.Int("Threads", c.Threads, 1, 1000);
                c.Timeout = Input.Int("Timeout seconds", c.Timeout, 1, 120);
                c.Retry.Enabled = Input.Bool("Retry failed requests", c.Retry.Enabled);
                if (c.Retry.Enabled)
                    c.Retry.MaxAttempts = Input.Int("Max attempts", c.Retry.MaxAttempts, 1, 20);
                break;
        }

        Term.Blank();
        c.Verbose = Input.Bool("Show every result", c.Verbose);
        c.Beep = Input.Bool("Beep when a name is available", c.Beep);

        string state = string.IsNullOrEmpty(c.Webhook) ? "none" : "set";
        var hook = Input.Ask($"  Discord webhook [{state}] (- clears): ");
        if (hook == "-")
        {
            c.Webhook = "";
        }
        else if (!string.IsNullOrEmpty(hook))
        {
            if (Webhook.IsValid(hook)) c.Webhook = hook;
            else Term.Line(("  That is not a Discord webhook URL, kept the old value.", Tones.Yellow));
        }

        c.Normalize();
        ConfigStore.Save(c);
        _pool.ResetClients();

        Term.Blank();
        Term.Line(
            ("  [+] ", Tones.Green),
            ("Saved: ", Tones.White),
            ($"{c.Threads} threads, {c.Timeout}s timeout, {(c.Retry.Enabled ? c.Retry.MaxAttempts : 1)} attempts", Tones.Gray));
        Input.Pause();
    }

    private void FilesMenu()
    {
        Note? note = null;
        while (true)
        {
            Header("Stats and files");

            var life = LifetimeStats.Load();
            long saved = 0;
            try
            {
                if (File.Exists(Paths.Valids))
                    saved = File.ReadLines(Paths.Valids).Count(l => l.Trim().Length > 0);
            }
            catch { }

            Term.Row("Sessions", life.Sessions.ToString("N0"), Tones.White);
            Term.Row("Checked", life.Checked.ToString("N0"), Tones.White);
            Term.Row("Available", life.Available.ToString("N0"), Tones.Green);
            Term.Row("Taken", life.Taken.ToString("N0"), Tones.Red);
            Term.Row("Failed", life.Failed.ToString("N0"), Tones.Yellow);
            Term.Row("Saved names", saved.ToString("N0"), Tones.Cyan);
            Term.Blank();
            Item("1", "Open results folder");
            Item("2", "Open data folder");
            Item("3", "Open logs folder");
            Item("4", "Back");
            ShowNote(note);
            Term.Blank();
            note = null;

            var choice = Input.Ask("  > ");
            if (choice == null || choice == "4") return;

            switch (choice)
            {
                case "1": note = OpenFolder(Paths.Results); break;
                case "2": note = OpenFolder(Paths.Data); break;
                case "3": note = OpenFolder(Paths.Logs); break;
                default: note = new Note("Pick a number from the menu.", false); break;
            }
        }
    }

    private static Note? OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return null;
        }
        catch
        {
            return new Note("Could not open it. Path: " + path, false);
        }
    }
}
