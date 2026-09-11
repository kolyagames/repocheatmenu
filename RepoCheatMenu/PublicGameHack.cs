using ExitGames.Client.Photon;
using Photon.Pun;

namespace RepoCheatMenu;

// One-click shortcut to the exact same action the game's own "Public Game" -> "Random
// Matchmaking" main-menu button performs (SemiFunc.MenuActionRandomMatchmaking, confirmed in the
// decompiled source - resets progress, creates/loads a save, and joins a random public lobby via
// RunManager.ChangeLevel). Guarded to only fire before you've actually spawned into a level, same
// as the vanilla button is only ever reachable from the main menu - calling this mid-run would
// reset your current progress, which the vanilla flow never lets happen by accident.
internal static class PublicGameHack
{
    public static void JoinRandomPublic()
    {
        // Guarded on SemiFunc.MenuLevel() (the game's own "current level is main menu / lobby menu
        // / splash" check), NOT on PlayerAvatar.instance == null like this used to be. Verified
        // live at the main menu: levelCurrent is "Level - Main Menu" and MenuLevel() is true, yet
        // PlayerAvatar.instance is a real "Player Avatar Controller" object - the menu keeps an
        // avatar around for the character preview. So the old guard was true in the one place the
        // button is meant to work from, and this never once ran.
        if (!SemiFunc.MenuLevel())
        {
            Plugin.Log.LogInfo("Публичная игра: нужно сначала выйти в главное меню - вызов посреди забега сбросил бы текущий прогресс.");
            return;
        }

        EventLog.Write("session", "JoinRandomPublic: calling SemiFunc.MenuActionRandomMatchmaking().");
        SemiFunc.MenuActionRandomMatchmaking();
    }

    // The server list only ever queries whichever Photon region is fixed
    // (PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion, set from DataDirector.
    // networkRegion) - if that's pinned to a region with no active public rooms, the list stays
    // empty even while other regions have real games. This is the exact same two-line reset the
    // vanilla "Pick Best Region" menu option runs (MenuPageRegions.cs, confirmed in the
    // decompiled source) - it doesn't reconnect anything itself, it just clears the pin so the
    // NEXT connection (opening the server list, or Public Game) auto-picks the best-performing
    // region instead of whatever was stuck from before.
    public static void ResetToBestRegion()
    {
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "";
        ServerSettings.ResetBestRegionCodeInPreferences();
        Plugin.Log.LogInfo("Регион сброшен на авто-выбор. Теперь открой список серверов заново (или нажми Публичную игру) - подключение пойдёт по лучшему региону, не по старому закреплённому.");
    }

    // Photon defaults to UDP with EnableProtocolFallback=true (confirmed in the decompiled
    // AppSettings source) - it tries UDP first and only falls back to another protocol after that
    // attempt already timed out, which is exactly what a slow/flaky connection looks like on a
    // network that quietly drops UDP (some routers/ISPs do). Forcing WebSocketSecure up front
    // skips the failed-UDP-attempt-then-fallback delay entirely - it's plain TCP-based traffic
    // over port 443, the same kind of connection a browser makes, so it's far less likely to be
    // silently filtered than raw UDP. Doesn't reconnect anything itself, same as ResetToBestRegion
    // - takes effect on the next connection attempt.
    public static void ForceWebSocketProtocol()
    {
        PhotonNetwork.PhotonServerSettings.AppSettings.Protocol = ConnectionProtocol.WebSocketSecure;
        Plugin.Log.LogInfo("Протокол Photon форсирован на WebSocketSecure (TCP через 443 порт, как у браузера) - " +
            "открой список серверов заново. Если сеть режет UDP, это должно подключаться быстрее/надёжнее.");
    }
}
