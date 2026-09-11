using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Everything a script module sees by default (Roslyn scripting exposes a "globals" instance's
// PUBLIC members as bare top-level identifiers in script code - "Local" instead of
// "Globals.Local"). Public on purpose: scripts compile into their OWN dynamic assembly (a
// separate one from RepoCheatMenu.dll), so they can only see this assembly's PUBLIC surface,
// same C#-level rule as any other cross-assembly call - internal classes like CheatState/
// GameReflect/KnockdownHack are invisible to a script even though this class (living inside
// RepoCheatMenu.dll itself) can freely call them. Kept deliberately small and named for what a
// script author would actually reach for, rather than exposing every internal type wholesale.
//
// One thing this can't paper over: fields that are genuinely private in the real, running
// Assembly-CSharp.dll still throw FieldAccessException if touched with plain dot-syntax, from a
// script exactly the same as from any other mod code (confirmed in GameReflect.cs's own
// comment - the "publicized" compile-time stub the game-libs NuGet package provides doesn't
// change what the CLR enforces at runtime). Use GetField/SetField/Call below for those, the same
// way the rest of this mod already has to.
public class ScriptGlobals
{
    public PlayerAvatar Local => PlayerAvatar.instance;
    public PlayerController Controller => PlayerController.instance;
    public GameDirector Director => GameDirector.instance;

    // "Control the game" handles for scripts that want more than a toggle - Steam lobby
    // creation/joining, the run/level state machine, and (once ScriptEngine's reference list
    // includes the Photon assemblies) Photon.Pun.PhotonNetwork/Photon.Realtime types are all
    // directly usable in script code now, same as any normal C# would use them.
    public SteamManager Steam => SteamManager.instance;
    public GameManager Game => GameManager.instance;
    public RunManager Run => RunManager.instance;

    // Renames the lobby you're ALREADY hosting - both the Steam lobby's displayed "RoomName"
    // (confirmed in the decompiled SteamManager.SetLobbyData - that's the exact field the game's
    // own lobby browser/invite UI reads) and the underlying Photon room, so the two stay
    // consistent. Deliberately does NOT drive the multi-step "host a brand-new lobby from the
    // main menu" sequence (RunManagerPUN.HostSteamLobby/NetworkConnect) - that depends on which
    // screen/state you're already in, which a one-line wrapper can't safely guess. Call
    // Steam.HostLobby(true) yourself first if you need a lobby to exist at all - see the seeded
    // "СоздатьЛобби" example module for the exact working sequence.
    public bool RenameRoom(string name)
    {
        SteamManager steam = Steam;
        if (steam == null) return false;

        steam.SetLobbyData(name);

        // Room.Name's setter is "internal set" (confirmed in the decompiled PhotonRealtime
        // source) - only Photon's own assembly can call it directly, same restriction as any
        // internal/private game member. GameReflect.Set goes straight at the backing field
        // ("name", declared on the base RoomInfo class - GameReflect.cs now walks base types to
        // find it) instead, same bypass this mod already relies on everywhere else.
        Photon.Realtime.Room room = Photon.Pun.PhotonNetwork.CurrentRoom;
        if (room != null) GameReflect.Set(room, "name", name);
        return true;
    }

    public void Log(object message) => Plugin.Log.LogInfo(message?.ToString() ?? "null");

    public List<PlayerAvatar> Players()
    {
        GameDirector director = Director;
        List<PlayerAvatar> list = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        return list ?? new List<PlayerAvatar>();
    }

    public List<PlayerAvatar> OtherPlayers()
    {
        PlayerAvatar local = Local;
        List<PlayerAvatar> result = new();
        foreach (PlayerAvatar p in Players())
            if (p != null && p != local) result.Add(p);
        return result;
    }

    // Thin pass-throughs to GameReflect (internal, so invisible to a script's own assembly) -
    // for touching fields/methods that aren't genuinely public on the real game types.
    public T GetField<T>(object instance, string name) => GameReflect.Get<T>(instance, name);
    public void SetField(object instance, string name, object value) => GameReflect.Set(instance, name, value);
    public void Call(object instance, string method, params object[] args) => GameReflect.Invoke(instance, method, args);

    // Existing mod toggles/hacks, by name - lets a script flip the exact same switches the GUI
    // does instead of re-implementing them. Unknown names are silently ignored (returns false)
    // rather than throwing, since a typo in a script shouldn't take down the whole run.
    public bool SetCheat(string name, bool value) => CheatState.TrySetBool(name, value);
    public bool GetCheat(string name) => CheatState.TryGetBool(name);
}
