using EqFlex.Core.Models;
using LiteDB;

namespace EqFlex.Infrastructure.Storage;

public sealed class ProfileStore
{
    private const string Col = "profiles";
    private readonly LiteDbContext _ctx;

    public ProfileStore(LiteDbContext ctx)
    {
        _ctx = ctx;
        var col = _ctx.Db.GetCollection<CharacterProfile>(Col);
        col.EnsureIndex(x => x.Name);
    }

    public IReadOnlyList<CharacterProfile> GetAll() =>
        _ctx.Db.GetCollection<CharacterProfile>(Col).FindAll().ToList();

    public CharacterProfile? GetById(int id) =>
        _ctx.Db.GetCollection<CharacterProfile>(Col).FindById(id);

    public CharacterProfile? GetLastUsed() =>
        _ctx.Db.GetCollection<CharacterProfile>(Col)
            .Find(Query.All())
            .OrderByDescending(p => p.LastUsed)
            .FirstOrDefault();

    public int Upsert(CharacterProfile profile)
    {
        var col = _ctx.Db.GetCollection<CharacterProfile>(Col);
        if (profile.Id == 0)
        {
            col.Insert(profile);
        }
        else
        {
            col.Update(profile);
        }
        return profile.Id;
    }

    public void Delete(int id) =>
        _ctx.Db.GetCollection<CharacterProfile>(Col).Delete(id);
}
