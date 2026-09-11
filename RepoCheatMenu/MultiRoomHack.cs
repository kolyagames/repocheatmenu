using System.Threading.Tasks;
using Photon.Pun;
using Photon.Realtime;

namespace RepoCheatMenu;

// "Sibling Rooms" - takes the room you're ALREADY hosting and spins up MultiRoomExtraCount more
// genuinely independent Photon rooms (real separate populations, each up to
// MultiRoomMaxPlayers - not the same room re-listed under extra server-browser entries), all
// sharing your current room's own displayed Steam-lobby name. Deliberately does NOT implement
// "several server-list entries that all resolve to the same room" - every entry this creates is
// a real, separately-joinable room with its own real capacity, by design.
//
// Requires already being in a room (CanRun) for two reasons: that's where the shared display
// name comes from (SteamManager's own "RoomName" lobby data - see GetCurrentDisplayName), and
// it's the room this leaves you back in once done.
//
// Hard constraint from Photon itself, not a mod limitation: a single client can only be IN one
// room at a time (confirmed in the decompiled PhotonNetwork - CreateRoom/JoinRoom both refuse to
// run while already in a room, they require being back on the Master server first). So creating
// each sibling means briefly leaving your own room, then coming back at the end. If you're the
// only one in your own room when this runs, it can expire while you're away - your room's own
// EmptyRoomTtl (whatever it already was, not something this controls) decides that, not this
// hack - logged as a warning rather than blocked outright, since running it anyway is a
// legitimate call for the room's owner to make.
internal static class MultiRoomHack
{
    public static bool CanRun => GameManager.Multiplayer() && PhotonNetwork.InRoom;

    private static bool _running;

    public static async void CreateSiblingRooms()
    {
        if (_running)
        {
            Plugin.Log.LogInfo("Sibling Rooms: already running, wait for it to finish.");
            return;
        }
        if (!CanRun)
        {
            Plugin.Log.LogInfo("Sibling Rooms: only works while you're already hosting/in a room.");
            return;
        }

        _running = true;
        try
        {
            await RunSequence();
        }
        finally
        {
            _running = false;
        }
    }

    private static async Task RunSequence()
    {
        string homeRoomName = PhotonNetwork.CurrentRoom.Name;
        string displayName = GetCurrentDisplayName();
        int extraCount = CheatState.MultiRoomExtraCount.Value;
        int maxPlayers = CheatState.MultiRoomMaxPlayers.Value;

        if (PhotonNetwork.CurrentRoom.PlayerCount <= 1)
        {
            Plugin.Log.LogInfo("Sibling Rooms: you're alone in your own room right now - it may " +
                "expire while this hops away to create the others. Safer with at least one other " +
                "real player already in it first, but proceeding anyway.");
        }

        Plugin.Log.LogInfo($"Sibling Rooms: creating {extraCount} more room(s) named \"{displayName}\"...");

        for (int i = 0; i < extraCount; i++)
        {
            var options = new RoomOptions
            {
                MaxPlayers = maxPlayers,
                IsVisible = true,
                IsOpen = true,
                // Keeps each sibling alive for a while even with nobody in it, since we always
                // leave right after setting it up - without this it would die the instant we do.
                EmptyRoomTtl = 5 * 60 * 1000
            };

            PhotonNetwork.LeaveRoom();
            while (PhotonNetwork.InRoom) await Task.Delay(100);

            // null - Photon assigns a unique technical id itself. Giving every sibling the same
            // technical name would just make every call after the first one fail (CreateRoom
            // requires a unique name) - the shared name players actually see is the Steam lobby
            // data set below instead, which has no such uniqueness requirement.
            PhotonNetwork.CreateRoom(null, options, TypedLobby.Default);
            while (!PhotonNetwork.InRoom) await Task.Delay(100);

            SteamManager.instance.HostLobby(true);
            await Task.Delay(2000); // not a confirmed-ready poll - just a generous fixed pause

            SteamManager.instance.SetLobbyData(displayName);
            Plugin.Log.LogInfo($"Sibling Rooms: room {i + 1}/{extraCount} ready ({PhotonNetwork.CurrentRoom.Name}).");
        }

        PhotonNetwork.LeaveRoom();
        while (PhotonNetwork.InRoom) await Task.Delay(100);

        PhotonNetwork.JoinRoom(homeRoomName);
        while (!PhotonNetwork.InRoom) await Task.Delay(100);

        // Confirmed root cause of the empty-void/no-level symptom: creating/joining a Photon
        // room alone never triggers the actual SCENE load - that's a separate system
        // (RunManager), normally fired by the same menu button press. The real invite-join flow
        // (decompiled SteamManager.OnGameLobbyJoinRequested) calls exactly this after joining -
        // "ChangeLevel(true, false, LobbyMenu)" - mirrored here as a best-effort fix (that real
        // flow also does several state resets - Disconnect/SetGameMode/tutorial/result-screen
        // handling - BEFORE joining, for the "joining from an arbitrary prior menu state" case;
        // skipped here since we're returning to a room we already made, not a cold start, but
        // this is a guess not verified against a live run - if it still lands empty, that
        // preamble is likely still needed too).
        RunManager.instance.ChangeLevel(_completedLevel: true, _levelFailed: false, RunManager.ChangeLevelType.LobbyMenu);

        // Photon reassigns MasterClient to whoever's still in the room the instant the previous
        // master leaves - if you were alone, rejoining your own room after it expired lands you
        // in a brand-new room of the same name instead, not a real "return." Not something this
        // can detect from here, so it isn't silently assumed to have worked.
        SteamManager.instance.HostLobby(true);
        await Task.Delay(1000);
        SteamManager.instance.SetLobbyData(displayName);

        Plugin.Log.LogInfo("Sibling Rooms: done - back in your own room (check you're really " +
            "back with your original group, not a fresh empty one of the same name).");
    }

    // "RoomName" is the exact Steam lobby data key the game's own SetLobbyData/UI already use
    // (confirmed in the decompiled SteamManager) - reading it back here means the siblings match
    // whatever name your room is actually showing right now, not just its technical Photon id.
    private static string GetCurrentDisplayName()
    {
        Steamworks.Data.Lobby lobby = GameReflect.Get<Steamworks.Data.Lobby>(SteamManager.instance, "currentLobby");
        string fromLobby = lobby.GetData("RoomName");
        return !string.IsNullOrEmpty(fromLobby) ? fromLobby : PhotonNetwork.CurrentRoom.Name;
    }
}
