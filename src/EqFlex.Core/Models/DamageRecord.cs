namespace EqFlex.Core.Models;

public enum DamageType { Melee, Spell, Dot, Proc, DamageShield, Bane, Other }
public enum DamagePerspective { Outgoing, Incoming, Observed }

public sealed record DamageRecord(
    string Attacker,
    string? AttackerOwner,   // pet/merc owner name; null for direct players
    string Defender,
    string Ability,
    long Total,
    long Blocked,
    DamageType Type,
    DamagePerspective Perspective,
    bool IsCritical,
    bool IsLucky,
    bool IsRampage,
    bool IsRiposte,
    bool AttackerIsSpell,    // true when attacker field holds a spell name, not a person
    long Timestamp);
