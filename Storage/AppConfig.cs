using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlizsyChecker;

public sealed class UsernamesCfg
{
    [JsonPropertyName("custom")] public bool Custom { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; } = 1000;
    [JsonPropertyName("length")] public int Length { get; set; } = 3;
    [JsonPropertyName("charset")] public string Charset { get; set; } = "lowernum";
}

public sealed class RetryCfg
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("max_attempts")] public int MaxAttempts { get; set; } = 5;
}

public sealed class AppConfig
{
    [JsonPropertyName("usernames")] public UsernamesCfg Usernames { get; set; } = new();
    [JsonPropertyName("retry")] public RetryCfg Retry { get; set; } = new();
    [JsonPropertyName("threads")] public int Threads { get; set; } = 20;
    [JsonPropertyName("timeout")] public int Timeout { get; set; } = 15;
    [JsonPropertyName("webhook")] public string Webhook { get; set; } = "";
    [JsonPropertyName("verbose")] public bool Verbose { get; set; } = true;
    [JsonPropertyName("beep")] public bool Beep { get; set; }

    public void Normalize()
    {
        Usernames ??= new UsernamesCfg();
        Retry ??= new RetryCfg();
        Webhook ??= "";
        Threads = Math.Clamp(Threads, 1, 1000);
        Timeout = Math.Clamp(Timeout, 1, 120);
        Retry.MaxAttempts = Math.Clamp(Retry.MaxAttempts, 1, 20);
        Usernames.Amount = Math.Clamp(Usernames.Amount, 1, 1_000_000);
        Usernames.Length = Math.Clamp(Usernames.Length, 2, 32);
        if (Usernames.Charset == null || !UsernameRules.Charsets.ContainsKey(Usernames.Charset))
            Usernames.Charset = "lowernum";
        else
            Usernames.Charset = Usernames.Charset.ToLowerInvariant();
    }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppConfig Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(Paths.Config))
            {
                var fresh = new AppConfig();
                Save(fresh);
                return fresh;
            }
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Paths.Config), Options) ?? new AppConfig();
            cfg.Normalize();
            return cfg;
        }
        catch (Exception ex)
        {
            warning = "Config could not be read (" + ex.Message + "). Defaults loaded.";
            return new AppConfig();
        }
    }

    public static void Save(AppConfig cfg)
    {
        try { File.WriteAllText(Paths.Config, JsonSerializer.Serialize(cfg, Options)); }
        catch { }
    }
}
