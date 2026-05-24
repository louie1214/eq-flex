using EqFlex.Core.Models;

namespace EqFlex.Core.Interfaces;

public interface ISpellDataService
{
    SpellData? GetByName(string name);
    SpellData? GetByAbbrv(string abbrv);
    IReadOnlyList<SpellData> GetAllByName(string name);
    bool IsNpc(string name);
    bool IsPetName(string name);
    string? GetClassForSpell(string spellName);
    /// <summary>True if the spell is known to produce charm/control effects.</summary>
    bool IsCharmSpell(string spellName);
    /// <summary>
    /// Checks whether <paramref name="action"/> ends with a charm-spell "lands on other"
    /// string. If so, returns the entity name (the prefix before the effect text).
    /// </summary>
    bool TryMatchCharmLanding(string action, out string entityName);
    /// <summary>
    /// Checks whether <paramref name="action"/> matches a charm wear-off message
    /// (e.g. "a skeleton is no longer charmed."). Returns the entity whose charm broke.
    /// </summary>
    bool TryMatchCharmBreak(string action, out string entityName);
    void Load(string dataDirectory);
}
