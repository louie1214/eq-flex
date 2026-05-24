namespace EqFlex.Core.Interfaces;

public interface IPlayerRegistry
{
    bool IsPlayer(string name);
    bool IsPet(string name);
    string? GetPetOwner(string petName);
    void RegisterPlayer(string name);
    void RegisterPet(string petName, string ownerName);
}
