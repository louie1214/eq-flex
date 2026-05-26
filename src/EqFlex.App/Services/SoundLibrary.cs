using System.IO;

namespace EqFlex.App.Services;

public sealed class SoundFile
{
    public string Name     { get; init; } = string.Empty; // display name (no extension)
    public string FileName { get; init; } = string.Empty; // filename stored in DB
    public string FullPath { get; init; } = string.Empty; // absolute path for playback
    public override string ToString() => Name;
}

/// <summary>
/// Scans the bundled sounds/ directory and %APPDATA%\EqFlex\sounds for available audio files.
/// AudioPath values in the DB are stored as plain filenames; Resolve() converts them to full paths.
/// </summary>
public sealed class SoundLibrary
{
    private static readonly string[] _extensions = [".wav", ".mp3", ".ogg", ".flac"];

    public static readonly string UserSoundsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EqFlex", "sounds");

    private readonly string _bundledDir;
    private List<SoundFile> _sounds = [];

    public SoundLibrary()
    {
        _bundledDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds");
        Directory.CreateDirectory(UserSoundsDir);
        Refresh();
    }

    public IReadOnlyList<SoundFile> Sounds => _sounds;

    public void Refresh()
    {
        var files = new List<SoundFile>();
        AddFromDir(files, _bundledDir);
        AddFromDir(files, UserSoundsDir);
        _sounds = [.. files.OrderBy(f => f.Name)];
    }

    /// <summary>
    /// Resolves a stored audio value (filename or legacy absolute path) to a full path for playback.
    /// Returns null if nothing matches.
    /// </summary>
    public string? Resolve(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
        if (Path.IsPathRooted(nameOrPath) && File.Exists(nameOrPath)) return nameOrPath;
        return _sounds.FirstOrDefault(s =>
            s.FileName.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase))?.FullPath;
    }

    /// <summary>Copies a file into the user sounds directory and refreshes the list.</summary>
    public SoundFile? Import(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var dest = Path.Combine(UserSoundsDir, fileName);
        File.Copy(sourcePath, dest, overwrite: true);
        Refresh();
        return _sounds.FirstOrDefault(s => s.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddFromDir(List<SoundFile> list, string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir)
                     .Where(f => _extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                     .OrderBy(f => f))
        {
            list.Add(new SoundFile
            {
                Name     = Path.GetFileNameWithoutExtension(f),
                FileName = Path.GetFileName(f),
                FullPath = f
            });
        }
    }
}
