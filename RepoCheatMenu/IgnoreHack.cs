// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// "Игнор": входящие RPC от ДРУГИХ игроков, направленные на мой аватар, можно выборочно отбросить.
// Ничего не отправляется - просто метод RPC на этой машине не выполняется. Свои же вызовы
// (Sender == я, либо вызов без сети) никогда не режутся, поэтому честная смерть/урон от
// собственной игры по-прежнему работают. Остальные клиенты видят то, что видели.
internal static class IgnoreHack
{
    public static long DeathDropped;
    public static long HurtDropped;
    public static long ImpulseDropped;
    public static long TumbleDropped;
    public static long HealthDropped;

    // true = RPC надо отбросить: он пришёл по сети от другого игрока и адресован моему объекту.
    public static bool ShouldDrop(Component target, PhotonMessageInfo info)
    {
        if (target == null) return false;
        if (info.Sender == null || info.Sender.IsLocal) return false;
        PhotonView view = GameReflect.Get<PhotonView>(target, "photonView");
        return view != null && view.IsMine;
    }

    public static void ResetCounters()
    {
        DeathDropped = HurtDropped = ImpulseDropped = TumbleDropped = HealthDropped = 0;
    }
}

[HarmonyPatch(typeof(PlayerAvatar), "PlayerDeathRPC")]
internal static class IgnoreDeathPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PlayerAvatar __instance, PhotonMessageInfo _info)
    {
        try
        {
            if (!CheatState.IgnoreDeath.Value || !IgnoreHack.ShouldDrop(__instance, _info)) return true;
            IgnoreHack.DeathDropped++;
            return false;
        }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(PlayerHealth), "HurtOtherRPC")]
internal static class IgnoreHurtPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PlayerHealth __instance, PhotonMessageInfo _info)
    {
        try
        {
            if (!CheatState.IgnoreHurt.Value || !IgnoreHack.ShouldDrop(__instance, _info)) return true;
            IgnoreHack.HurtDropped++;
            return false;
        }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(PlayerHealth), "UpdateHealthRPC")]
internal static class IgnoreHealthSetPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PlayerHealth __instance, PhotonMessageInfo _info)
    {
        try
        {
            if (!CheatState.IgnoreHealthSet.Value || !IgnoreHack.ShouldDrop(__instance, _info)) return true;
            IgnoreHack.HealthDropped++;
            return false;
        }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(PlayerAvatar), "ForceImpulseRPC")]
internal static class IgnoreImpulsePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PlayerAvatar __instance, PhotonMessageInfo _info)
    {
        try
        {
            if (!CheatState.IgnoreImpulse.Value || !IgnoreHack.ShouldDrop(__instance, _info)) return true;
            IgnoreHack.ImpulseDropped++;
            return false;
        }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(PlayerTumble), "TumbleSetRPC")]
internal static class IgnoreTumbleSetPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PlayerTumble __instance, PhotonMessageInfo _info)
    {
        try
        {
            if (!CheatState.IgnoreTumble.Value || !IgnoreHack.ShouldDrop(__instance, _info)) return true;
            IgnoreHack.TumbleDropped++;
            return false;
        }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(PlayerTumble), "TumbleForceRPC")]
internal static class IgnoreTumbleForcePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PlayerTumble __instance, PhotonMessageInfo _info)
    {
        try
        {
            if (!CheatState.IgnoreTumble.Value || !IgnoreHack.ShouldDrop(__instance, _info)) return true;
            IgnoreHack.TumbleDropped++;
            return false;
        }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(PlayerTumble), "TumbleTorqueRPC")]
internal static class IgnoreTumbleTorquePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PlayerTumble __instance, PhotonMessageInfo _info)
    {
        try
        {
            if (!CheatState.IgnoreTumble.Value || !IgnoreHack.ShouldDrop(__instance, _info)) return true;
            IgnoreHack.TumbleDropped++;
            return false;
        }
        catch { return true; }
    }
}
