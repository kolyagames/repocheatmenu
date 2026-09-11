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
    public static void Init()
    {
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
    // Turning it on starts an infinite loop that Apply() below drains over time at a capped rate.
    public static void OnToggled(bool enabled)
    {
        _isEnabled = enabled;

        if (!_isEnabled)
        {
            Plugin.Log.LogInfo("[PacketTestHack] Toggle turned OFF. Stopping packet transmission.");
            return;
        }

        if (!PhotonNetwork.InRoom)
        {
            Plugin.Log.LogWarning("[PacketTestHack] Not in a Photon room - nothing to send to.");
            _isEnabled = false;
            return;
        }

        EnsureSubscribed(); // safe here - InRoom guarantees the game's own client is already up
        _lastSendTick = System.Environment.TickCount - CheatState.PacketTestIntervalMs.Value; // Отправить первый пакет сразу же
        Plugin.Log.LogInfo($"[PacketTestHack] Toggle turned ON. Sending empty test packets every {CheatState.PacketTestIntervalMs.Value}ms.");
    }

    // Polled from Patches.cs's TimeTickPatch.ApplyStats (~33/sec) like the rest of the menu's
    // per-tick hacks - drains the burst at BurstIntervalMs spacing regardless of how often this
    // is called.
    public static void Apply()
    {
        if (!_isEnabled) return;

        if (!PhotonNetwork.InRoom)
        {
            Plugin.Log.LogWarning("[PacketTestHack] Left the Photon room - stopping transmission.");
            _isEnabled = false;
            return;
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