// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace RepoCheatMenu;

// "Призрак-наблюдатель": ваш аватар существует только у вас, для остальных клиентов его нет.
//
// Механика (сверено с PhotonUnityNetworking из REPO_Data\Managed): весь исходящий трафик PUN идёт
// через LoadBalancingClient.OpRaiseEvent (PhotonNetwork.RaiseEventInternal -> NetworkingClient.
// OpRaiseEvent). Мы не мешаем игре создавать аватар локально (PhotonNetwork.Instantiate по-прежнему
// выдаёт ViewID и регистрирует вид), а только режем исходящие события:
//   202 Instantiate / 204 Destroy  - совсем (чужие клиенты так и не узнают о ваших объектах);
//   200 RPC                         - те, что адресованы видам, созданным нами (CreatorActorNr == мы);
//   201/206 Serialize               - из пакета вырезаются записи наших видов.
// Сцено-виды (CreatorActorNr == 0: NetworkManager, LevelGenerator, ...) идут как обычно, на них
// держатся счётчики готовности хоста («PlayerSpawnedRPC» считается по отправителю, не по аватару),
// поэтому загрузка уровня у хоста не зависает.
// Голос: PunVoiceClient.ConnectVoice/JoinVoiceRoom отключены - иначе вход в голосовую комнату виден
// остальным голосовым клиентам. Плата: вы не слышите и не говорите.
// Невозможно скрыть: сам факт входа в комнату Photon (OnPlayerEnteredRoom у всех, ник, PlayerCount).
// Только гость: у мастера (хоста) режим не включается. Включать нужно до захода в комнату - решение
// «призрак или нет» защёлкивается в NetworkManager.Start каждой сцены.
internal static class GhostObserverHack
{
    // Защёлка на сцену: аватар, созданный до защёлки, уже известен остальным и отключить его нельзя.
    internal static bool SessionGhost;
    internal static int DroppedInstantiate, DroppedDestroy, DroppedRpc, DroppedViewUpdates, FilterErrors;
    internal static int VoiceLetThrough; // how many "Voice" 202-events the filter deliberately passed
    // Anonymous-speak state: once the recorder's UserData has been scrubbed for the current scene, we
    // leave it alone so RestartRecording() does not fire every tick. Reset on every scene latch and
    // also whenever the recorder identity changes (the game can re-create the Recorder mid-scene when
    // voice reconnects, which silently drops our scrub if we only compared "configured yet?" by bool).
    internal static bool AnonymousVoiceConfigured;
    internal static int AnonymousScrubbedRecorderId;
    internal static bool AnonymousVoiceLiveStatus;
    internal static int AnonymousRecorders;      // recorders scanned this tick
    internal static int AnonymousRecordersReady; // RecordingEnabled && TransmitEnabled && UserData==null
    private static float _voiceDiagNext;
    // Cache PhotonVoiceView list - FindObjectsByType is a scene-wide reflection sweep every frame
    // (every ~0.15s hack tick) would be pure overhead for 1-2 views per player avatar.
    private static Photon.Voice.PUN.PhotonVoiceView[] _voiceViewCache;
    private static float _voiceViewRescanAt;

    internal static void InvalidateVoiceCache()
    {
        _voiceViewCache = null;
        _voiceViewRescanAt = 0f;
        AnonymousScrubbedRecorderId = 0;
    }

    internal static bool Filtering(LoadBalancingClient client)
    {
        if (!SessionGhost) return false;
        if (!ReferenceEquals(client, PhotonNetwork.NetworkingClient)) return false;
        return client.LocalPlayer != null && !client.LocalPlayer.IsMasterClient;
    }

    internal static bool IsOurView(int viewId, int localActor)
    {
        PhotonView view = PhotonView.Find(viewId);
        if (view != null) return !view.IsRoomView && view.CreatorActorNr == localActor;
        return viewId / PhotonNetwork.MAX_VIEW_IDS == localActor;
    }

