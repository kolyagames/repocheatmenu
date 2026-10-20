// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Text;

namespace RepoCheatMenu;

// Scaffold for custom networked actions that don't piggyback on an existing PunRPC method on
// some game prefab. PhotonNetwork.RaiseEvent ships a raw custom event straight through the
// room (here, to the master client), and we own both ends - sending here and EventReceived
// below - so the payload format is entirely ours to define. Right now it just round-trips an
// empty payload and logs what comes back, to prove the channel works; once there's a real
// effect to drive (e.g. a target position for movement), put the real data in the payload and
// act on it in OnEventReceived instead of just logging.
internal static class PacketTestHack
{
    private const byte TestEventCode = 1;

    private static bool _isEnabled; // Флаг работы тумблера
    private static int _lastSendTick;
    private static bool _subscribed;

    // Deliberately does nothing with Photon here - this runs from Plugin.Awake(), long before
    // the game creates/configures its own Photon client (DataDirector.PhotonSetAppId/SetRegion
    // and NetworkConnect only run later, on a menu/level scene's Start()). Touching
    // PhotonNetwork.NetworkingClient this early force-creates a default, unconfigured
    // LoadBalancingClient - confirmed to make the game's own connect sequence hang forever
    // waiting on OnConnectedToMaster (Steam auth ticket sent, AppId set, but the callback never
    // fires) once the game's later setup finds a client "already there" instead of building its
    // own. Subscribe lazily instead, once we're already confirmed in a room - see EnsureSubscribed.
    //
    // Also syncs _isEnabled from the persisted ConfigEntry - found live-auditing this file: BepInEx
    // writes CheatState.SendTestPacket to disk, so a game that starts with the toggle already ON
    // reloads it as true, but _isEnabled (a plain static bool, not the ConfigEntry itself) defaulted
    // to false and was only ever set from OnToggled's CHANGE callback - which never fires on load,
    // only on a later click. Net effect before this fix: the checkbox shows ON, nothing is actually
    // sent, until the player clicks it off and back on once. Apply() below now starts the actual
    // subscribe/send lazily once a room exists, so this only needs to restore the flag, not also
    // duplicate the InRoom check (which would always fail this early anyway - no room yet at Awake).
    public static void Init()
    {
        _isEnabled = CheatState.SendTestPacket.Value;
    }

    public static void Shutdown()
    {
        if (!_subscribed) return;
        PhotonNetwork.NetworkingClient.EventReceived -= OnEventReceived;
        _subscribed = false;
    }

    private static void EnsureSubscribed()
    {
        if (_subscribed) return;
        PhotonNetwork.NetworkingClient.EventReceived += OnEventReceived;
        _subscribed = true;
    }

    // Wired straight to the "Send Test Packet" checkbox's onClick (see OverlayUI's Misc tab).
    // Turning it on just flips the flag - Apply() below does the actual InRoom check and subscribe,
    // the same lazy path Init() above now relies on to resume a toggle left ON from last session.
    public static void OnToggled(bool enabled)
    {
        _isEnabled = enabled;

        if (!_isEnabled && _subscribed)
        {
            Plugin.Log.LogInfo("[PacketTestHack] Toggle turned OFF. Stopping packet transmission.");
            PhotonNetwork.NetworkingClient.EventReceived -= OnEventReceived;
            _subscribed = false;
        }
    }

    // Polled from Patches.cs's TimeTickPatch.ApplyStats (~33/sec) like the rest of the menu's
    // per-tick hacks.
    public static void Apply()
    {
        if (!_isEnabled) return;

        if (!PhotonNetwork.InRoom)
        {
            // Only a real "you left mid-session" event if we'd actually started sending - at
            // main-menu (never joined a room yet this session, e.g. right after Init() restored
            // a persisted ON toggle) this is expected and should just wait quietly, not disable
            // itself and re-arm the exact bug Init()'s comment above describes.
            if (_subscribed)
            {
                Plugin.Log.LogWarning("[PacketTestHack] Left the Photon room - stopping transmission.");
                PhotonNetwork.NetworkingClient.EventReceived -= OnEventReceived;
                _subscribed = false;
                _isEnabled = false;
                CheatState.SendTestPacket.Value = false; // keep the checkbox in sync with reality
            }
            return;
        }

        if (!_subscribed)
        {
            EnsureSubscribed(); // safe here - InRoom guarantees the game's own client is already up
            _lastSendTick = System.Environment.TickCount - CheatState.PacketTestIntervalMs.Value; // Отправить первый пакет сразу же
            Plugin.Log.LogInfo($"[PacketTestHack] Sending empty test packets every {CheatState.PacketTestIntervalMs.Value}ms.");
        }

        int now = System.Environment.TickCount;
        if (now - _lastSendTick < CheatState.PacketTestIntervalMs.Value) return;
        _lastSendTick = now;

        byte[] payload = System.Array.Empty<byte>();
        var options = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
        PhotonNetwork.RaiseEvent(TestEventCode, payload, options, SendOptions.SendReliable);

        Plugin.Log.LogInfo("[PacketTestHack] Packet sent.");
    }

    private static void OnEventReceived(EventData photonEvent)
    {
        if (photonEvent.Code != TestEventCode) return;

        string text = photonEvent.CustomData is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : photonEvent.CustomData?.ToString();

        Plugin.Log.LogInfo($"[PacketTestHack] Received test packet from actor {photonEvent.Sender}: {text}");
    }
}