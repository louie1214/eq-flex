using EqFlex.Core.Models;

namespace EqFlex.Infrastructure.Storage;

public sealed class TriggerStore
{
    private const string TriggerCol = "triggers";
    private const string FolderCol  = "trigger_folders";
    private readonly LiteDbContext _ctx;

    public TriggerStore(LiteDbContext ctx) => _ctx = ctx;

    // ── Triggers ──────────────────────────────────────────────────────────────

    public IReadOnlyList<Trigger> GetAll()
    {
        var col = _ctx.Db.GetCollection<Trigger>(TriggerCol);
        col.EnsureIndex(x => x.Id);
        return col.FindAll().OrderBy(t => t.Name).ToList();
    }

    /// <summary>Returns only enabled triggers inside enabled folders — for seeding TriggerEngine.</summary>
    public IReadOnlyList<Trigger> GetEnabledForEngine()
    {
        var enabledFolderIds = GetAllFolders().Where(f => f.IsEnabled).Select(f => f.Id).ToHashSet();
        return GetAll().Where(t => t.IsEnabled && enabledFolderIds.Contains(t.FolderId)).ToList();
    }

    public void Save(Trigger trigger) =>
        _ctx.Db.GetCollection<Trigger>(TriggerCol).Upsert(trigger);

    public void Delete(int id) =>
        _ctx.Db.GetCollection<Trigger>(TriggerCol).Delete(id);

    // ── Folders ───────────────────────────────────────────────────────────────

    public IReadOnlyList<TriggerFolder> GetAllFolders()
    {
        var col = _ctx.Db.GetCollection<TriggerFolder>(FolderCol);
        col.EnsureIndex(x => x.Id);
        return col.FindAll().OrderBy(f => f.Name).ToList();
    }

    public void SaveFolder(TriggerFolder folder) =>
        _ctx.Db.GetCollection<TriggerFolder>(FolderCol).Upsert(folder);

    public void DeleteFolder(int id) =>
        _ctx.Db.GetCollection<TriggerFolder>(FolderCol).Delete(id);
}