    // Расстановку по точкам старта делает хост: LevelGenerator.PlayerSpawn() шлёт SpawnRPC каждому
    // аватару из СВОЕГО PlayerList, а призрака там нет. Без этого призрак остался бы там, где аватар
    // создан ((0,0,2) из PlayerAvatar.Awake), с spawned == false. Поэтому призрак сам вызывает игровой
    // SpawnRPC на своём аватаре, локально, с отправителем «хост» (иначе MasterOnlyRPC его отвергнет).
    // spawnImpulse гасится, чтобы аватар не слал хосту PlayerSpawnedRPC: хост считает его по
    // отправителю, и лишний голос мог бы закончить его ожидание раньше настоящего игрока.
    private static float _nextSpawnCheck;

    internal static void Tick()
    {
        // Grabbing cannot reach anyone (the host never hears about it), but the local beam still showed and
        // the item looked held on our screen only. The game's own grab-disable timer keeps the ghost hands-off.
        // Ghost telekinesis takes a different path (PhysGrabObject.Teleport, scene view) that goes through
        // the ghost filter anyway, so this ban doesn't affect it.
        if (SessionGhost && PhysGrabber.instance != null) PhysGrabber.instance.OverrideGrabDisable(0.2f);
        // Invisible-block-grab: a grab in invisibility reads to the host as a hover beam from the sky
        // and the picked valuable's SetPositionRPC teleports to your sky coordinates - everyone sees
        // the valuable fly away. Blocking the grab silently while invisible keeps the trick quiet.
        else if (CheatState.InvisibleBlockGrab.Value && IsInvisibleHidden
                 && PhysGrabber.instance != null && PhotonNetwork.InRoom)
            PhysGrabber.instance.OverrideGrabDisable(0.2f);
        ApplyAnonymousVoice();
        try { AnonymousVoiceReceiver.Tick(); }
        catch (System.Exception ex) { Plugin.Log.LogWarning("[Ghost] anonymous voice receiver: " + ex.Message); }

        if (!SessionGhost || Time.unscaledTime < _nextSpawnCheck) return;
        _nextSpawnCheck = Time.unscaledTime + 0.25f;

        PlayerAvatar avatar = PlayerAvatar.instance;
        if (avatar == null || LevelGenerator.Instance == null) return;
        // Need a live photonView to build the PhotonMessageInfo "pretend master is calling" trick -
        // without it the master-only guard inside SpawnRPC rejects us, or the info constructor NREs.
        if (avatar.photonView == null) return;
        if (PhotonNetwork.MasterClient == null) return;
        if (GameReflect.Get<bool>(avatar, "spawned")) return;

        SpawnPoint[] points = Object.FindObjectsOfType<SpawnPoint>();
        if (points.Length == 0) return;
        SpawnPoint point = points[Random.Range(0, points.Length)];
        if (point == null || point.transform == null) return;

        try
        {
            var info = new PhotonMessageInfo(PhotonNetwork.MasterClient, PhotonNetwork.ServerTimestamp, avatar.photonView);
            GameReflect.Invoke(avatar, "SpawnRPC", point.transform.position, point.transform.rotation, info);
            GameReflect.Set(avatar, "spawnImpulse", false);
            Plugin.Log.LogInfo("[Ghost] spawned locally at " + point.transform.position);
        }
        catch (System.Exception ex)
        {
            // One-shot: without this, a bad SpawnPoint (destroyed between FindObjectsOfType and the
            // Invoke, or a torn-down PhotonView on the avatar during a scene swap) would surface as a
            // single logged exception and _nextSpawnCheck would still have advanced, so the retry
            // only happens 250 ms later anyway - but a plain Invoke bubbles the throw out through the
            // RunSafe wrapper around the whole Tick and leaves the warning hard to find in the log.
            Plugin.Log.LogWarning("[Ghost] SpawnRPC failed, retrying next tick: " + ex.GetType().Name + " " + ex.Message);
            _nextSpawnCheck = 0f;
        }
    }

