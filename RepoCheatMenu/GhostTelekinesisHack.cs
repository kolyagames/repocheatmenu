// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using UnityEngine;

namespace RepoCheatMenu;

// Ghost / Invisible telekinesis: pick up a PhysGrabObject and hold it in front of your camera, no hands
// needed. Named "ghost" historically, now also allowed while Invisible / Fly+Noclip is hiding you - the
// SetPositionRPC rides on the OBJECT's own view, not ours, so GhostNetFilterPatch does not touch it and
// the host accepts it from any guest, same as a Trap Room door teleport. Note: in Invisible mode the
// moved valuable will visually float at your REAL head position (not the broadcast sky position), so
// other players can tell "something is being telekinetically moved from this spot" even if they can't
// see you - that is a side effect of the game not having a per-object owner-position override.
//
// Why this works for a ghost at all: PhysGrabObject.Teleport, confirmed in the decompile, is
//     if (SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClient())
//         photonView.RPC("SetPositionRPC", RpcTarget.MasterClient, position, rotation);
//     else
//         SetPositionRPC(position, rotation);
// - that RPC rides on the OBJECT'S own view, not ours. GhostNetFilterPatch only drops events for views
// we created (CreatorActorNr == us), and the arguments here are Vector3/Quaternion, so the "args carry
// our view ID" clause does not match either. The RPC passes the filter and lands at the host as if any
// grabber had moved the object. TrapRoom and LockAllDoors already lean on this same loophole; this just
// aims it at whatever the ghost is looking at.
//
// Not sent, deliberately: PhysGrabber.GrabStartedRPC / grab beam RPCs - those are on our PhysGrabber view
// (CreatorActorNr == us), so the filter would drop them. We never pretend to be grabbing; we only move
// the object. On the host side there is no "someone is holding this" bookkeeping, so it just reads as the
// object randomly teleporting - same as a Trap Room door.
//
// No throw-impulse on release: velocity is broadcast via OnPhotonSerializeView, not via an RPC on the
// object's view, and the ghost's own broadcasts are filtered out, so no impulse the ghost sets locally
// would ever reach the real host simulation. Releasing just stops teleporting. Falling takes over.
internal static class GhostTelekinesisHack
{
    private static PhysGrabObject _held;
    private static float _distance = 3.5f;
    private const float PickRange = 50f;
    private const float MinDistance = 1f;
    private const float MaxDistance = 15f;

    // A thin cyan beam from camera to the held object so the ghost can see where the telekinesis is
    // pointing (there is no PhysGrabber grab beam while the ghost is active - OverrideGrabDisable
    // keeps it off every tick). Procedural LineRenderer on a hidden GameObject, created lazily.
    private static GameObject _beamGo;
    private static LineRenderer _beam;

    // SetPositionRPC rides the object's own view, so the ghost filter never drops it and the host
    // accepts it from any guest - a visible, living guest can move props exactly the same way a
    // ghost or an invisible player can. Earlier versions gated this to Ghost/Invisible as "staying
    // in character"; after live reports that telekinesis "stopped working", the gate was dropped -
    // the toggle alone is enough. The only side effect while visible is that the valuable floats
    // visibly in front of your crosshair, which is the whole point.
    internal static bool Allowed => CheatState.GhostTelekinesis.Value;

