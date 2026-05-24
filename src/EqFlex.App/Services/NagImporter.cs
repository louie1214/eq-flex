using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using EqFlex.Core.Models;
using EqFlex.Infrastructure.Storage;

namespace EqFlex.App.Services;

/// <summary>
/// One-shot importer for eq-nag's trigger-database.json format.
///
/// NAG actions carry a "phrases" list that says which specific capture-phrase IDs trigger each
/// action.  EQ Flex doesn't have per-action phrase mapping — when any phrase matches, all
/// actions fire.  To preserve NAG semantics, actions are grouped by their phrase set and each
/// unique group becomes a separate EQ Flex trigger (suffixed "(1)", "(2)", … when split).
/// </summary>
public sealed partial class NagImporter
{
    private const int NagDisplayText  = 0;
    private const int NagPlayAudio    = 1;
    private const int NagSpeak        = 2;
    private const int NagTimer        = 3;
    private const int NagBenTimer     = 4;   // upward-counting / beneficial timer
    // 5  = StoreVariable  — no equivalent, skip
    private const int NagDisplayVar   = 7;   // displayText with stored-variable ref → DisplayText
    // 9  = FCT/Clipboard  — no equivalent, skip
    private const int NagTimerVariant = 13;  // another timer shape

    public sealed record ImportResult(
        int Imported, int Skipped, int FoldersCreated, int OverlaysCreated, string? Error = null);

