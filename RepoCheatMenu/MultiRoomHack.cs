// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Threading.Tasks;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace RepoCheatMenu;

// "Sibling Rooms" - turns the room you're already hosting into MultiRoomExtraCount + 1 real,
// separately-joinable public rooms that all show the same name in the in-game server list.
//
// Rewritten 2026-10-04 after a read of the game's own room flow (NetworkConnect /
// MenuPageServerList / NetworkManager / SteamManager in the decompile) found why rooms kept
// "disappearing". The old version hopped out of the hosted room and tried to JoinRoom back into it:
//   1. Leaving as master kills the home room. Every remaining player gets
//      NetworkManager.OnMasterClientSwitched -> TriggerLeavePhotonRoomForced ("Host disconnected"),
//      and a room that ends up empty is gone (EmptyRoomTtl defaults to 0), so the final JoinRoom
//      never had anything to return to. Now we refuse unless you are alone in the room, and the
//      LAST room we create is the one we stay in - there is no "return home" step any more.
//   2. Siblings were created in TypedLobby.Default with no custom properties. The server list
//      (MenuPageServerList) does JoinLobby(DataDirector.customLobby) and prints the Photon room
//      property "server_name", so those rooms were invisible. Now they use customLobby,
//      CustomRoomProperties { server_name } and CustomRoomPropertiesForLobby { server_name },
//      exactly like NetworkConnect.OnConnectedToMaster does for a named public room.
//   3. CreateRoom was called the instant InRoom turned false, before the client was back on the
//      master server, so it returned false and the old code then waited forever for InRoom.
//      Every wait now has a timeout and the run aborts with a log line instead of hanging.
//   4. MaxPlayers was passed straight through; anything above GameManager.maxPlayersPhoton makes
//      CreateRoom fail ("max players peer room value is too big"). Now clamped to it.
//   5. A new Steam lobby was created per sibling and never unlocked (HostLobby leaves it
//      SetJoinable(false)), and SteamManager.OnLobbyEntered leaves the previous one each time,
//      destroying it. The game only uses Steam lobbies for invites - the public list is Photon -
//      so siblings no longer touch Steam, and the final room gets one proper lobby, the same
//      HostLobby -> SetLobbyData -> UnlockLobby sequence RunManagerPUN.HostSteamLobby uses.
//
// Hard limits that stay: a client can only be in one Photon room at a time, and an empty sibling
// is kept alive by EmptyRoomTtl for at most 5 minutes (Photon Cloud's cap), so run this shortly
// before you want people to join. Not verified against a live Photon session.
internal static class MultiRoomHack
{
    private const int StepTimeoutMs = 15000;
    private const int EmptyRoomTtlMs = 5 * 60 * 1000; // Photon Cloud maximum

    public static bool CanRun => GameManager.Multiplayer() && PhotonNetwork.InRoom;

    private static bool _running;

    // Read by Plugin.HotReloadBlocker: do not swap the mod out in the middle of a room hop.
    internal static bool IsRunning => _running;

    public static async void CreateSiblingRooms()
    {
        if (_running)
        {
            Plugin.Log.LogInfo("Sibling Rooms: already running, wait for it to finish.");
            return;
        }
        if (!CanRun)
        {
            Plugin.Log.LogInfo("Sibling Rooms: only works while you're already hosting a public room.");
            return;
        }
        if (!PhotonNetwork.IsMasterClient)
        {
            Plugin.Log.LogInfo("Sibling Rooms: only the host of the room can do this.");
            return;
        }
        if (PhotonNetwork.CurrentRoom.PlayerCount > 1)
        {
            // Leaving as master makes the game kick everyone else to the main menu.
            Plugin.Log.LogInfo("Sibling Rooms: refused - other players are in your room and " +
                "leaving as host would send all of them to the main menu. Run it while alone.");
            return;
        }
        if (!PhotonNetwork.CurrentRoom.IsVisible)
        {
            Plugin.Log.LogInfo("Sibling Rooms: refused - your room is private, the siblings " +
                "would be public. Start a public room first.");
            return;
        }

        _running = true;
        try
        {
            await RunSequence();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Sibling Rooms: aborted - " + e.Message);
        }
        finally
        {
            _running = false;
        }
    }

