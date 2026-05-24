using EqFlex.Core.Models;

namespace EqFlex.Infrastructure.Storage;

public sealed class OverlayWindowStore
{
    private const string Col = "overlayWindows";
    private readonly LiteDbContext _ctx;

    public OverlayWindowStore(LiteDbContext ctx) => _ctx = ctx;

    public IReadOnlyList<OverlayWindow> GetAll()
        => _ctx.Db.GetCollection<OverlayWindow>(Col).FindAll().OrderBy(w => w.Id).ToList();

    public void Save(OverlayWindow window)
        => _ctx.Db.GetCollection<OverlayWindow>(Col).Upsert(window);

    public void Delete(int id)
        => _ctx.Db.GetCollection<OverlayWindow>(Col).Delete(id);
}
