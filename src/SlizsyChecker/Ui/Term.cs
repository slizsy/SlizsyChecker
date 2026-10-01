using System.Runtime.InteropServices;
using System.Text;

namespace SlizsyChecker;

public readonly record struct Tone(int R, int G, int B, ConsoleColor Fallback);

public static class Tones
{
    public static readonly Tone Purple = new(168, 85, 247, ConsoleColor.Magenta);
    public static readonly Tone Pink = new(244, 114, 182, ConsoleColor.Magenta);
    public static readonly Tone Cyan = new(34, 211, 238, ConsoleColor.Cyan);
    public static readonly Tone Sky = new(96, 165, 250, ConsoleColor.Cyan);
    public static readonly Tone Green = new(74, 222, 128, ConsoleColor.Green);
    public static readonly Tone Red = new(248, 113, 113, ConsoleColor.Red);
    public static readonly Tone Yellow = new(250, 204, 21, ConsoleColor.Yellow);
    public static readonly Tone Gray = new(148, 163, 184, ConsoleColor.DarkGray);
    public static readonly Tone White = new(241, 245, 249, ConsoleColor.White);
}

public static class Term
{
    private static readonly object Gate = new();
    private static bool _vt;
    private static (string Text, Tone Color)[]? _status;
    private static int _statusWidth;

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    public static int Width
    {
        get
        {
            try { return Console.WindowWidth; }
            catch { return 100; }
        }
    }

    public static void Init()
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
        if (Console.IsOutputRedirected) { _vt = false; return; }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var handle = GetStdHandle(-11);
                _vt = GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | 0x0004);
            }
            catch { _vt = false; }
        }
        else
        {
            _vt = true;
        }
    }

    private static void Put(string text, Tone tone)
    {
        if (_vt)
        {
            Console.Write("\u001b[38;2;" + tone.R + ";" + tone.G + ";" + tone.B + "m" + text + "\u001b[0m");
        }
        else
        {
            Console.ForegroundColor = tone.Fallback;
            Console.Write(text);
            Console.ResetColor();
        }
    }

    private static void Erase()
    {
        if (_status == null) return;
        Console.Write("\r" + new string(' ', _statusWidth) + "\r");
    }

    private static void Draw()
    {
        if (_status == null) return;
        int width = 0;
        foreach (var part in _status)
        {
            Put(part.Text, part.Color);
            width += part.Text.Length;
        }
        _statusWidth = width;
    }

    public static void SetStatus(params (string Text, Tone Color)[] parts)
    {
        if (Console.IsOutputRedirected) return;
        lock (Gate)
        {
            Erase();
            _status = parts;
            Draw();
        }
    }

    public static void ClearStatus()
    {
        lock (Gate)
        {
            Erase();
            _status = null;
            _statusWidth = 0;
        }
    }

    public static void Text(string text, Tone color)
    {
        lock (Gate)
        {
            Erase();
            Put(text, color);
        }
    }

    public static void Line(params (string Text, Tone Color)[] parts)
    {
        lock (Gate)
        {
            Erase();
            foreach (var part in parts) Put(part.Text, part.Color);
            Console.WriteLine();
            Draw();
        }
    }

    public static void Row(string label, string value, Tone color)
    {
        Line(($"  {label,-11}", Tones.Gray), (value, color));
    }

    public static void Blank()
    {
        lock (Gate)
        {
            Erase();
            Console.WriteLine();
            Draw();
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                _status = null;
                _statusWidth = 0;
                Console.Clear();
            }
        }
        catch { }
    }

    public static void Title(string title)
    {
        try { Console.Title = title; } catch { }
    }

    public static Tone Blend(Tone a, Tone b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Tone(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            t < 0.5 ? a.Fallback : b.Fallback);
    }

    public static void Gradient(IReadOnlyList<string> lines, Tone from, Tone to)
    {
        int width = lines.Max(l => l.Length);
        int span = width + lines.Count * 2;
        lock (Gate)
        {
            for (int row = 0; row < lines.Count; row++)
            {
                string line = lines[row];
                if (_vt)
                {
                    var sb = new StringBuilder();
                    for (int col = 0; col < line.Length; col++)
                    {
                        char ch = line[col];
                        if (ch == ' ') { sb.Append(' '); continue; }
                        var tone = Blend(from, to, (col + row * 2.0) / span);
                        sb.Append("\u001b[38;2;").Append(tone.R).Append(';').Append(tone.G).Append(';').Append(tone.B).Append('m').Append(ch);
                    }
                    sb.Append("\u001b[0m");
                    Console.WriteLine(sb.ToString());
                }
                else
                {
                    Console.ForegroundColor = row < lines.Count / 2 ? ConsoleColor.Magenta : ConsoleColor.Cyan;
                    Console.WriteLine(line);
                    Console.ResetColor();
                }
            }
        }
    }
}
