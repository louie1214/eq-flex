# EQ Flex — Claude Context

## What this is

EQ Flex is an EverQuest log-parsing desktop app combining the damage-meter functionality of [EQLogParser (EQLP)](C:\Users\louie\dev-projects\EQLogParser) with an always-on-top overlay. Parsing logic is ported from EQLP — always check EQLP first before implementing new parsing features.

## Stack

- **WPF / .NET 10** (`net10.0-windows`), target Windows 11
- **CommunityToolkit.Mvvm** — `[ObservableProperty]`, `[RelayCommand]` source generators
- **LiteDB** — profiles and settings stored in `%APPDATA%\EqFlex\eqflex.db`
- **xUnit** — 24 tests in `tests/EqFlex.Tests/`
- **DI** — `Microsoft.Extensions.DependencyInjection`, wired in `App.xaml.cs`

## Solution layout

```
src/
  EqFlex.Core/           # Domain: models, parsers, interfaces, services
  EqFlex.Infrastructure/ # File I/O, LiteDB storage, spell data loaders
  EqFlex.App/            # WPF shell — ViewModels + Views + Overlays
tests/
  EqFlex.Tests/
```

## Key files

| File | Purpose |
|------|---------|
| `Core/Parsing/DamageParser.cs` | All melee/spell/DoT/miss patterns; ability modifier class detection |
| `Core/Parsing/HealingParser.cs` | Heal line parser |
| `Core/Parsing/CastParser.cs` | Spell cast + zone entry; low-confidence class detection from class-exclusive spells |
| `Core/Parsing/LineModifiersParser.cs` | Parses `(Critical)`, `(Assassinate)`, `(Headshot)` etc. into a bitmask |
| `Core/Parsing/ParserUtil.cs` | Shared: `GetHitType`, `FindStop`, `UpdateAttacker/Defender` |
| `Core/Services/FightManager.cs` | Fight lifecycle, damage/heal routing, expiry, replay batching, session reset |
| `Core/Services/PlayerRegistry.cs` | Allied entity tracking (players/pets/mercs), pet ownership, class detection |
| `Core/Models/Fight.cs` | `Fight`, `PlayerStats` (ConcurrentDictionary fields), `AbilityStats`, `TankStats` |
| `Infrastructure/Logging/LogTailer.cs` | `FileSystemWatcher` tail; `ReplayAsync` with cutoff timestamp support |
| `Infrastructure/Logging/LogProcessor.cs` | Channel consumer — timestamp strip, parser routing, group/who/charm detection |
| `Infrastructure/Data/SpellDataService.cs` | Loads spells/npcs/pets/procs/titles; builds class map and charm landing map |
| `App/ViewModels/LogViewModel.cs` | Log file selection, parse time range, replay flow, live tailing |
| `App/ViewModels/DamageViewModel.cs` | Fight list, player/ability/healer aggregation for display |
| `App/ViewModels/OverlayViewModel.cs` | DPS/Tank/Heal overlay data; fight timer; auto-show/hide |
| `App/ViewModels/TriggersViewModel.cs` | Trigger editor; action color/font/overlay mirror properties; `AvailableOverlays` list |
| `App/ViewModels/OverlaysViewModel.cs` | Overlay CRUD (create/delete/preview/toggle); wraps `OverlayManager` |
| `App/ViewModels/TriggerOverlayViewModel.cs` | Per-overlay VM — `Alerts`/`Timers` collections, preview mode, `BackgroundOpacity`, `ShowChrome`, `TextAlign`, `WpfTextAlignment` |
| `App/Services/OverlayManager.cs` | Owns all `TriggerOverlay` window instances; routes `TriggerFired` actions; shared TTS + audio |
| `App/Views/DamageView.xaml` | Main parse UI — fight list + tabbed detail panel; layout persisted |
| `App/Views/TriggersView.xaml` | Trigger list + editor with `ColorWheelPicker`, action detail panel |
| `App/Views/OverlaysView.xaml` | Overlay management — create/delete trigger overlays; DPS overlay settings |
| `App/Overlays/DamageMeterOverlay.xaml` | Transparent always-on-top DPS overlay window |
| `App/Overlays/TriggerOverlay.xaml` | Transparent always-on-top trigger overlay — alerts + timer bars |
| `App/Controls/ColorWheelPicker.xaml` | HSV color wheel control; `SelectedHex` DP bindable two-way |
| `App/Overlays/NativeMethods.cs` | Win32 P/Invoke: `SetWindowLong`, `SetWindowPos`, `SendMessage`, `ReleaseCapture` |
| `Core/Models/Trigger.cs` | `Trigger`, `TriggerAction`, `CapturePhrase`, `TriggerFolder` (`ParentFolderId=0` = root; non-zero = nested) |
| `Core/Models/OverlayWindow.cs` | `OverlayWindow` config (position, size, opacity, chrome, text align); `OverlayTextAlign` enum |
| `Core/Services/TriggerEngine.cs` | Phrase/regex matching, cooldown, `{S0}/{GroupName}/{C}/{L}` substitution, `TriggerFired` event |
| `Infrastructure/Storage/TriggerStore.cs` | LiteDB CRUD for triggers + folders; `GetEnabledForEngine()` respects folder enabled state |
| `Infrastructure/Storage/OverlayWindowStore.cs` | LiteDB CRUD for overlay window configs |
| `App/ViewModels/TriggerTreeNodes.cs` | `TriggerFolderNode` (`Children: ObservableCollection<object>` holds sub-folders then triggers; `IsMarked` propagates to descendants) and `TriggerNode` (`IsEnabled` auto-saves; `IsMarked` for select-mode bulk delete) |
| `App/Services/NagImporter.cs` | One-shot importer from NAG `trigger-database.json`; reads sibling `overlays-database.json` to create matching EQ Flex overlays; builds real nested folder tree; splits triggers by phrase-set when actions target different phrases |

