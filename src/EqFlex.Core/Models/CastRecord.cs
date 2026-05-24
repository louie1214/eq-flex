namespace EqFlex.Core.Models;

public sealed record CastRecord(
    string Caster,
    string Spell,
    bool IsBeginCast,
    bool IsInterrupted,
    long Timestamp);
