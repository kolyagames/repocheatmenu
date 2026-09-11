using System.Collections.Generic;

namespace RepoCheatMenu;

// Per-item-type indestructibility, keyed off the game's own SemiFunc.itemType (confirmed in the
// decompiled source - the 16-value enum every real Item asset already carries via ItemAttributes.
// item.itemType). Separate from CheatState.Unbreakable, which only ever covers actual valuables
// (PhysGrabObjectImpactDetector's own ___isValuable flag, unrelated to this enum) - this covers
// everything else: weapons, tools, vehicles, upgrade stations, etc., each independently toggleable
// instead of one blanket switch.
internal static class UnbreakableItemsHack
{
    private static readonly Dictionary<SemiFunc.itemType, System.Func<bool>> Lookup = new()
    {
        [SemiFunc.itemType.drone] = () => CheatState.UnbreakableDrone.Value,
        [SemiFunc.itemType.orb] = () => CheatState.UnbreakableOrb.Value,
        [SemiFunc.itemType.cart] = () => CheatState.UnbreakableCart.Value,
        [SemiFunc.itemType.item_upgrade] = () => CheatState.UnbreakableItemUpgrade.Value,
        [SemiFunc.itemType.player_upgrade] = () => CheatState.UnbreakablePlayerUpgrade.Value,
        [SemiFunc.itemType.power_crystal] = () => CheatState.UnbreakablePowerCrystal.Value,
        [SemiFunc.itemType.grenade] = () => CheatState.UnbreakableGrenade.Value,
        [SemiFunc.itemType.melee] = () => CheatState.UnbreakableMelee.Value,
        [SemiFunc.itemType.healthPack] = () => CheatState.UnbreakableHealthPack.Value,
        [SemiFunc.itemType.gun] = () => CheatState.UnbreakableGun.Value,
        [SemiFunc.itemType.tracker] = () => CheatState.UnbreakableTracker.Value,
        [SemiFunc.itemType.mine] = () => CheatState.UnbreakableMine.Value,
        [SemiFunc.itemType.pocket_cart] = () => CheatState.UnbreakablePocketCart.Value,
        [SemiFunc.itemType.tool] = () => CheatState.UnbreakableTool.Value,
        [SemiFunc.itemType.vehicle] = () => CheatState.UnbreakableVehicle.Value,
        [SemiFunc.itemType.launcher] = () => CheatState.UnbreakableLauncher.Value,
    };

    public static bool IsProtected(PhysGrabObjectImpactDetector detector)
    {
        if (detector == null) return false;

        ItemAttributes attributes = detector.GetComponent<ItemAttributes>();
        if (attributes == null || attributes.item == null) return false;

        return Lookup.TryGetValue(attributes.item.itemType, out var isOn) && isOn();
    }
}