    // Ghost speak: scrub Recorder.UserData -> null so receivers fall into the "anonymous Speaker" branch
    // (PunVoiceClient.InstantiateSpeakerForRemoteVoice, decompile: userData == null => InstantiateSpeakerPrefab
    // on the PunVoiceClient GameObject, destroyOnRemove: true). Without this scrub, UserData is our
    // PhotonVoiceView.ViewID, which no one else can resolve (we have no PhotonView on their side) and the
    // voice just gets dropped silently on receive.
    //
    // UserData setter calls RestartRecording(); doing it once per scene is enough. If the player flipped
    // GhostSpeak on mid-session, this also kicks RecordingEnabled on (ConnectVoice may already have gone
    // through in listen-only, with the mic blocked, so just unblocking the StartRecording patch is not
    // enough to actually start transmitting - the voice stream has to be created).
    // Being heard while Invisible: the Invisible broadcast ships our avatar to the sky, and the
    // PlayerVoiceChat Speaker lives on the avatar transform, so other players hear us from the
    // sky = inaudible. Scrubbing Recorder.UserData -> null makes receivers spawn the anonymous
    // Speaker on PunVoiceClient's own GameObject (same path as GhostSpeak), so our voice reaches
    // everyone without having to broadcast the real position (InvisibleRevealWhileTalking's
    // trade-off). Only when Invisible/Fly+Noclip-hide is actually hiding us.
    internal static bool IsInvisibleHidden =>
        CheatState.Invisible.Value
        || (CheatState.FlyAutoHide.Value && (CheatState.Fly.Value || CheatState.Noclip.Value));

    internal static bool WantInvisibleSpeak => IsInvisibleHidden && CheatState.InvisibleSpeak.Value && !SessionGhost;

    private static void ApplyAnonymousVoice()
    {
        try { ApplyAnonymousVoiceCore(); }
        catch (System.Exception ex)
        {
            // UserData setter calls Recorder.RestartRecording(), which can throw if the voice client
            // is in a transient state (room swap, mic hot-plug, audio driver glitch). Dropping that
            // throw would take down the whole GhostObserverHack Tick - the mod-level RunSafe wrapper
            // catches it eventually, but swallowing here lets the rest of the Tick (SpawnRPC retry,
            // OverrideGrabDisable) keep running in the same tick.
            if (System.DateTime.UtcNow.Ticks - _lastVoiceErrorLog > 50_000_000L) // 5 s throttle
            {
                _lastVoiceErrorLog = System.DateTime.UtcNow.Ticks;
                Plugin.Log.LogWarning("[Ghost] ApplyAnonymousVoice throttled error: " + ex.GetType().Name + " " + ex.Message);
            }
        }
    }
    private static long _lastVoiceErrorLog;

