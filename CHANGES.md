## What's New in 1.2.3

### TLP Tunnel improvements
- Click any trade row to open a detail pane showing full item stats, auction metadata, and raw log line
- Detail pane is resizable via a drag splitter; width is remembered between sessions
- When an item name maps to multiple Lucy item IDs (same name re-used across expansions), the detail pane shows stats for every version
- Scroll position is now anchored when new trades arrive while viewing an entry

### Damage meter
- Consolidated copy toolbar: single Copy button with a Damage / Tanking / Healing selector, shared Top N and Fields controls

### Class detection
- /who lines with player titles (e.g. `[65 Overlord (Warrior)]`) now correctly resolve the class from inside the parentheses
- AFK prefix on /who lines no longer causes the line to be skipped
- Race suffix in player names (e.g. `Bloodydagger(Iksar)`) is now stripped before name validation
- Anonymous players on /who are now registered even when class is unknown
- Level cap for /who parsing raised to 200

## What's New in 1.2.2

### Fixes
- Fight duration now correct when an NPC name collides with a player character name on the server (e.g. a player named "Terror" causing the NPC Terror to be registered as a player via a /who line, which rejected all Terror combat as PvP and froze the fight timer at a few seconds)

## What's New in 1.2.1

### Fixes
- Overlay dropdown in the trigger editor now populates correctly

## What's New in 1.2.0

### TLP Tunnel (Trade Mode)
- New Tunnel section in the sidebar — parses auction channel trade broadcasts in real time
- Trades tab: live feed of WTS/WTB lines with per-item rows, type/seller/price columns, search and type filter
- Hover any item name for on-demand stats pulled from Lucy (slot, AC, HP, Mana, stats, classes)
- Krono tab: live Krono price from tlp-auctions.com with local sale history
- Prices tab: search items against local trade data or fall back to the tlp-auctions.com API; "Include API" checkbox forces both sources simultaneously
- Enable Trade Mode per character profile from the Characters page
- Note: tlp-auctions.com plans to support the Frostreaver server around June 3rd — Krono prices and item lookups for that server may not be available until then

### Item Alerts
- Keyword-based alerts that fire when matching WTS/WTB lines appear in the live log
- Optional max price cap — supports platinum, Krono, or combined limits (e.g. `1kr 5000pp`)
- Krono rate comparison: set a PP-per-Krono rate (auto-filled from the Krono tab) so mixed-currency caps are compared as a single total equivalent
- Shared display and sound settings for all alerts — text color, font size, bold, overlay target, and audio file via the "Display and Sound" panel in the Alerts toolbar
- Alert hit history persisted for 14 days

### Character Profiles
- Selecting a profile now shows a read-only detail card (name, character, server, log path, parsing options)
- Profile action buttons (New, Edit, Delete, Set Active) moved above the list
- Parse Damage, Healing, and Casting options consolidated into a single "Parse Combat" toggle

### Quality of Life
- App now starts live tailing automatically on launch when a character profile was previously active
- Tooltips throughout the app now use the dark theme instead of the system light yellow default
- Removed non-functional global parsing checkboxes from the Settings page

### Fixes
- Consumer thread now survives individual line-parse exceptions without stopping trade or combat processing
- Alerts DataGrid virtualization disabled to prevent a WPF layout crash that blocked the dispatcher

## What's New in 1.1.0

### Trigger Sharing
- Share any trigger folder with teammates using an 8-character code (`{FLEX:share/XXXXXXXX}`)
- Import a shared pack by pasting the code in the Triggers toolbar — or EQ Flex detects share codes automatically when they appear in your log and prompts you to import
- Shared packs expire after 90 days; codes refresh the timer each time they're fetched

### Setup Wizard
- First-launch wizard walks new users through creating a character profile and picking a log file

### Fixes and Polish
- Trigger overlay expander always visible on empty folders
- Various stability improvements
