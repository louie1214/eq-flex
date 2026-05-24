namespace EqFlex.Core.Models;

public sealed record SpellData(
    string Id,
    string Name,
    string NameAbbrv,
    byte Level,
    ushort Duration,       // seconds (ticks * 6)
    bool IsBeneficial,
    byte Target,
    ushort ClassMask,
    short Damaging,        // 2 = Bane damage
    int Resist,
    bool SongWindow,
    byte Adps,
    bool Mgb,
    byte Rank,
    bool HasAmbiguity,
    string LandsOnYou,
    string LandsOnOther,
    string WearOff,
    byte Proc);            // 1 = proc spell (from procs.txt)