    private static void ApplyAnonymousVoiceCore()
    {
        // Ghost speak no longer scrubs UserData: the Voice-prefab 202-event is let through the ghost
        // filter now, so every receiver has our real PhotonVoiceView and the normal (not-anonymous)
        // voice path works. The old UserData=null anonymous route is kept for Invisible-speak: an
        // Invisible player's PlayerAvatar DOES exist on the receivers, their Voice-prefab's transform
        // tracks it to the sky, and 3D audio makes it inaudible - so Invisible still needs the
        // anonymous Speaker on PunVoiceClient (requires either the game's own SpeakerPrefab or the
        // fallback in AnonymousVoiceReceiver, i.e. still mod-only on that side).
        bool wantInvis = WantInvisibleSpeak;
        bool want = wantInvis;
        AnonymousVoiceLiveStatus = false;
        if (!want)
        {
            AnonymousVoiceConfigured = false;
            return;
        }
        // IMPORTANT: there are usually many Recorder-s in the scene - one per PhotonVoiceView, i.e.
        // one per player's avatar for their TTS stream. We ONLY want our own: scrubbing a remote
        // player's TTS recorder breaks their TTS Speaker on receivers. Our view is the one whose
        // photonView.IsMine is true.
        //
        // The transmit=False problem is NOT our scrub: PlayerVoiceChat.Update writes to
        // recorder.TransmitEnabled every frame by the game's own rules (microphoneEnabled &&
        // !toggleMute && (!pushToTalk || InputHold(PushToTalk))). Fighting it from a tick is
        // pointless - a Harmony Prefix on Recorder.set_TransmitEnabled (installed in
        // InstallVoicePatches) overrides every one of those writes while GhostSpeak is on.
        if (_voiceViewCache == null || Time.unscaledTime >= _voiceViewRescanAt)
        {
            _voiceViewCache = UnityEngine.Object.FindObjectsByType<Photon.Voice.PUN.PhotonVoiceView>(FindObjectsSortMode.None);
            _voiceViewRescanAt = Time.unscaledTime + 1f;
        }

        Photon.Voice.Unity.Recorder ownRec = null;
        foreach (Photon.Voice.PUN.PhotonVoiceView view in _voiceViewCache)
        {
            if (view == null) continue;
            var pv = view.GetComponent<Photon.Pun.PhotonView>();
            if (pv == null || !pv.IsMine) continue;
            ownRec = view.RecorderInUse;
            break;
        }

        if (ownRec != null)
        {
            // Identity check: if the game created a NEW Recorder mid-scene (voice reconnect, scene
            // partial reload, PhotonVoiceView swap) we must scrub UserData on this new instance even
            // though AnonymousVoiceConfigured was already true for the old one.
            int id = ownRec.GetInstanceID();
            if (id != AnonymousScrubbedRecorderId)
            {
                AnonymousScrubbedRecorderId = id;
                AnonymousVoiceConfigured = false;
            }
            if (ownRec.UserData != null) ownRec.UserData = null;
            if (!ownRec.RecordingEnabled) ownRec.RecordingEnabled = true;
            // TransmitEnabled is forced through the Prefix patch now - no race with
            // PlayerVoiceChat.Update anymore.
            AnonymousRecorders = 1;
            AnonymousRecordersReady = (ownRec.RecordingEnabled && ownRec.TransmitEnabled && ownRec.UserData == null) ? 1 : 0;
            if (ownRec.IsCurrentlyTransmitting) AnonymousVoiceLiveStatus = true;
        }
        else
        {
            AnonymousRecorders = 0;
            AnonymousRecordersReady = 0;
        }

        if (!AnonymousVoiceConfigured && ownRec != null)
        {
            AnonymousVoiceConfigured = true;
            Plugin.Log.LogInfo("[Ghost] own recorder set up: " + ownRec.name);
        }

        // Heartbeat every ~5s with the full state (RecordingEnabled / TransmitEnabled /
        // IsCurrentlyTransmitting) for diagnosing silent-recording cases - OUR recorder only.
        if (Time.unscaledTime >= _voiceDiagNext && ownRec != null)
        {
            _voiceDiagNext = Time.unscaledTime + 5f;
            Plugin.Log.LogInfo("[Ghost] voice " + ownRec.name + ": rec=" + ownRec.RecordingEnabled
                + " transmit=" + ownRec.TransmitEnabled + " live=" + ownRec.IsCurrentlyTransmitting
                + " userData=" + (ownRec.UserData?.ToString() ?? "null"));
        }
    }

    // Set once this room has seen our avatar announced normally. From then on hiding is impossible until
    // we leave, and blocking our mic would only make us a visible player who silently can't talk.
    internal static bool VisibleThisRoom;

    internal static string Status()
    {
        if (SessionGhost && !CheatState.GhostObserver.Value)
            return "  -> Выключается: со следующей сцены вы появитесь у всех (сейчас ещё скрыты)";
        if (SessionGhost)
        {
            string voice;
            if (CheatState.GhostSpeak.Value)
            {
                string route = VoiceLetThrough > 0
                    ? ". Слышат все (голос через голосовой PhotonView, 2D)"
                    : ". Готовится (голос начнёт работать на следующей сцене - войдите заново)";
                voice = ". Голос: включён" + route;
            }
            else if (ListenOnly) voice = ". Голос: только слушаете (чтобы говорить - включите «Говорить голосом»)";
            else voice = ". Голос: выключен";
            return "  -> АКТИВЕН: в игре вас нет. Отрезано: создание " + DroppedInstantiate +
                   ", RPC " + DroppedRpc + ", синхр. " + DroppedViewUpdates + voice +
                   (FilterErrors > 0 ? ". ОШИБОК ФИЛЬТРА: " + FilterErrors + " (см. лог, возможна утечка)" : "") +
                   ". Видно всё равно: вход в комнату, Steam-лобби, список Photon (хост с модом может кикнуть)";
        }
        if (!CheatState.GhostObserver.Value)
            return "  -> Выключено. Включайте ДО захода в комнату";
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            return "  -> Не работает: вы хост. Режим только для гостя";
        if (PhotonNetwork.InRoom && VisibleThisRoom)
            return "  -> Не включился: вас уже увидели в этой комнате. Выйдите и зайдите заново";
        return "  -> Готов: включится при заходе в комнату (только гость)";
    }

