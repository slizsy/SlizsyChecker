<div align="center">

<img src="assets/screenshot.png" alt="Slizsy Checker" width="760">

# Slizsy Checker

Fast, colorful username availability checker for Windows. One exe, no install.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)
![Windows](https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square)
![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)

</div>

## Features

- Random username generator with charset, length and amount controls
- Bring your own list, or paste usernames straight into the app
- Local validation: 2-32 characters, lowercase letters, digits, `_` and `.`, no duplicates
- Proxy support for HTTP, HTTPS and SOCKS5, with login
- Paste proxies from the menu, test them, and remove the dead ones
- Bad or rate-limited proxies rest automatically and are skipped
- Connection check before every run, so a broken setup fails fast
- Live progress bar with speed and ETA
- Clear failure reasons instead of silent hangs
- Settings presets: auto, safe, balanced, fast, custom
- Discord webhook and optional beep when a name is available
- Per-session results and logs, plus lifetime stats

## Quick start

### Download

1. Open the **Releases** tab and download `SlizsyChecker.exe`.
2. Run it. The `data`, `results` and `logs` folders are created next to the exe.

### Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
build.bat
```

or

```
dotnet publish src/SlizsyChecker -c Release -r win-x64 -o dist
```

The single-file exe is written to `dist/SlizsyChecker.exe`.
To work on it in Visual Studio 2022, open `SlizsyChecker.sln`.

## Usage

| Menu | What it does |
|---|---|
| Start checking | Runs a connection check, then checks every ready username |
| Usernames | Random generator, paste your own list, forget checked names |
| Proxies | Paste, test, edit or remove proxies |
| Settings | Presets, threads, timeout, retries, webhook, beep |
| Stats and files | Lifetime stats and shortcuts to the output folders |

Press `Ctrl+C` during a run to stop it cleanly.

### Proxies

One proxy per line in `data/proxies.txt`, or paste them under **Proxies**. All of these work:

```
host:port
host:port:username:password
http://username:password@host:port
socks5://username:password@host:port
```

See [`examples/`](examples) for sample files.

## Configuration

Settings are stored in `data/config.json` and can be changed from the app.

| Key | Default | Description |
|---|---|---|
| `usernames.custom` | `false` | Use `data/usernames.txt` instead of generating names |
| `usernames.amount` | `1000` | How many names to generate |
| `usernames.length` | `3` | Length of generated names |
| `usernames.charset` | `lowernum` | `lower`, `digits`, `lowernum` or `all` |
| `retry.enabled` | `true` | Retry failed requests |
| `retry.max_attempts` | `5` | Attempts per username |
| `threads` | `20` | Concurrent checks |
| `timeout` | `15` | Request timeout in seconds |
| `webhook` | `""` | Discord webhook URL for available names |
| `verbose` | `true` | Show every result, not only available names |
| `beep` | `false` | Beep when a name is available |

## Output

```
data/
  config.json      settings
  proxies.txt      your proxies
  usernames.txt    your custom list
  blacklist.txt    names already checked
  valids.txt       every available name found
  stats.json       lifetime totals
results/
  available-<time>.txt    names found in that session
logs/
  session-<time>.log      full log of that session
```

## Project layout

```
src/SlizsyChecker/
  Program.cs, App.cs     entry point and menus
  Core/                  checker, runner, proxy pool, username rules, webhook
  Ui/                    banner, console colors, input helpers
  Storage/               config, paths, blacklist, session files, stats
```

## Disclaimer

This project is for educational use and is not affiliated with Discord.
Sending automated requests in bulk may violate Discord's Terms of Service and can get your IP address rate-limited or blocked.
You are responsible for how you use it. Keep volumes modest.

## Credits and license

Based on the idea and original Go implementation by [raz461](https://github.com/raz461/Discord-Username-Checker).
Released under the [GPL-3.0](LICENSE) license.