    private static async Task RunSequence()
    {
        string displayName = GetCurrentDisplayName();
        int total = CheatState.MultiRoomExtraCount.Value + 1; // the extras plus the one we stay in
        int maxPlayers = CheatState.MultiRoomMaxPlayers.Value;
        int photonCap = GameManager.instance.maxPlayersPhoton;
        if (photonCap > 0 && maxPlayers > photonCap)
        {
            Plugin.Log.LogInfo($"Sibling Rooms: MaxPlayers {maxPlayers} is above the server cap " +
                $"({photonCap}), using {photonCap}.");
            maxPlayers = photonCap;
        }

        Plugin.Log.LogInfo($"Sibling Rooms: creating {total} public room(s) named \"{displayName}\"...");

        for (int i = 0; i < total; i++)
        {
            PhotonNetwork.LeaveRoom();
            await WaitFor(() => !PhotonNetwork.InRoom && BackOnMasterServer(), "leaving the room");

            var options = new RoomOptions
            {
                MaxPlayers = (byte)maxPlayers,
                IsVisible = true,
                IsOpen = true,
                // Keeps each room listed for a while with nobody in it. The room we end up in
                // doesn't need it but it is harmless there.
                EmptyRoomTtl = EmptyRoomTtlMs,
                CustomRoomPropertiesForLobby = new[] { "server_name" },
                CustomRoomProperties = new Hashtable { { "server_name", displayName } }
            };

            // null name - Photon assigns a unique technical id. The visible name is server_name.
            if (!PhotonNetwork.CreateRoom(null, options, DataDirector.instance.customLobby))
                throw new Exception("CreateRoom was refused by the client (not ready for matchmaking)");
            await WaitFor(() => PhotonNetwork.InRoom, "creating room " + (i + 1));

            Plugin.Log.LogInfo($"Sibling Rooms: room {i + 1}/{total} ready ({PhotonNetwork.CurrentRoom.Name}).");
        }

        // We are now master of the last room. Rebuild what the game normally sets up around a
        // freshly created public room: the scene, and a Steam lobby for invites.
        GameManager.instance.SetConnectRandom(true);
        GameManager.instance.SetLobbyType(GameManager.LobbyTypes.Public);
        RunManager.instance.ChangeLevel(_completedLevel: true, _levelFailed: false,
            RunManager.ChangeLevelType.LobbyMenu);
        await HostSteamLobby(displayName);

        Plugin.Log.LogInfo("Sibling Rooms: done - you are hosting the last room; the others are " +
            "listed for up to 5 minutes while empty.");
    }

    // Same order RunManagerPUN.HostSteamLobby uses: HostLobby, wait for the id, SetLobbyData,
    // then unlock. LeaveLobby first because the previous lobby belonged to the room we left.
    private static async Task HostSteamLobby(string displayName)
    {
        if (!Steamworks.SteamClient.IsValid) return;
        SteamManager steam = SteamManager.instance;
        steam.LeaveLobby();
        steam.HostLobby(true);
        await WaitFor(() => GameReflect.Get<Steamworks.Data.Lobby>(steam, "currentLobby").Id.IsValid,
            "creating the Steam lobby");
        steam.SetLobbyData(displayName);
        steam.UnlockLobby(true);
    }

    private static bool BackOnMasterServer()
    {
        ClientState s = PhotonNetwork.NetworkClientState;
        return s == ClientState.ConnectedToMasterServer || s == ClientState.JoinedLobby;
    }

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        int waited = 0;
        while (!condition())
        {
            if (waited >= StepTimeoutMs) throw new Exception("timed out " + what);
            await Task.Delay(100);
            waited += 100;
        }
    }

    // server_name is the Photon room property the server list prints (MenuPageServerList);
    // fall back to the Steam lobby's RoomName, then the technical room name.
    private static string GetCurrentDisplayName()
    {
        object fromProps = PhotonNetwork.CurrentRoom.CustomProperties["server_name"];
        if (fromProps is string s && !string.IsNullOrEmpty(s)) return s;

        Steamworks.Data.Lobby lobby = GameReflect.Get<Steamworks.Data.Lobby>(SteamManager.instance, "currentLobby");
        string fromLobby = lobby.GetData("RoomName");
        return !string.IsNullOrEmpty(fromLobby) ? fromLobby : PhotonNetwork.CurrentRoom.Name;
    }
}