    internal static void InstallVoicePatches(Harmony harmony)
    {
        System.Type voice = AccessTools.TypeByName("Photon.Voice.PUN.PunVoiceClient");
        if (voice == null)
        {
            Plugin.Log.LogWarning("[Ghost] PunVoiceClient not found - voice stays connected.");
            return;
        }
        foreach (string name in new[] { "ConnectVoice", "JoinVoiceRoom" })
        {
            var method = AccessTools.Method(voice, name);
            if (method == null)
            {
                Plugin.Log.LogWarning("[Ghost] " + name + " not found.");
                continue;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(GhostVoicePatch), "Prefix")));
        }

        // Listen-only: the voice room itself shows nothing in-game, only our own outgoing stream would
        // (Recorder.StartRecording creates the LocalVoice and announces it to the voice room).
        var startRecording = AccessTools.Method(typeof(Photon.Voice.Unity.Recorder), "StartRecording");
        if (startRecording == null)
            Plugin.Log.LogWarning("[Ghost] Recorder.StartRecording not found - listen-only mode falls back to no voice.");
        else
            harmony.Patch(startRecording, prefix: new HarmonyMethod(AccessTools.Method(typeof(GhostRecorderPatch), "Prefix")));
        ListenOnlySupported = startRecording != null;

        // PhotonVoiceView.SetupRecorder (private) writes recorder.UserData = photonView.ViewID right
        // before the Recorder is added to the voice client. If this runs AFTER our Tick already set
        // UserData=null for the ghost, the ViewID is restored and receivers no longer find the
        // anonymous-Speaker branch. Postfix on that private method, keyed on the ghost+GhostSpeak gate,
        // scrubs it back to null before the Recorder is registered.
        var setupRec = AccessTools.Method(AccessTools.TypeByName("Photon.Voice.PUN.PhotonVoiceView"), "SetupRecorder");
        if (setupRec == null)
            Plugin.Log.LogWarning("[Ghost] PhotonVoiceView.SetupRecorder not found - UserData override may be overwritten.");
        else
            harmony.Patch(setupRec, postfix: new HarmonyMethod(AccessTools.Method(typeof(GhostVoiceViewPatch), "Postfix")));

        // Prefix on Recorder.set_TransmitEnabled - PlayerVoiceChat.Update stamps it to false
        // every frame when the game's mute / push-to-talk conditions say so. While GhostSpeak is
        // on, force the written value to true before the setter stores it.
        var setTransmit = AccessTools.PropertySetter(typeof(Photon.Voice.Unity.Recorder), "TransmitEnabled");
        if (setTransmit == null)
            Plugin.Log.LogWarning("[Ghost] Recorder.set_TransmitEnabled not found - PlayerVoiceChat will keep muting us.");
        else
            harmony.Patch(setTransmit, prefix: new HarmonyMethod(AccessTools.Method(typeof(GhostTransmitForcePatch), "Prefix")));
    }

    internal static bool ListenOnlySupported;

    // Same condition as the voice-connect gate, not SessionGhost: voice connects in the lobby before the
    // per-scene latch exists, and the mic must already be off by then.
    //
    // GhostSpeak overrides it: the mic goes out anyway, but with UserData scrubbed to null so receivers
    // fall into PunVoiceClient.InstantiateSpeakerForRemoteVoice's "userData == null" branch and spawn an
    // anonymous Speaker (not tied to our non-existent PhotonVoiceView).
    internal static bool BlockOwnVoice => !CheatState.GhostSpeak.Value &&
        (SessionGhost || (CheatState.GhostObserver.Value && !PhotonNetwork.IsMasterClient && !VisibleThisRoom));

    internal static bool ListenOnly => !CheatState.GhostSpeak.Value
        && (SessionGhost || (CheatState.GhostObserver.Value && !PhotonNetwork.IsMasterClient && !VisibleThisRoom))
        && CheatState.GhostObserverHear.Value && ListenOnlySupported;

    // Short-circuit for features that rely on an RPC off our own PlayerAvatar / PhysGrabber view
    // (chat, animation speed spoof, face expression, fake hurt, heal, grab/release broadcasts, …).
    // Under a session ghost, GhostNetFilterPatch drops every one of those on the wire - the toggle
    // would appear to work locally but nobody else would see anything. Features check this and
    // early-return instead of pretending to work.
    internal static bool LocalRpcIsMuted => SessionGhost;
}

