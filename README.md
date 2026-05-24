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
- Live fight timer; auto-show on fight start, auto-hide on expiry (configurable delay)
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

### Character Profiles
- Per-character log file association
- Profiles persist across restarts via LiteDB

## Requirements

- Windows 10/11 (64-bit)
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

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