    public static ImportResult Import(string jsonPath, TriggerStore store, OverlayManager overlayManager)
    {
        try
        {
            using var fs  = File.OpenRead(jsonPath);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;

            // ── 1. Build overlay map: NAG UUID → EQ Flex overlay Id ─────────────
            var (overlayMap, overlaysCreated) = BuildOverlayMap(jsonPath, overlayManager);

            // ── 2. Build real EQ Flex folder tree mirroring the NAG hierarchy ────
            var nagIdToFlex = new Dictionary<string, TriggerFolder>(StringComparer.Ordinal);
            if (root.TryGetProperty("folders", out var foldersEl) &&
                foldersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in foldersEl.EnumerateArray())
                    CreateFolderTree(f, parentFlexId: 0, nagIdToFlex, store);
            }

            // Fallback folder for triggers whose folderId isn't in the tree
            TriggerFolder? fallback = null;
            TriggerFolder Fallback()
            {
                if (fallback != null) return fallback;
                fallback = new TriggerFolder { Name = "Imported from NAG", ParentFolderId = 0 };
                store.SaveFolder(fallback);
                return fallback;
            }

            TriggerFolder GetFolder(string? nagId) =>
                nagId != null && nagIdToFlex.TryGetValue(nagId, out var f) ? f : Fallback();

            // ── 3. Map triggers ──────────────────────────────────────────────────
            if (!root.TryGetProperty("triggers", out var triggersEl) ||
                triggersEl.ValueKind != JsonValueKind.Array)
                return new ImportResult(0, 0, nagIdToFlex.Count, overlaysCreated);

            int imported = 0, skipped = 0;

            foreach (var nt in triggersEl.EnumerateArray())
            {
                var results = MapTriggers(nt, id => GetFolder(id), overlayMap).ToList();
                if (results.Count == 0) { skipped++; continue; }
                foreach (var t in results) { store.Save(t); imported++; }
            }

            return new ImportResult(
                imported, skipped,
                nagIdToFlex.Count + (fallback != null ? 1 : 0),
                overlaysCreated);
        }
        catch (Exception ex)
        {
            return new ImportResult(0, 0, 0, 0, ex.Message);
        }
    }

    // ── Overlay map builder ───────────────────────────────────────────────────

    /// <summary>
    /// Reads overlays-database.json from the same directory as trigger-database.json.
    /// Creates EQ Flex overlays for each NAG Alert/Timer overlay, deduplicating by name.
    /// Returns (nagUUID → flexId map, count of newly created overlays).
    /// </summary>
    private static (Dictionary<string, int> Map, int Created) BuildOverlayMap(
        string triggerDbPath, OverlayManager overlayManager)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var overlayDbPath = Path.Combine(
            Path.GetDirectoryName(triggerDbPath)!, "overlays-database.json");

        if (!File.Exists(overlayDbPath)) return (map, 0);

        using var fs  = File.OpenRead(overlayDbPath);
        using var doc = JsonDocument.Parse(fs);
        var root = doc.RootElement;

        if (!root.TryGetProperty("overlays", out var overlaysEl) ||
            overlaysEl.ValueKind != JsonValueKind.Array)
            return (map, 0);

        int created = 0;

        foreach (var ov in overlaysEl.EnumerateArray())
        {
            var nagId = Str(ov, "overlayId");
            var name  = Str(ov, "name");
            var type  = Str(ov, "overlayType");

            if (nagId is null || name is null) continue;

            // FCT overlays have no EQ Flex equivalent — skip them.
            if (type is not ("Alert" or "Timer")) continue;

            // Reuse an existing overlay with the same name rather than creating duplicates.
            var existing = overlayManager.Overlays
                .FirstOrDefault(v => string.Equals(v.OverlayName, name, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                map[nagId] = existing.OverlayId;
            }
            else
            {
                var vm = overlayManager.CreateOverlay(name);
                map[nagId] = vm.OverlayId;
                created++;
            }
        }

        return (map, created);
    }

    // ── Folder tree builder ───────────────────────────────────────────────────

    /// <summary>
    /// Recursively creates EQ Flex TriggerFolder objects matching the NAG folder tree.
    /// Each folder is saved immediately so LiteDB assigns its Id before children reference it.
    /// </summary>
    private static void CreateFolderTree(JsonElement folder, int parentFlexId,
        Dictionary<string, TriggerFolder> nagIdToFlex, TriggerStore store)
    {
        var nagId    = Str(folder, "folderId");
        var name     = Str(folder, "name") ?? "Unknown";
        var isActive = !folder.TryGetProperty("active", out var act) || act.GetBoolean();

        var flex = new TriggerFolder { Name = name, IsEnabled = isActive, ParentFolderId = parentFlexId };
        store.SaveFolder(flex);   // LiteDB assigns flex.Id here

        if (nagId != null) nagIdToFlex[nagId] = flex;

        // Recurse into children (NAG may serialise an empty list as a whitespace string)
        if (folder.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
            foreach (var child in ch.EnumerateArray())
                CreateFolderTree(child, flex.Id, nagIdToFlex, store);
    }

    // ── Trigger mapper — yields ≥1 triggers due to phrase-set splitting ───────

    private static IEnumerable<Trigger> MapTriggers(JsonElement nt, Func<string?, TriggerFolder> getFolder,
        IReadOnlyDictionary<string, int> overlayMap)
    {
        var name     = Str(nt, "name") ?? "Imported Trigger";
        var enabled  = !nt.TryGetProperty("enabled",         out var en) || en.GetBoolean();
        var folderId = Str(nt, "folderId");
        var cooldown = nt.TryGetProperty("useCooldown",      out var uc) && uc.GetBoolean()
                       && nt.TryGetProperty("cooldownDuration", out var cd)
                       ? cd.GetDouble() : 0;

        // ── Build phraseId → CapturePhrase ──────────────────────────────────
        var phraseMap = new Dictionary<string, CapturePhrase>(StringComparer.Ordinal);
        if (nt.TryGetProperty("capturePhrases", out var cps) &&
            cps.ValueKind == JsonValueKind.Array)
        {
            foreach (var cp in cps.EnumerateArray())
            {
                var pid     = Str(cp, "phraseId") ?? "";
                var pattern = Str(cp, "phrase")   ?? "";
                var useRx   = cp.TryGetProperty("useRegEx", out var rx) && rx.GetBoolean();
                if (!string.IsNullOrWhiteSpace(pid) && !string.IsNullOrWhiteSpace(pattern))
                    phraseMap[pid] = new CapturePhrase { Pattern = pattern, UseRegex = useRx };
            }
        }
        if (phraseMap.Count == 0) yield break;

        // ── Group actions by their phrase set ────────────────────────────────
        // Each action carries a "phrases" list (the NAG phraseIds it fires on).
        // Actions that share the same phrase set can live in the same EQ Flex trigger.
        // Actions with a different phrase set must become a separate trigger.
        var allPhraseIds = new HashSet<string>(phraseMap.Keys, StringComparer.Ordinal);

        // key = sorted, pipe-joined set of phraseIds → (phraseId set, mapped actions)
        var groups = new Dictionary<string, (HashSet<string> PhraseIds, List<TriggerAction> Actions)>(
            StringComparer.Ordinal);

        if (nt.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array)
        {
            foreach (var act in acts.EnumerateArray())
            {
                var mapped = MapAction(act, overlayMap);
                if (mapped is null) continue;

                // Collect the phraseIds this action references, filtered to known phrases
                var actionPhraseIds = new HashSet<string>(StringComparer.Ordinal);
                if (act.TryGetProperty("phrases", out var phs) && phs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in phs.EnumerateArray())
                    {
                        var pid = p.GetString();
                        if (pid is not null && phraseMap.ContainsKey(pid))
                            actionPhraseIds.Add(pid);
                    }
                }

                // An action with no phrase mapping fires for all phrases
                if (actionPhraseIds.Count == 0)
                    actionPhraseIds = new HashSet<string>(allPhraseIds, StringComparer.Ordinal);

                var key = string.Join("|", actionPhraseIds.OrderBy(x => x));
                if (!groups.TryGetValue(key, out var group))
                {
                    group = (actionPhraseIds, []);
                    groups[key] = group;
                }
                group.Actions.Add(mapped);
            }
        }

        if (groups.Count == 0) yield break;

        var folder    = getFolder(folderId);
        var multiPart = groups.Count > 1;
        var partNum   = 1;

        foreach (var (_, (phraseIds, actions)) in groups)
        {
            var phrases = phraseIds
                .Where(phraseMap.ContainsKey)
                .Select(id => phraseMap[id])
                .ToList();

            if (phrases.Count == 0 || actions.Count == 0) continue;

            yield return new Trigger
            {
                Name        = multiPart ? $"{name} ({partNum++})" : name,
                IsEnabled   = enabled,
                FolderId    = folder.Id,
                CooldownSec = cooldown,
                Phrases     = phrases,
                Actions     = actions,
            };
        }
    }

    // ── Action mapper ─────────────────────────────────────────────────────────

    private static TriggerAction? MapAction(JsonElement act, IReadOnlyDictionary<string, int> overlayMap)
    {
        if (!act.TryGetProperty("actionType", out var atEl)) return null;
        var type     = atEl.GetInt32();
        var text     = ConvertTokens(Str(act, "displayText") ?? "");
        var duration = act.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number
            ? dur.GetDouble() : 0;

        // Resolve NAG overlayId → EQ Flex int; null or unmapped → 0 (default overlay).
        var nagOverlayId = Str(act, "overlayId");
        var overlayId = nagOverlayId != null && overlayMap.TryGetValue(nagOverlayId, out var oid) ? oid : 0;

        return type switch
        {
            NagDisplayText or NagDisplayVar => new TriggerAction
            {
                ActionType  = TriggerActionType.DisplayText,
                Text        = text,
                DurationSec = duration > 0 ? duration : 5,
                OverlayId   = overlayId,
            },
            NagSpeak => new TriggerAction
            {
                ActionType = TriggerActionType.Speak,
                Text       = text,
            },
            NagPlayAudio => new TriggerAction
            {
                ActionType = TriggerActionType.PlayAudio,
                // NAG stores audio as internal IDs, not file paths — user must set AudioPath.
                AudioPath  = string.Empty,
            },
            NagTimer or NagBenTimer => new TriggerAction
            {
                ActionType  = TriggerActionType.Timer,
                Text        = text,
                // NagBenTimer with duration=0 is an upward-counting stopwatch → use 60s.
                DurationSec = duration > 0 ? duration : 60,
                OverlayId   = overlayId,
            },
            NagTimerVariant when !string.IsNullOrWhiteSpace(text) && duration > 0 => new TriggerAction
            {
                ActionType  = TriggerActionType.Timer,
                Text        = text,
                DurationSec = duration,
                OverlayId   = overlayId,
            },
            _ => null,  // StoreVariable, Clipboard, FCT, counter, etc.
        };
    }

    // ── Token conversion ──────────────────────────────────────────────────────

    /// <summary>
    /// NAG → EQ Flex substitution token mapping:
    ///   {s}          → {L}            (full matched line)
    ///   {1},{2},...  → {S1},{S2},...  (numbered capture groups)
    ///   ${VarName}   → {VarName}      (stored variable → named capture group)
    /// </summary>
    private static string ConvertTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = text.Replace("{s}", "{L}", StringComparison.OrdinalIgnoreCase);
        text = NumberedGroupRx().Replace(text, m => $"{{S{m.Groups[1].Value}}}");
        text = VariableRefRx().Replace(text,   m => $"{{{m.Groups[1].Value}}}");
        return text;
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex NumberedGroupRx();

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex VariableRefRx();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
