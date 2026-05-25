using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EqFlex.App.ViewModels;
using EqFlex.Core.Models;
using EqFlex.Infrastructure.Storage;
using TriggerActionType = EqFlex.Core.Models.TriggerActionType;

namespace EqFlex.App.Services;

/// <summary>
/// Serialize/upload/fetch/import EQ Flex trigger share packages via the Cloudflare Worker backend.
///
/// Worker URL: update WorkerBaseUrl after deploying cloudflare/worker.js.
/// POST /share  → { "code": "ABC12345" }
/// GET  /share/{code} → TriggerSharePackage JSON
/// </summary>
public sealed class TriggerShareService
{
    internal const string WorkerBaseUrl = "https://eq-flex-share.eqflex.workers.dev";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly HttpClient _http;
    private readonly TriggerStore _store;
    private readonly OverlayManager _overlayManager;

    public TriggerShareService(HttpClient http, TriggerStore store, OverlayManager overlayManager)
    {
        _http = http;
        _store = store;
        _overlayManager = overlayManager;
    }

    // ── Serialize ─────────────────────────────────────────────────────────────

    public TriggerSharePackage Serialize(TriggerFolderNode rootFolder, string author)
    {
        return new TriggerSharePackage
        {
            Author    = author.Trim(),
            CreatedAt = DateTime.UtcNow,
            Folders   = [SerializeFolder(rootFolder)],
        };
    }

    private ShareFolder SerializeFolder(TriggerFolderNode node)
    {
        var sf = new ShareFolder { Name = node.FolderName, IsEnabled = node.FolderEnabled };
        foreach (var child in node.Children)
        {
            if (child is TriggerFolderNode sub) sf.Children.Add(SerializeFolder(sub));
            else if (child is TriggerNode tn)   sf.Triggers.Add(SerializeTrigger(tn));
        }
        return sf;
    }

    private ShareTrigger SerializeTrigger(TriggerNode node)
    {
        var t = node.Trigger;
        return new ShareTrigger
        {
            Name        = t.Name,
            IsEnabled   = t.IsEnabled,
            CooldownSec = t.CooldownSec,
            Phrases = t.Phrases.Select(p => new SharePhrase { Pattern = p.Pattern, UseRegex = p.UseRegex }).ToList(),
            Actions = t.Actions.Select(a =>
            {
                var overlayName = a.OverlayId > 0
                    ? _overlayManager.Overlays.FirstOrDefault(v => v.OverlayId == a.OverlayId)?.OverlayName ?? string.Empty
                    : string.Empty;
                return new ShareAction
                {
                    ActionType    = a.ActionType.ToString(),
                    Text          = a.Text,
                    DurationSec   = a.DurationSec,
                    TextColor     = a.TextColor,
                    FontSize      = a.FontSize,
                    IsBold        = a.IsBold,
                    TimerBarColor = a.TimerBarColor,
                    AudioPath     = a.AudioPath,
                    SpeakInterrupt = a.SpeakInterrupt,
                    OverlayName   = overlayName,
                };
            }).ToList(),
        };
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task<string> UploadAsync(TriggerSharePackage package, CancellationToken ct = default)
    {
        var json    = JsonSerializer.Serialize(package, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{WorkerBaseUrl}/share", content, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ShareCodeResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("No response from share server.");
        return result.Code;
    }

    private sealed record ShareCodeResponse(string Code);

    // ── Fetch ─────────────────────────────────────────────────────────────────

    public async Task<TriggerSharePackage?> FetchAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();
        var resp = await _http.GetAsync($"{WorkerBaseUrl}/share/{code}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<TriggerSharePackage>(JsonOpts, ct);
    }

    // ── Import ────────────────────────────────────────────────────────────────

    public sealed record ImportResult(int Folders, int Triggers);

    public ImportResult Import(TriggerSharePackage package, int parentFolderId = 0)
    {
        int folders = 0, triggers = 0;
        foreach (var sf in package.Folders)
            ImportFolder(sf, parentFolderId, ref folders, ref triggers);
        return new ImportResult(folders, triggers);
    }

    private void ImportFolder(ShareFolder sf, int parentId, ref int folders, ref int triggers)
    {
        var folder = new TriggerFolder { Name = sf.Name, IsEnabled = sf.IsEnabled, ParentFolderId = parentId };
        _store.SaveFolder(folder);
        folders++;

        foreach (var st in sf.Triggers)
        {
            _store.Save(MapTrigger(st, folder.Id));
            triggers++;
        }
        foreach (var child in sf.Children)
            ImportFolder(child, folder.Id, ref folders, ref triggers);
    }

    private Trigger MapTrigger(ShareTrigger st, int folderId) => new()
    {
        Name        = st.Name,
        IsEnabled   = st.IsEnabled,
        FolderId    = folderId,
        CooldownSec = st.CooldownSec,
        Phrases     = st.Phrases.Select(p => new CapturePhrase { Pattern = p.Pattern, UseRegex = p.UseRegex }).ToList(),
        Actions     = st.Actions.Select(MapAction).ToList(),
    };

    private TriggerAction MapAction(ShareAction sa)
    {
        var overlayId = 0;
        if (!string.IsNullOrWhiteSpace(sa.OverlayName))
        {
            var match = _overlayManager.Overlays
                .FirstOrDefault(v => string.Equals(v.OverlayName, sa.OverlayName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) overlayId = match.OverlayId;
        }
        Enum.TryParse<TriggerActionType>(sa.ActionType, out var actionType);
        return new TriggerAction
        {
            ActionType    = actionType,
            Text          = sa.Text,
            DurationSec   = sa.DurationSec > 0 ? sa.DurationSec : 5,
            TextColor     = string.IsNullOrEmpty(sa.TextColor) ? "#FFD4D4D4" : sa.TextColor,
            FontSize      = sa.FontSize > 0 ? sa.FontSize : 13,
            IsBold        = sa.IsBold,
            TimerBarColor = sa.TimerBarColor,
            AudioPath     = sa.AudioPath,
            SpeakInterrupt = sa.SpeakInterrupt,
            OverlayId     = overlayId,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static int CountTriggers(TriggerFolderNode node)
    {
        int n = 0;
        foreach (var child in node.Children)
        {
            if (child is TriggerNode) n++;
            else if (child is TriggerFolderNode sub) n += CountTriggers(sub);
        }
        return n;
    }

    public static string ExtractCode(string input)
    {
        // Accept "{FLEX:share/ABC12345}" or just "ABC12345"
        const string prefix = "{FLEX:share/";
        var idx = input.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var start = idx + prefix.Length;
            var end = input.IndexOf('}', start);
            return end > start ? input[start..end].Trim().ToUpperInvariant() : string.Empty;
        }
        return input.Trim().ToUpperInvariant();
    }
}