internal static class GhostVoicePatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!GhostObserverHack.BlockOwnVoice || GhostObserverHack.ListenOnly) return true;
        __result = false;
        return false;
    }
}

internal static class GhostRecorderPatch
{
    private static bool Prefix() => !GhostObserverHack.BlockOwnVoice;
}

// Prefix on Recorder.set_TransmitEnabled: whenever Ghost Speak is on, force the incoming value to
// true before the setter stores it. Without this, PlayerVoiceChat.Update's own writes (which run
// every frame on the local avatar's PlayerVoiceChat and reset to false when the game thinks the
// mic is muted or push-to-talk is off) keep the mic silent. We only touch the local recorder's
// setter when the ghost+speak gate is on, so remote players' TTS recorders stay untouched.
internal static class GhostTransmitForcePatch
{
    private static void Prefix(Photon.Voice.Unity.Recorder __instance, ref bool value)
    {
        if (!GhostObserverHack.SessionGhost || !CheatState.GhostSpeak.Value) return;
        if (__instance == null) return;
        // UserData scrubbed to null = this is the one we care about (own recorder; chat/TTS
        // recorders of remote avatars never go through this gate because we don't touch them).
        if (__instance.UserData != null) return;
        if (!value) value = true;
    }
}

// Postfix on PhotonVoiceView.SetupRecorder: if ghost+GhostSpeak is on when SetupRecorder runs, undo
// the UserData = ViewID assignment the method just made (private field is accessed via the public
// Recorder property on the same instance). We also force the next ApplyAnonymousVoice tick to run
// again - setting UserData from this postfix would normally trigger Recorder.RestartRecording() by
// itself, but keeping both paths wired is cheap and both go through the same setter.
internal static class GhostVoiceViewPatch
{
    private static void Postfix(Photon.Voice.PUN.PhotonVoiceView __instance)
    {
        try
        {
            // Ghost speak no longer needs this scrub (the Voice prefab reaches receivers now, so UserData
            // = our PhotonVoiceView.ViewID is exactly right). Only Invisible-speak still wants anonymous.
            if (!GhostObserverHack.WantInvisibleSpeak) return;
            Photon.Voice.Unity.Recorder rec = __instance != null ? __instance.RecorderInUse : null;
            if (rec == null) return;
            rec.UserData = null;
            GhostObserverHack.AnonymousVoiceConfigured = true; // next tick leaves it alone
            Plugin.Log.LogInfo("[Ghost] PhotonVoiceView.SetupRecorder override: UserData scrubbed to null.");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning("[Ghost] PhotonVoiceView postfix error: " + ex.Message);
        }
    }
}

// Receiving side of anonymous voices (a ghost / invisible player using this mod speaks with Recorder.UserData
// = null). A receiver plays such a stream only through VoiceConnection.InstantiateSpeakerPrefab, which needs
// PunVoiceClient.SpeakerPrefab - and REPO links every voice to the per-player "Voice" object instead, so it
// may never set one (then Photon logs "SpeakerPrefab is not set." and drops the voice). When the game left
// it empty, this client gets a plain 2D speaker template, so ghosts of other mod users become audible here.
// Players without the mod are not affected by this at all - whether THEY can hear a ghost depends only on
// the game's own prefab, which is logged once (GameHasSpeakerPrefab) and shown in the ghost status.
//
// PunVoiceClient.Instance is not used on purpose: its getter CREATES a new voice client when none exists.
// The private static `instance` field is read instead - no side effects.
internal static class AnonymousVoiceReceiver
{
    internal static bool? GameHasSpeakerPrefab; // null = not seen a voice client yet
    private static float _next;
    private static GameObject _template;

