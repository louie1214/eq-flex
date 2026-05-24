namespace EqFlex.Core.Models;

public sealed class CharacterProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public bool AutoRenameLog { get; set; } = true;
    public bool ParseDamage { get; set; } = true;
    public bool ParseHealing { get; set; } = true;
    public bool ParseCasting { get; set; } = true;
    public bool ParseChat { get; set; } = false;
    public bool ParseTrade { get; set; } = false;
    public DateTime LastUsed { get; set; }
}
