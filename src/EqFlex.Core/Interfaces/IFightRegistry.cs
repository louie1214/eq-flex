using EqFlex.Core.Models;

namespace EqFlex.Core.Interfaces;

public interface IFightRegistry
{
    Fight GetOrCreate(string npcName, long timestamp);
    IReadOnlyList<Fight> GetActiveFights();
    void CheckExpiry(long nowSeconds);
    event EventHandler<Fight> FightUpdated;
    event EventHandler<Fight> FightExpired;
}