    private static readonly System.Reflection.FieldInfo InstanceField =
        typeof(Photon.Voice.PUN.PunVoiceClient).GetField("instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    internal static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 3f;
        if (GameManager.instance == null || !GameManager.Multiplayer() || InstanceField == null) return;
        if (!(InstanceField.GetValue(null) is Photon.Voice.PUN.PunVoiceClient client) || client == null) return;

        GameObject current = client.SpeakerPrefab;
        if (current != null && current != _template)
        {
            if (GameHasSpeakerPrefab == null)
            {
                GameHasSpeakerPrefab = true;
                Plugin.Log.LogInfo("[Ghost] PunVoiceClient.SpeakerPrefab is set by the game: anonymous (ghost) voices are audible to everyone.");
            }
            return;
        }
        if (GameHasSpeakerPrefab == null)
        {
            GameHasSpeakerPrefab = false;
            Plugin.Log.LogInfo("[Ghost] PunVoiceClient.SpeakerPrefab is NOT set by the game: players without this mod cannot hear " +
                "anonymous (ghost) voices. Installed a local fallback so this client hears ghosts of other mod users.");
        }
        GameObject template = Template();
        if (current != template) client.SpeakerPrefab = template;
    }

    private static GameObject Template()
    {
        if (_template == null)
        {
            // Root object named RepoCheatMenu_* so Plugin.DestroyOwnObjects cleans it up on unload; an instance is
            // created per anonymous remote voice under the PunVoiceClient object and destroyed with that voice.
            _template = new GameObject("RepoCheatMenu_AnonVoiceSpeaker");
            Object.DontDestroyOnLoad(_template);
            AudioSource source = _template.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // the ghost has no position on the receiver's side
            source.volume = 1f;
            _template.AddComponent<Photon.Voice.Unity.Speaker>();
        }
        // Same mixer group as normal player voices once it exists, so the game's voice volume applies.
        AudioSource src = _template.GetComponent<AudioSource>();
        PlayerVoiceChat local = PlayerVoiceChat.instance;
        if (src != null && src.outputAudioMixerGroup == null && local != null && local.mixerMicrophoneSound != null)
            src.outputAudioMixerGroup = local.mixerMicrophoneSound;
        return _template;
    }
}

[HarmonyPatch(typeof(NetworkManager), "Start")]
internal static class GhostSessionLatchPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        bool was = GhostObserverHack.SessionGhost;
        // NetworkManager.Start creates our avatar only when gameMode == 1, so only those scenes matter.
        bool avatarScene = GameManager.instance != null && GameManager.instance.gameMode == 1 && PhotonNetwork.InRoom;
        if (!PhotonNetwork.InRoom) GhostObserverHack.VisibleThisRoom = false;

        // Once visible in this room, staying visible: a ghost from the next scene on would make our
        // avatar vanish for everyone mid-run, which is louder than not hiding at all.
        GhostObserverHack.SessionGhost = avatarScene && CheatState.GhostObserver.Value
            && !PhotonNetwork.IsMasterClient && !GhostObserverHack.VisibleThisRoom;
        if (avatarScene && !GhostObserverHack.SessionGhost) GhostObserverHack.VisibleThisRoom = true;

        if (GhostObserverHack.SessionGhost != was || GhostObserverHack.SessionGhost)
            Plugin.Log.LogInfo("[Ghost] session ghost = " + GhostObserverHack.SessionGhost);
        if (avatarScene && CheatState.GhostObserver.Value && !GhostObserverHack.SessionGhost)
            Plugin.Log.LogWarning(PhotonNetwork.IsMasterClient
                ? "[Ghost] not armed: you are the host."
                : "[Ghost] not armed: already visible in this room, rejoin to hide.");
        GhostObserverHack.FilterErrors = 0;
        GhostObserverHack.DroppedInstantiate = GhostObserverHack.DroppedDestroy = 0;
        GhostObserverHack.DroppedRpc = GhostObserverHack.DroppedViewUpdates = 0;
        GhostObserverHack.AnonymousVoiceConfigured = false;
        // After a scene change the old PhotonVoiceView refs are either destroyed or belong to the previous
        // level - keeping them would spend the first full post-scene second missing the new Recorder.
        GhostObserverHack.InvalidateVoiceCache();
    }
}

[HarmonyPatch(typeof(LoadBalancingClient), "OpRaiseEvent")]
internal static class GhostNetFilterPatch
{
    private static readonly byte KeyViewId = 0;
    private static readonly byte KeyParams = 4;

