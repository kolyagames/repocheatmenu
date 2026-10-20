// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Live status line for the Invisible toggle - mirrors GhostObserverHack.Status() so the player can see
// at a glance what is actually being broadcast. The two tricky states to spot otherwise are:
//
//   1. the "talk reveal" window - with InvisibleRevealWhileTalking on, the real position is sent for
//      a short grace after you talk, which breaks the invisibility and is easy to miss;
//   2. GhostSpeak path winning over InvisibleSpeak - the anonymous voice patches share one gate, and if
//      you forgot to turn Invisible off before entering a ghost run, voice would be anonymous but for a
//      different reason than you think.
//
// If InvisibleBlockGrab is also on, states that line too.
internal static class InvisibleStatus
{
    public static string Line()
    {
        bool invis = CheatState.Invisible.Value;
        bool flyHide = CheatState.FlyAutoHide.Value && (CheatState.Fly.Value || CheatState.Noclip.Value);
        if (!invis && !flyHide) return "  -> Выключено";

        string why = invis && flyHide ? "Invisible + Fly/Noclip" : invis ? "Invisible" : "Fly/Noclip (авто-прятание)";

        string pos;
        if (InvisiblePatch.BroadcastIsHidden)
        {
            float h = CheatState.InvisibleOverCeiling.Value
                ? CheatState.InvisibleOverCeilingHeight.Value : CheatState.InvisibleHeight.Value;
            pos = CheatState.InvisibleOverCeiling.Value
                ? "позиция: +" + h.ToString("0") + " м над потолком"
                : "позиция: Y=" + h.ToString("0");
        }
        else
        {
            pos = CheatState.InvisibleRevealWhileTalking.Value && !CheatState.InvisibleSpeak.Value
                ? "ВИДЕН: сейчас транслируется реальная позиция (reveal while talking)"
                : "позиция: реальная (ещё не было тика сети после включения)";
        }

        string voice;
        if (GhostObserverHack.SessionGhost && CheatState.GhostSpeak.Value)
        {
            voice = "голос: аноним через ghost";
        }
        else if (CheatState.InvisibleSpeak.Value)
        {
            string live = GhostObserverHack.AnonymousVoiceLiveStatus ? ", сейчас передаёте" : "";
            voice = GhostObserverHack.AnonymousRecorders == 0
                ? "голос: аноним (recorder ещё не создан, подождите тик)"
                : (GhostObserverHack.AnonymousRecordersReady == GhostObserverHack.AnonymousRecorders
                    ? "голос: аноним (слышат все, без 3D)" + live
                    : "голос: аноним (" + GhostObserverHack.AnonymousRecordersReady + "/" + GhostObserverHack.AnonymousRecorders + " готовы)" + live);
        }
        else if (CheatState.InvisibleRevealWhileTalking.Value)
        {
            voice = "голос: виден при разговоре (reveal)";
        }
        else
        {
            voice = "голос: уходит из позиции в небе (чужим не слышно)";
        }

        string grab = CheatState.InvisibleBlockGrab.Value ? "хват заблокирован" : "хват работает (виден предметам)";

        string room = PhotonNetwork.InRoom ? "" : ". Вы не в комнате - никто вас всё равно не видит";
        return "  -> АКТИВНА (" + why + "). " + pos + ". " + voice + ". " + grab + room;
    }
}
