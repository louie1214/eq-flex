# EQ Flex

An EverQuest log-parsing desktop app for Windows. Combines a live damage meter with an always-on-top overlay, a trigger system, and floating combat text (FCT).

## Features

### Damage Meter
- Parses live EQ log files — damage, healing, casting, and DoTs
- Fight list with per-player breakdown: DPS, SDPS, hits, attempts, abilities
- Healing tab with healer and spell-level detail
- Tanking tab with damage-taken and hit counts
- Pet damage attributed to owner
- Configurable parse time range (last hour → all)
- Layout and column widths persisted across sessions

### Always-on-top DPS Overlay
- Transparent, click-through window that sits over the game
- Three views: DPS / Tank / Heal — toggle from the overlay header
- Live fight timer derived from log timestamps
- Auto-show when you participate in a fight; auto-hide on expiry (configurable delay)
- Drag to reposition, resize grip, lock/unlock

### Trigger System
- Text and regex phrase matching against incoming log lines
- Folder tree with unlimited nesting; enable/disable entire folders
- Actions per trigger: display text, timer bar, TTS speech, audio file playback
- Per-action cooldown, duration, token substitution (`{S0}`, `{GroupName}`, `{C}`, `{L}`)
- Multiple named overlay windows — each independently positioned, sized, and styled
- Per-overlay background opacity, chrome toggle, text alignment
- Import from NAG (`trigger-database.json`) preserving full folder hierarchy

### Floating Combat Text (FCT)
- Animated numbers float up from a configurable canvas overlay
- Separate color and visibility toggles per damage type:
  - Melee auto-attack, Ability (Backstab, Kick, Frenzy, etc.), Spell/Proc, DoT, Pet
  - Heal received (with optional caster name), Heal done, Incoming damage
- Critical hits scale up at a configurable multiplier
- Lane + tier slot system prevents overlap during AoE bursts
- Heal display includes overheal: `+1,234 (456 oh)` or `(456 oh)` for pure overheal
- All colors customizable via HSV color wheel picker

### TLP Tunnel (Trade Mode)
- Parses real-time auction channel broadcasts — WTS, WTB, Selling, Buying
- **Trades tab** — live feed with per-item rows; search and filter by type; hover any item for on-demand stat lookup via Lucy
- **Krono tab** — current Krono price from tlp-auctions.com; local sale history with timestamps
- **Prices tab** — search by item name; uses local data first, falls back to tlp-auctions.com API; optionally include both sources in one search
- **Item Alerts** — keyword alerts with optional max price caps (platinum, Krono, or combined); Krono-to-PP rate used for total-equivalent comparisons; shared display, overlay, and sound settings for all alerts; 14-day hit history
- Enable per character profile

### Character Profiles
- Selecting a profile shows a read-only detail card (name, character, server, log path, parsing options)
- Per-character log file association and parsing options
- Per-profile log archiving — automatically renames and clears the log when it exceeds a configurable size limit
- Profiles persist across restarts via LiteDB
- App starts live tailing automatically on launch when a profile was previously active

### Auto-Update
- Checks GitHub Releases for new versions on startup
- Sidebar notification shows available version with release notes
- One-click download and install via Velopack — restarts into the new version automatically

## User Data

All user data is stored in a single file:

```
%APPDATA%\EqFlex\eqflex.db
```

This LiteDB database holds character profiles, app settings, overlay positions, and all triggers. To back up or transfer your configuration, copy that file. To reset to defaults, delete it.

Errors and crashes are logged to:

```
%APPDATA%\EqFlex\logs\eqflex-YYYYMMDD.log
```

Logs roll daily and the last 7 days are retained. If you encounter a bug, including the relevant log file in your report helps significantly.

## Requirements

- Windows 10/11 (64-bit)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

## Building from Source

```
git clone https://github.com/louie1214/eq-flex.git
cd eq-flex
dotnet build
dotnet test
```

The app project targets `net10.0-windows` and requires the Windows Desktop Runtime.

## Stack

| Layer | Technology |
|-------|-----------|
| UI | WPF / .NET 10 |
| MVVM | CommunityToolkit.Mvvm |
| Storage | LiteDB (`%APPDATA%\EqFlex\eqflex.db`) |
| DI | Microsoft.Extensions.DependencyInjection |
| Updates | Velopack |
| Tests | xUnit |

Parsing logic is ported from [EQLogParser](https://github.com/kauffman12/EQLogParser) with DI refactoring and architectural changes.

## Project Structure

```
src/
  EqFlex.Core/           # Domain: models, parsers, interfaces, services
  EqFlex.Infrastructure/ # File I/O, LiteDB storage, spell data loaders
  EqFlex.App/            # WPF shell — ViewModels, Views, Overlays
tests/
  EqFlex.Tests/          # xUnit parser tests
```

## Data Files

Spell, NPC, pet, proc, and title data live in `src/EqFlex.Infrastructure/Data/` and are copied to the output directory on build. These are sourced from EQLogParser.

## License

MIT
