namespace EqFlex.Core.Models;

public sealed class CharacterProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public bool LogArchiveEnabled { get; set; }
    public int LogArchiveSizeMb { get; set; } = 500;
    public bool ParseCombat { get; set; } = true;
    public bool ParseTrade { get; set; } = false;
    public DateTime LastUsed { get; set; }
}