## Data files

Loaded from `src/EqFlex.Infrastructure/Data/` (Content → Copy Always):
- `spells.txt` — `^`-delimited. Key field indices: `[0]`=Id, `[1]`=Name, `[2]`=Level, `[3]`=DurationTicks, `[4]`=IsBeneficial, `[6]`=Target, `[7]`=ClassMask, `[8]`=Damaging, `[10]`=Resist, `[11]`=SongWindow, `[14]`=Rank, `[17]`=LandsOnYou, `[18]`=LandsOnOther, `[19]`=WearOff
- `npcs.txt` — one NPC name per line, O(1) lookup via `HashSet`
- `petnames.txt` — syllables; pet names are two-syllable combos
- `procs.txt` — spell names that are procs (`SpellData.Proc = 1`)
- `titles.txt` — `ClassName=Title1,Title2,...` format, maps player titles → class

## Critical design decisions

### Timestamps
All timestamps are **Unix seconds from the full log date** — not seconds-of-day. `LogProcessor` builds `(long)(new DateTime(yr,mon,day,h,m,s,Unspecified) - UnixEpoch).TotalSeconds`. Fight `StartTime`, `LastTime`, `EndTime`, and `HealingRecord.Timestamp` all use the same epoch.

### DPS vs SDPS
- **DPS** = `Damage / PlayerStats.ParsedSeconds` (player's first-swing to last-swing window)
- **SDPS** = `Damage / Fight.DurationSeconds` (full fight clock)
- Duration: `LastTime - StartTime + 1` (+1 matches EQLP's `TimeSegment.Total`)
- `PlayerStats.Hits` — real hits only (`Total > 0`); `PlayerStats.Attempts` — all swings including misses

### Healing attribution
Heals go into `FightManager._healStore` (global, timestamped). Attribution happens on-demand via `ComputeHealingForRange(startTime, lastTime)` when a fight is selected. Snapshot pattern (copy under lock, iterate copy) prevents blocking the consumer thread.

### Thread safety — Fight dictionaries
`Fight.PlayerStats`, `Fight.PlayerTankStats`, and `PlayerStats.Abilities` are **`ConcurrentDictionary`** — the consumer thread writes while the UI thread may iterate during `RefreshFromSelection` or overlay refresh. Write paths use `GetOrAdd`. Plain `Dictionary` caused `InvalidOperationException` crashes and inflated numbers.

### Replay batching
1. `_fightManager.BeginReplay()` → `_suppressEvents = true`
2. Replay loop on `Task.Run`
3. `await Task.Run(() => _processor.Stop())` — drains channel (consumer.Wait(3000))
4. `_fightManager.EndReplay()` — fires one batch of `FightUpdated`/`FightExpired`
5. Build fresh `LogProcessor`, start `LogTailer` for live tail

Do **not** poll `_channel.Reader.Count` — throws on `SingleReader=true,SingleWriter=true` channels.

### Session reset
`FightManager.BeginSession` fires `SessionStarted` event. `DamageViewModel` subscribes and calls `ClearFights()`. `OverlayViewModel` resets `_lastFight`. Without this, stopping and restarting tailing during a fight leaves old rows in the UI and new rows get added for the same NPC, appearing doubled when selected together.

### NPC vs player classification (`IsValidAttack`)
- `PlayerRegistry.IsPossiblePlayerName` — single word, all letters, 3–64 chars
- `_registry.IsPetOrPlayerOrMerc(name)` is the authority for confirmed allied entities
- `AttackerIsSpell=true` — attacker field is a spell name; checked against `_recentSpellCache`
- Do NOT call `_registry.AddVerifiedPlayer` from `DamageParser.MakeRecord` for all attackers

### Mercenary classification
Mercs have names starting with "a " or "an ". Three detection paths:
1. **Group join message** — `"a X has joined the group."` → `AddMerc`
2. **Combat inference** — `FightManager.IsValidAttack`: `LooksLikeMercName(attacker) && !_spells.IsNpc(attacker) && _active.ContainsKey(defender)` → `AddMerc` and allow. Also for tanking side and heals.
3. **"is now group Main Tank."** — `LogProcessor.ProcessGroupLine` (note: includes trailing period)

`FightManager.GetPlayerClass` returns `"MERC"` for registered mercs before checking the class map.

### Charm pet detection
Charmed mobs are treated identically to summoned pets (same `AddPetToPlayer` / `AttackerOwner` path). Detection pipeline in `LogProcessor`:

1. **`SpellDataService.LoadSpells`** builds `_charmLandingMap` (trimmed `LandsOnOther` suffix → spell name) for spells whose `LandsOnOther` contains charm keywords (charm, captivat, enthrall, beguile, servant of, under your control, thrall, tame, befriend).
2. **`CastParser.CastDetected`** → `LogProcessor.OnCastDetected` — if `_spells.IsCharmSpell(spell)`, records `caster → timestamp` in `_recentCharmCasts`.
3. **`LogProcessor.TryDetectCharmLanding`** — called after standard parser chain. Quick-bails on most lines (keyword check). If `_spells.TryMatchCharmLanding(action, out entityName)` matches, finds most recent charm caster within 30s, calls `registry.AddPetToPlayer(entityName, charmer)`.

**Three detection signals — earliest fires first:**
1. **`"[Pet] told you, 'Attacking X Master.'"` (`TryDetectPetCommunication`)** — earliest signal; fires when the charmed mob receives an attack command, before most combat lines. Registers the pet immediately via `AddPetToPlayer(petName, registry.PlayerName)`.
2. **LandsOnOther** (`TryDetectCharmLanding`) — `"a skeleton has been charmed."` suffix match against `_charmLandingMap`; requires recent charm spell cast in `_recentCharmCasts`.
3. **`"Your [Spell] spell has worn off of [Entity]."`** (`TryDetectCharmBreak`) — **caster-only** (confirmed from real logs; bystanders see nothing). `TryMatchCharmBreak` in `SpellDataService` parses this format and checks `_charmSpellNames`.

**Charm break — behavioral fallback**: `FightManager.IsValidAttack` — if registered pet attacks a `IsVerifiedPlayer` → `RemovePet()` + re-classify as NPC.

**Same-name combat**: `IsValidAttack` self-attack check exempts registered pets — `"A skeleton" vs "A skeleton"` is allowed when attacker is in `_pets` (`npcIsDefender = true`, early return before PvP check). Direction is ambiguous in the log; all attributed as pet-attacks-enemy.

**Attribution**: `RecordDamage` checks `_registry.GetPlayerFromPet(attacker)` for charmed pets after `record.AttackerOwner` for summoned pets.

**Known limitation**: Pet combat before the first `"told you"` message in the log is unrecoverable — charm was cast before the parse window with no earlier signal. Verified against `eqlog_Jolrael_vox.txt` in `codex-eq/example_logs/`.

### Class detection (three sources)
1. **`/who` output** — HIGH confidence. `LogProcessor.ProcessWhoLine` parses `[120 Bard] PlayerName`.
2. **Ability modifiers** — HIGH confidence. `DamageParser.MakeRecord`: `(Assassinate)` → ROG, `(Headshot)`/`(Double Bow Shot)` → RNG, `(Slay Undead)` → PAL.
3. **Class-exclusive spells** — LOW confidence, commits after 8 observations. `SpellData.ClassMask` single-bit → class abbreviation in `_spellClassMap`. Checked in `CastParser`.

Abbreviations: WAR, CLR, PAL, RNG, SHD, DRU, MNK, BRD, ROG, SHM, NEC, WIZ, MAG, ENC, BST, BER

## Overlay (`DamageMeterOverlay`)

Transparent always-on-top WPF window, no OS chrome.

- **Click-through** when locked: `WS_EX_TRANSPARENT | WS_EX_LAYERED` via `SetWindowLong`
- **Topmost enforcement**: `SetWindowPos(HWND_TOPMOST)` every 2s (game can steal topmost)
- **Resize**: `ResizeMode="CanResize"` + `WM_NCLBUTTONDOWN(HTBOTTOMRIGHT)` via `SendMessage` on grip drag
- **Three views**: DPS / Tank / Heal — toggled in the header; `OverlayViewModel.Mode` (`OverlayMode` enum)
- **Fight timer**: wall-clock `DispatcherTimer` ticks every second; freezes at `fight.DurationSeconds` on expiry
- **Auto-show/hide**: `OverlayViewModel.AutoShow` — opens on `FightUpdated`, starts `_autoHideTimer` on expiry; configurable delay (`AutoHideDelaySec`, persisted). Clicks on overlay reset the countdown.
- **Damage bars**: star-proportioned `Grid` columns (not fixed pixels) so bars scale with window width. `PercentToGridLengthConverter` in `App.xaml`.
- **Damage format**: exact comma-separated below 1M, `X.XM` above.
- **Bounds persistence**: position + size saved to LiteDB on drag/resize (500ms debounce) and on close.

`App.IsShuttingDown` static flag set in `shell.Closing` handler prevents `DamageMeterOverlay.OnClosing` from cancelling app exit.

## Trigger Overlays (`TriggerOverlay`)

Multi-window trigger overlay system. Each window is a separate `TriggerOverlay` (WPF `Window`, transparent, always-on-top) owned by `OverlayManager`.

- **`OverlayManager`** — singleton; creates windows from `OverlayWindowStore` on startup; routes `TriggerEngine.TriggerFired` → correct `TriggerOverlayViewModel.Dispatch()`; owns shared `SpeechSynthesizer` and `MediaPlayer`
- **`TriggerOverlayViewModel`** — holds `Alerts` (`TriggerAlertItem`) and `Timers` (`TriggerTimerItem`) collections; `DispatcherTimer` ticks every second, decrementing countdowns and removing expired items
- **Chrome toggle**: `ShowChrome` persisted per overlay. `ChromeVisible = ShowChrome || IsPreview` — header/border/resize grip all bind to this
- **Preview mode**: `IsPreview=true` inserts a sample alert and timer (marked `IsPreview=true` so the tick never expires them), unlocks the window for dragging, shows chrome regardless of `ShowChrome`
- **Background opacity**: `SolidColorBrush.Opacity` on the background brush only — text/timers remain fully opaque
- **Text alignment**: `OverlayTextAlign` enum (`Left`/`Center`/`Right`) persisted in `OverlayWindow.TextAlign`; exposed as `WpfTextAlignment` (`System.Windows.TextAlignment`) for XAML binding; applied to alert TextBlock and timer name label
- **Action routing**: `TriggerAction.OverlayId = 0` → first enabled overlay (default); non-zero → specific overlay by Id
- **Per-overlay settings** in `OverlayWindow`: `BackgroundOpacity`, `ShowChrome`, `TextAlign`, position/size

### Trigger folder tree
`TriggerFolder.ParentFolderId = 0` means root level. Any other value nests the folder under the folder with that Id. `TriggersViewModel.Reload()` builds the tree recursively using parent-child grouping. `TriggerFolderNode.Children` is a mixed `ObservableCollection<object>` — sub-folders come first, then `TriggerNode` leaf items. WPF's `HierarchicalDataTemplate` applies the correct template per type automatically, so nesting is unlimited depth.

- **Adding a trigger folder**: `AddFolderCommand` — creates under `_selectedFolderNode` (subfolder) or root if nothing selected
- **Deleting**: `DeleteSelectedCommand` recursively collects all trigger IDs and child folder IDs via `CollectSubtreeIds` before deleting
- **Select mode**: toolbar "☑ Select" toggle shows `IsMarked` checkboxes on every node; checking a folder propagates to all descendants; "Delete Marked" confirms with count and calls `DeleteMarked()` which collects marked triggers + marked folder subtrees
- **Engine filtering**: `ReloadEngine()` recursively walks the tree and respects both `FolderEnabled` (parent) and `IsEnabled` (trigger); a disabled parent folder suppresses all children

### NAG trigger import (`App/Services/NagImporter.cs`)
Reads `trigger-database.json` from `%APPDATA%\electron-angular-eq-parse\`. "Import NAG…" button in the Triggers toolbar.

- **Folders**: `CreateFolderTree()` recurses through NAG's folder tree and creates real EQ Flex `TriggerFolder` records with correct `ParentFolderId` — the full hierarchy is preserved
- **Overlays**: reads sibling `overlays-database.json`; creates EQ Flex overlays for NAG `"Alert"` and `"Timer"` type overlays (deduplicates by name on repeat imports); FCT overlays are skipped. `OverlayId` on each imported action is mapped from NAG UUID → EQ Flex int
- **Phrase-action split**: NAG actions carry a `phrases` list specifying which capture-phrase IDs fire them. Since EQ Flex fires all actions on any match, actions are grouped by their phrase set — each unique group becomes a separate EQ Flex trigger (suffixed `(1)`, `(2)`, …)
- **Token mapping**: NAG `{s}` → `{L}`, `{1}`/`{2}` → `{S1}`/`{S2}`, `${VarName}` → `{VarName}`
- **Skipped action types**: StoreVariable (5), Clipboard (9), FCT (9+), counter — no EQ Flex equivalent
- **Audio**: PlayAudio actions import with empty `AudioPath` — NAG uses internal IDs, not file paths

### Performance — Triggers page
`TriggersViewModel` and `OverlaysViewModel` are registered as **singletons** (`AddSingleton`). The tree is built once on first navigation and stays in memory; subsequent navigations are instant. `Reload()` is `async void` — LiteDB queries (`GetAllFolders()` + `GetAll()`) run on a `Task.Run` background thread while the UI shows "Loading triggers…". Tree building and collection updates happen on the UI thread after the `await`.

### Adding a new per-overlay setting
1. Add property to `OverlayWindow` (Core/Models)
2. Add `[ObservableProperty]` to `TriggerOverlayViewModel`; load from `config` in constructor
3. Add `partial void OnXChanged` → sync `Config.X`, call `SaveRequested?.Invoke(Config)`, notify any computed properties
4. Add UI control in `OverlaysView.xaml` overlay DataTemplate (items are `TriggerOverlayViewModel`)
5. Bind in `TriggerOverlay.xaml` via `{Binding X}` (DataContext is the VM)

## Trigger engine

`TriggerEngine` in `Core/Services`:
- Holds a list of enabled `Trigger` objects loaded via `LoadTriggers()`
- `Process(string line, long timestamp)` — called from `LogProcessor` after combat parsing; strips timestamp prefix before matching
- Each `Trigger` has a list of `CapturePhrase` (text or regex). Any phrase match fires all `TriggerAction`s
- Cooldown tracked per trigger; actions fired only if `timestamp - lastFired >= CooldownSec`
- `TriggerFiredArgs` record: `(Trigger, TriggerAction, string Line, IReadOnlyDictionary<string,string> Captures)`
- Token substitution: `{S0}` = full match, `{S1}`…`{Sn}` = numbered groups, `{GroupName}` = named groups, `{C}` = player name, `{L}` = full line

### Action types
| Type | Behaviour |
|------|-----------|
| `DisplayText` | Adds `TriggerAlertItem` to target overlay; expires after `DurationSec` |
| `Timer` | Adds/replaces `TriggerTimerItem` bar in target overlay; countdown from `DurationSec` |
| `Speak` | TTS via `SpeechSynthesizer`; `SpeakInterrupt=true` cancels previous speech |
| `PlayAudio` | Opens + plays `AudioPath` via `MediaPlayer` |

## Parse time range

`LogViewModel.TimeRangeOptions` (Last hour / Last 8h / Last 24h / Last 2 days / Last 7 days / All). Cutoff passed to `LogTailer.ReplayAsync(cutoffTimestamp)`. Lines before cutoff are skipped (progress bar still advances from actual bytes read). Default: All.

## Layout persistence

`DamageView.xaml.cs` saves/restores via `AppSettings.LayoutWidths: Dictionary<string, double>` (LiteDB). Keys: `"splitter.left"`, `"fight.col.N"`, `"players.col.N"`, `"abilities.col.N"`, `"tanking.col.N"`, `"healers.col.N"`, `"healspells.col.N"`. Saved on `UserControl.Unloaded` (navigation + app close); restored at `DispatcherPriority.Loaded`.

## Common patterns

### Adding a new parser feature
1. Check EQLP at `C:\Users\louie\dev-projects\EQLogParser\src\parsing\`
2. Add pattern to `Core/Parsing/*.cs`
3. Wire into `LogProcessor.ProcessLine` routing (damage → healing → cast → charm)
4. Tests in `tests/EqFlex.Tests/`

### Adding a new overlay column / stat
Update `OverlayPlayerRow` record in `OverlayViewModel.cs`, populate in `BuildRows<T>`, update `DamageMeterOverlay.xaml` DataTemplate.

### Adding a new UI column to main window
1. Add field to row record in `DamageViewModel.cs`
2. Populate in `RefreshFromSelection`
3. Add `<DataGridTextColumn>` in `DamageView.xaml`
4. Column width auto-persists via the existing `DamageView.xaml.cs` save/restore

### Adding a new persisted setting
Add property to `AppSettings` in `SettingsStore.cs`. LiteDB handles serialization automatically including `Dictionary<string, double>`.

### Value converters in `App/Controls/Converters.cs`
All registered as static resources in `App.xaml`:

| Key | Class | Use |
|-----|-------|-----|
| `BoolToVisConverter` | `BoolToVisibilityConverter` | `bool → Visibility` |
| `InverseBoolToVisConverter` | `InverseBoolToVisibilityConverter` | `!bool → Visibility` |
| `InverseBoolConverter` | `InverseBoolConverter` | `!bool → bool` |
| `NotNullToVisConverter` | `NotNullToVisibilityConverter` | `obj != null → Visible` |
| `BoldConverter` | `BoolToFontWeightConverter` | `bool → Bold/Normal` |
| `PctToStarConverter` | `PercentToGridLengthConverter` | `double (0–100) → GridLength star`; `ConverterParameter=remainder` gives `(100-pct)*` |
| `StringToBrushConverter` | `StringToBrushConverter` | `"#AARRGGBB" string → SolidColorBrush`; returns gray on parse failure |
| `EnumToBoolConverter` | `EnumToBoolConverter` | `Enum → bool` for RadioButton `IsChecked`; `ConverterParameter` = member name string |

### Build and test
```
dotnet build       # from repo root
dotnet test        # 24 tests, ~45ms
```

## WPF gotchas (learned the hard way)

- **`Cursor="Crosshair"` is invalid** — the correct XAML name is `Cursor="Cross"`. BAML doesn't validate cursor names at compile time; `CursorConverter.ConvertFromString` throws `NotSupportedException` at runtime, which surfaces as a hang-then-crash on first navigation to the view.
- **`static readonly` fields are not bindable** — `{Binding MyStaticField}` silently produces no items. Use an instance property (`public T Foo { get; } = ...`) or `{x:Static}`.
- **`Color="{Binding}"` inside `SolidColorBrush` doesn't auto-convert strings** — use a converter on the parent element's `Background`/`Fill` instead: `Background="{Binding Converter={StaticResource StringToBrushConverter}}"`.
- **`OnSelectedHexChanged` (DP callback) fires before `Loaded`** — during BAML init, bindings establish and fire property-changed callbacks before the `Loaded` event. Guard any code that touches named child elements with `if (!IsLoaded) return;`.
- **`Effect="{x:Null}"` on shapes** — can trigger unexpected behaviour in WPF's hardware renderer on some drivers. Avoid; the default (no effect) is already null.
- **RadioButton grouping in DataTemplate** — RadioButtons without explicit `GroupName` are grouped by their containing Panel. Each DataTemplate instantiation creates its own Panel, so grouping is automatically per-row. Use `EnumToBoolConverter` with `ConverterParameter=MemberName` for enum binding.
- **`DisplayMemberPath` doesn't work with a custom `ComboBox` ControlTemplate** — WPF only auto-generates `SelectionBoxItemTemplate` through an internal code path that works with the default template. With a fully custom template, the selection box falls back to `ToString()`. Fix: replace `DisplayMemberPath` with an explicit `<ComboBox.ItemTemplate>` DataTemplate — WPF always maps an explicit `ItemTemplate` to `SelectionBoxItemTemplate` unconditionally.
- **DataGrid column layout saves phantom `MinWidth` values** — `DataGridColumn.ActualWidth` returns `20` (the WPF default `MinWidth`) for columns in tabs that have never been rendered (e.g., Tanking/Healing if the user never visited those tabs). Saving `w > 0` persists these phantom values; restoring them makes every column 20px. Guard both save and restore with `w > 40` and skip saving if `grid.ActualWidth < 50`.
- **`TriggersViewModel` / `OverlaysViewModel` must be singletons** — registering as `AddTransient` means a full LiteDB reload on every navigation. With 1000+ triggers this causes a multi-second freeze. Use `AddSingleton`.

## Still pending

- **FCT (Floating Combat Text)** — animated canvas text overlay
- **Trade Mode** — /barter and /bazaar log parsing
- **ADPS tracking** — buff attribution per fight
- **Per-phrase action mapping** — NAG supports mapping specific actions to specific phrases within one trigger; EQ Flex fires all actions on any match. Workaround: importer splits into multiple triggers. Native support would require `CapturePhrase.Id` + `TriggerAction.PhraseIds` + updated engine + new editor UI (~half day).
- **NAG audio import** — PlayAudio actions import with empty `AudioPath`; NAG stores audio as internal IDs, not file paths. User must set paths manually.
- **Full MiscLineParser port** — group join/raid join done; broader zone/death events not done
- **Log archiving** — `CharacterProfile.AutoRenameLog` + `TryRenameLog` implemented in `LogViewModel` but not wired to a UI toggle
