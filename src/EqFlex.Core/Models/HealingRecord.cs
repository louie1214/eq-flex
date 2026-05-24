namespace EqFlex.Core.Models;

public sealed record HealingRecord(
    string Healer,
    string Target,
    string Spell,
    long Amount,
    long OverHeal,
    bool IsHot,
    bool IsCritical,
    long Timestamp);
