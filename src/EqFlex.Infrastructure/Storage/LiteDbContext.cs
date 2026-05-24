using LiteDB;

namespace EqFlex.Infrastructure.Storage;

public sealed class LiteDbContext : IDisposable
{
    public LiteDatabase Db { get; }

    public LiteDbContext(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        Db = new LiteDatabase(new ConnectionString
        {
            Filename = dbPath,
            Connection = ConnectionType.Shared
        })
        {
            CheckpointSize = 10
        };
    }

    public void Dispose() => Db.Dispose();
}