    // Only an int that resolves to a live, non-room view we created counts, so a stray price or
    // damage number in the same numeric range is not mistaken for a view ID.
    private static bool ReferencesOurView(object[] args, int actor)
    {
        foreach (object arg in args)
        {
            if (arg is not int id || id / PhotonNetwork.MAX_VIEW_IDS != actor) continue;
            PhotonView view = PhotonView.Find(id);
            if (view != null && !view.IsRoomView && view.CreatorActorNr == actor) return true;
        }
        return false;
    }

    [HarmonyPrefix]
    private static bool Prefix(LoadBalancingClient __instance, byte eventCode, object customEventContent)
    {
        try
        {
            return Decide(__instance, eventCode, customEventContent);
        }
        catch (System.Exception ex)
        {
            // A broken filter must be noticed (it means events may leak), but it must never take the
            // game's own networking down with it.
            GhostObserverHack.FilterErrors++;
            if (GhostObserverHack.FilterErrors <= 3)
                Plugin.Log.LogError("[Ghost] filter error, event " + eventCode + " sent unfiltered: " + ex);
            return true;
        }
    }

    private static bool Decide(LoadBalancingClient __instance, byte eventCode, object customEventContent)
    {
        if (!GhostObserverHack.Filtering(__instance)) return true;
        int actor = __instance.LocalPlayer.ActorNumber;

        switch (eventCode)
        {
            case 202:
                // The game's own voice-only PhotonView (NetworkManager.Start spawns it right after
                // the player avatar; the prefab has a PhotonView + PhotonVoiceView + Speaker + Recorder
                // but no visible mesh - PlayerAvatar.LateUpdate drags its transform to the avatar's
                // head position). Letting this one 202 through for a session ghost is enough to make
                // our voice reach every other client without the mod: our Recorder.UserData stays as
                // our PhotonVoiceView.ViewID, the receiver finds the PhotonView, uses the SpeakerInUse
                // on it, and plays the stream. PlayerAvatar is still filtered, so other clients never
                // see our body; the Voice object stays at its spawn position in their scene (nothing
                // drags it there, our PlayerAvatar is not around) and audioSource.spatialBlend stays
                // 0 because PlayerVoiceChat.Update's inLobbyMixer branch holds. 2D voice, heard everywhere.
                if (customEventContent is ExitGames.Client.Photon.Hashtable instEvent
                    && instEvent[(byte)0] is string prefabName
                    && prefabName == "Voice")
                {
                    GhostObserverHack.VoiceLetThrough++;
                    return true;
                }
                GhostObserverHack.DroppedInstantiate++;
                return false;
            case 204:
                GhostObserverHack.DroppedDestroy++;
                return false;
            case 200:
                if (customEventContent is ExitGames.Client.Photon.Hashtable rpc)
                {
                    if (rpc.ContainsKey(KeyViewId) && rpc[KeyViewId] is int rpcView
                        && GhostObserverHack.IsOurView(rpcView, actor))
                    {
                        GhostObserverHack.DroppedRpc++;
                        return false;
                    }
                    // Scene/room objects carry our view IDs as arguments too (PhysGrabObject.GrabStartedRPC
                    // sends our PhysGrabber's ViewID to the host, which then does PhotonView.Find(id).GetComponent
                    // on null - an NRE in the host's console on every grab attempt).
                    if (rpc.ContainsKey(KeyParams) && rpc[KeyParams] is object[] args && ReferencesOurView(args, actor))
                    {
                        GhostObserverHack.DroppedRpc++;
                        return false;
                    }
                }
                return true;
            case 201:
            case 206:
                if (customEventContent is List<object> batch)
                {
                    for (int i = batch.Count - 1; i >= 2; i--)
                    {
                        if (batch[i] is List<object> entry && entry.Count > 0 && entry[0] is int viewId
                            && GhostObserverHack.IsOurView(viewId, actor))
                        {
                            batch.RemoveAt(i);
                            GhostObserverHack.DroppedViewUpdates++;
                        }
                    }
                    if (batch.Count <= 2) return false;
                }
                return true;
            default:
                return true;
        }
    }
}