    // Toggle via a bind: first press picks up what the crosshair is on, second press drops.
    public static void Toggle()
    {
        if (!Allowed)
        {
            DynamicIsland.Notify("Телекинез", "Включите «Телекинез» в меню", false);
            return;
        }

        if (_held != null)
        {
            // Shift held at release time -> toss. There is no sanctioned way to transmit a Rigidbody
            // impulse from a ghost (velocity broadcasts via OnPhotonSerializeView, our filter drops
            // those), but a Teleport of ~2m along the camera forward IS one SetPositionRPC the host
            // interpolates - from the host's point of view the object jumps forward in one physics
            // step, which reads as a hard shove. Not a real velocity, so it stops immediately on the
            // next host simulation tick; good enough for a prank, not for aimed shots.
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Camera.main != null)
            {
                Vector3 slamTo = _held.transform.position + Camera.main.transform.forward * 2.5f;
                GameReflect.TeleportSafe(_held, slamTo, _held.transform.rotation, "GhostTelekinesis.toss");
                DynamicIsland.Notify("Телекинез", "Пнул: " + ShortName(_held), true);
            }
            else
            {
                DynamicIsland.Notify("Телекинез", "Отпустил: " + ShortName(_held), true);
            }
            HideBeam();
            _held = null;
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        // We cast against every collider on the way - a PhysGrabObject usually has its colliders on
        // children, so a layer-only cast misses as often as it hits. RaycastAll gives us the full ordered
        // hit list, from which we pick the nearest hit whose parent chain has a PhysGrabObject component.
        RaycastHit[] hits = Physics.RaycastAll(ray, PickRange, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            DynamicIsland.Notify("Телекинез", "Прицельтесь на предмет", false);
            return;
        }
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            PhysGrabObject obj = hit.collider != null ? hit.collider.GetComponentInParent<PhysGrabObject>() : null;
            if (obj == null) continue;
            // Rooted/dead props (level geometry, destroyed objects) have no rigidbody or are set
            // kinematic by the game; Teleport still goes through but there is no payoff.
            if (obj.GetComponent<Rigidbody>() == null) continue;
            _held = obj;
            _distance = Mathf.Clamp(CheatState.GhostTelekinesisDistance.Value, MinDistance, MaxDistance);
            DynamicIsland.Notify("Телекинез", "Взял: " + ShortName(obj), true);
            return;
        }
        DynamicIsland.Notify("Телекинез", "В луче нет предмета", false);
    }

    public static void Tick()
    {
        // The toggle itself blocks when the ghost is off, but the ghost can also end mid-scene while an
        // item is still in hand - drop it quietly so the next time we come back we are not teleporting
        // an orphaned reference.
        if (_held == null) { HideBeam(); return; }
        if (!Allowed)
        {
            HideBeam();
            _held = null;
            return;
        }
        // The object may have been destroyed between frames (ownership transfer, scene change). Unity's
        // overloaded == spots a dead Object even when the C# reference is still live.
        if ((UnityEngine.Object)_held == null || _held.transform == null)
        {
            HideBeam();
            _held = null;
            return;
        }

        // Wheel adjusts distance live - but only while neither menu is open (otherwise scrolling the
        // menu list would unintentionally reel items in). The grabber's own wheel UI is suppressed
        // for the ghost (OverrideGrabDisable in GhostObserverHack.Tick keeps the grab beam off).
        if (!OverlayUI.IsOpen && !CustomOverlayUI.IsOpen && !UiInput.TypingInMenu())
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _distance = Mathf.Clamp(_distance + wheel * 0.5f, MinDistance, MaxDistance);
                CheatState.GhostTelekinesisDistance.Value = _distance;
            }
        }

        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 target = cam.transform.position + cam.transform.forward * _distance;
        if (!GameReflect.TeleportSafe(_held, target, _held.transform.rotation, "GhostTelekinesis"))
        {
            // Teleport can throw if photonView/photonTransformView was torn down (scene change,
            // ownership transfer). Drop the hold rather than spamming the log every tick.
            HideBeam();
            _held = null;
            return;
        }
        DrawBeam(cam.transform.position + cam.transform.forward * 0.4f, target);
    }

    private static void EnsureBeam()
    {
        if (_beam != null) return;
        _beamGo = new GameObject("RepoCheatMenu_GhostTkBeam") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(_beamGo);
        _beam = _beamGo.AddComponent<LineRenderer>();
        // Sprites/Default plays nicely with vertex colour alpha and is present at runtime in every
        // Unity build we patch - no shader shipped from our side needed.
        _beam.material = new Material(Shader.Find("Sprites/Default"));
        _beam.useWorldSpace = true;
        _beam.positionCount = 2;
        _beam.startWidth = 0.015f;
        _beam.endWidth = 0.025f;
        _beam.startColor = new Color(0.3f, 1f, 1f, 0.9f);
        _beam.endColor = new Color(0.3f, 1f, 1f, 0.4f);
        _beam.enabled = false;
    }

    private static void DrawBeam(Vector3 from, Vector3 to)
    {
        EnsureBeam();
        if (_beam == null) return;
        _beam.enabled = true;
        _beam.SetPosition(0, from);
        _beam.SetPosition(1, to);
    }

    private static void HideBeam()
    {
        if (_beam != null) _beam.enabled = false;
    }

    public static string KeyHint()
    {
        if (!CheatState.GhostTelekinesis.Value) return "";
        Keybind kb = KeybindManager.Find((Action)Toggle);
        string key = (kb != null && kb.Vk.Value != 0) ? Win32.VkName(kb.Vk.Value) : "не назначен";
        return "  -> Клавиша «Схватить/отпустить»: " + key;
    }

    public static string Status()
    {
        if (!CheatState.GhostTelekinesis.Value) return "  -> Выключено";
        if (_held == null)
        {
            string crosshair = InspectCrosshair();
            if (!string.IsNullOrEmpty(crosshair)) return "  -> На прицеле: " + crosshair + "  (бинд — взять, Shift+бинд — пнуть)";
            return "  -> Нажмите «Схватить/отпустить», прицелившись (колесо — дистанция, Shift+бинд — пнуть)";
        }
        string price = GetDollarValue(_held);
        string suffix = price != null ? "  — " + price : "";
        return "  -> Держу: " + ShortName(_held) + suffix + "  (" + _distance.ToString("0.0") + " м)";
    }

    // Fast ray-based inspection: what is the ghost looking at right now? Used only for the status
    // line, so a tight range (12m) is enough. GhostObserverHack.Tick runs OverrideGrabDisable each
    // tick so the normal grab beam does not fight for the same information visually.
    private static string InspectCrosshair()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
            return null;
        PhysGrabObject obj = hit.collider != null ? hit.collider.GetComponentInParent<PhysGrabObject>() : null;
        if (obj == null) return null;
        string name = ShortName(obj);
        string price = GetDollarValue(obj);
        return price != null ? name + " (" + price + ")" : name;
    }

    private static string GetDollarValue(PhysGrabObject obj)
    {
        ValuableObject val = obj != null ? obj.GetComponent<ValuableObject>() : null;
        if (val == null) return null;
        // dollarValueCurrent is `internal` on ValuableObject.cs: GameReflect bypasses Mono's
        // runtime access check that would otherwise throw FieldAccessException from here.
        float dollars = GameReflect.Get<float>(val, "dollarValueCurrent");
        if (dollars <= 0f) return null;
        return "$" + dollars.ToString("0");
    }

    private static string ShortName(PhysGrabObject obj)
    {
        if (obj == null) return "?";
        string n = obj.name;
        int paren = n.IndexOf('(');
        if (paren > 0) n = n.Substring(0, paren).Trim();
        return string.IsNullOrEmpty(n) ? "предмет" : n;
    }
}
