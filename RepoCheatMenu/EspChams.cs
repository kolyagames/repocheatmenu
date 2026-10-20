// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// "Shader Mode" fills the actual silhouette of ESP targets (not a generic box) and shows it
// through walls, without writing or compiling any custom shader - that needs a Unity Editor
// asset-bundle pipeline we don't have here. Instead this uses a shader-free trick:
//   1. Clone every Renderer found under the target (SkinnedMeshRenderer shares the source's
//      own `bones`/`rootBone` array directly, so the clone follows the source's animation for
//      free - it really is the entity's shape, not an approximation).
//   2. Put the clones on a dedicated layer that only our own "highlight" camera renders.
//   3. That camera uses CameraClearFlags.Depth, which clears ONLY the depth buffer (not color)
//      right before it draws - so the clones are never depth-tested against the wall the main
//      camera already painted into the same render target this frame, and paint straight over it.
//
// First attempt rendered straight to the screen backbuffer and visibly brightened everything.
// Root cause, confirmed from decompiled RenderTextureMain.cs: this game renders the 3D scene
// into a small offscreen RenderTexture (Camera.main.targetTexture) - that's what gives it its
// chunky low-res look - and a UI RawImage then stretches that texture to fill the real screen.
// Our camera, having no targetTexture, rendered directly onto the real screen AFTER that
// stretch, bypassing the downscale and whatever darkening happens along the way entirely. The
// fix is to target that exact same RenderTexture instead, so our clones get baked into the same
// offscreen image the rest of the frame goes through, before it's stretched to the screen.
internal static class EspChams
{
    private const int HighlightLayer = 31;

    private class Ghost
    {
        public GameObject Root;
        public readonly List<Renderer> Renderers = new();

        // Every GameObject this ghost created, tracked separately from Root because not all of
        // them live under it: static MeshRenderer clones have to be parented to the SOURCE object
        // so they follow it (a skinned clone follows via the shared bone array instead, so those
        // can sit under Root). Destroying Root therefore never reached the mesh clones - they
        // stayed parented inside the game's own objects, on the highlight layer, forever. Turning
        // Shader Mode off and on again just built another set on top, so valuables accumulated
        // overlapping ghost meshes for the whole session. That is why Shader Mode kept getting
        // heavier the longer it ran.
        public readonly List<GameObject> Nodes = new();
    }

    private static readonly Dictionary<Transform, Ghost> _ghosts = new();
    private static readonly HashSet<Transform> _seenThisFrame = new();
    private static readonly MaterialPropertyBlock _block = new();

    private static Camera _highlightCam;
    private static Material _material;
    private static bool _useEmission;
    private static string _colorProperty = "_Color";

    public static void Apply(List<Transform> players, List<Transform> enemies, List<Transform> valuables, List<Transform> carts)
    {
        if (!CheatState.EspShaderMode.Value)
        {
            if (_highlightCam != null) _highlightCam.gameObject.SetActive(false);
            ClearGhosts();
            return;
        }

        if (!EnsureCamera()) return;
        _highlightCam.gameObject.SetActive(true);

        Color color = GetColor();
        // The actual perf problem: Renderer.SetPropertyBlock has no equivalent of Graphic.color's
        // bit-equality skip (see EspOverlay's own rainbow comment for that one) - every call is
        // real work, so re-applying an unchanged color to every renderer of every ESP target on
        // every tick (~33/sec) was pure waste, and directly explains why Rainbow (color
        // legitimately differs each application) and Shader Mode (many renderers per target,
        // especially with several valuables up) were the two reported-heavy toggles - this was
        // their shared bottleneck. Only touching renderers when the color actually changed, or
        // the ghost is brand new this tick, cuts that to ~0/sec at rest and ~8/sec while
        // Rainbow's cycling (matching its own color-recompute throttle) instead of ~33/sec always.
        bool colorChanged = _lastAppliedColor == null || _lastAppliedColor.Value != color;

        _seenThisFrame.Clear();
        Sync(players, color, colorChanged);
        Sync(enemies, color, colorChanged);
        Sync(valuables, color, colorChanged);
        Sync(carts, color, colorChanged);
        RemoveUnseen();

        _lastAppliedColor = color;
    }

    private static Color? _lastAppliedColor;

    private static Color _cachedRainbowColor = Color.green;
    private static float _nextRainbowUpdate;

    // Same throttle as EspOverlay's box markers, same reason: a color that changes every single
    // tick forces real per-frame GPU upload work across every ghost renderer's
    // MaterialPropertyBlock instead of the no-op a genuinely unchanged color gets. ~8/sec still
    // reads as a smooth cycle.
    private static Color GetColor()
    {
        if (CheatState.EspRainbow.Value)
        {
            if (Time.unscaledTime >= _nextRainbowUpdate)
            {
                _nextRainbowUpdate = Time.unscaledTime + 0.12f;
                _cachedRainbowColor = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.3f, 1f), 1f, 1f);
            }
            return _cachedRainbowColor;
        }
        return ColorUtility.TryParseHtmlString(CheatState.EspColor.Value, out Color c) ? c : Color.green;
    }

    private static void Sync(List<Transform> targets, Color color, bool colorChanged)
    {
        if (targets == null) return;

        bool blockPrepared = false;

        foreach (Transform t in targets)
        {
            if (t == null) continue;
            _seenThisFrame.Add(t);

            bool isNew = !_ghosts.TryGetValue(t, out Ghost ghost);
            if (isNew)
            {
                ghost = Build(t);
                if (ghost == null) continue;
                _ghosts[t] = ghost;
            }

            // Existing ghost, color hasn't moved since last tick - nothing to push to the GPU.
            if (!isNew && !colorChanged) continue;

            if (!blockPrepared)
            {
                _block.SetColor(_colorProperty, color);
                if (_useEmission) _block.SetColor("_EmissionColor", color);
                blockPrepared = true;
            }

            foreach (Renderer r in ghost.Renderers)
            {
                if (r != null) r.SetPropertyBlock(_block);
            }
        }
    }

    private static void RemoveUnseen()
    {
        List<Transform> stale = null;
        foreach (var key in _ghosts.Keys)
        {
            if (_seenThisFrame.Contains(key)) continue;
            stale ??= new List<Transform>();
            stale.Add(key);
        }
        if (stale == null) return;

        foreach (Transform key in stale)
        {
            if (_ghosts.TryGetValue(key, out Ghost ghost)) DestroyGhost(ghost);
            _ghosts.Remove(key);
        }
    }

    private static void ClearGhosts()
    {
        foreach (Ghost ghost in _ghosts.Values) DestroyGhost(ghost);
        _ghosts.Clear();
    }

    private static void DestroyGhost(Ghost ghost)
    {
        if (ghost == null) return;

        // Nodes first: the mesh clones parented into the source object are not under Root and
        // would otherwise survive it (see the Ghost class comment).
        foreach (GameObject node in ghost.Nodes)
            if (node != null) Object.Destroy(node);
        ghost.Nodes.Clear();

        if (ghost.Root != null) Object.Destroy(ghost.Root);
    }

    private static Ghost Build(Transform source)
    {
        if (!EnsureMaterial()) return null;

        var root = new GameObject("EspChamsGhost");
        Object.DontDestroyOnLoad(root);

        var ghost = new Ghost { Root = root };

        foreach (Renderer src in source.GetComponentsInChildren<Renderer>(true))
        {
            if (src is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                var node = new GameObject("Skinned") { layer = HighlightLayer };
                node.transform.SetParent(root.transform, false);
                var clone = node.AddComponent<SkinnedMeshRenderer>();
                clone.sharedMesh = smr.sharedMesh;
                clone.bones = smr.bones;
                clone.rootBone = smr.rootBone;
                clone.localBounds = smr.localBounds;
                clone.updateWhenOffscreen = true;
                clone.sharedMaterial = _material;
                clone.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                clone.receiveShadows = false;
                ghost.Renderers.Add(clone);
                ghost.Nodes.Add(node);
            }
            else if (src is MeshRenderer && src.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            {
                var node = new GameObject("Mesh") { layer = HighlightLayer };
                node.transform.SetParent(src.transform, false);
                node.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var clone = node.AddComponent<MeshRenderer>();
                clone.sharedMaterial = _material;
                clone.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                clone.receiveShadows = false;
                ghost.Renderers.Add(clone);
                ghost.Nodes.Add(node);
            }
        }

        if (ghost.Renderers.Count > 0) return ghost;

        DestroyGhost(ghost);
        return null;
    }

    private static bool EnsureMaterial()
    {
        if (_material != null) return true;

        // UI/Default is guaranteed present in this build - the game's entire UI runs on it, and
        // we already use Canvas/Image ourselves - so unlike "Unlit/Color" there's no risk of it
        // having been stripped, and no need to spawn a throwaway primitive to fish a shader out
        // of the live scene.
        Shader shader = Shader.Find("UI/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null) return false;

        _material = new Material(shader) { color = Color.green };
        _colorProperty = _material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        _useEmission = _material.HasProperty("_EmissionColor");
        if (_useEmission)
        {
            _material.EnableKeyword("_EMISSION");
            _material.SetColor("_EmissionColor", Color.green);
        }
        _material.renderQueue = 5000;
        return true;
    }

    private static bool EnsureCamera()
    {
        RenderTexture target = RenderTextureMain.instance != null ? RenderTextureMain.instance.renderTexture : null;
        if (target == null || Camera.main == null) return false;

        if (_highlightCam == null)
        {
            var go = new GameObject("RepoCheatMenu_EspChamsCam");
            Object.DontDestroyOnLoad(go);
            _highlightCam = go.AddComponent<Camera>();
            _highlightCam.cullingMask = 1 << HighlightLayer;
            _highlightCam.clearFlags = CameraClearFlags.Depth;
            _highlightCam.allowHDR = false;
            _highlightCam.allowMSAA = false;
            go.AddComponent<EspChamsCamSync>();
        }

        // Render into the SAME offscreen texture the main camera renders the 3D scene into
        // (instead of the real screen backbuffer) - see the class comment for why. Re-applied
        // every call since RenderTextureMain can swap the RenderTexture instance.
        if (_highlightCam.targetTexture != target)
            _highlightCam.targetTexture = target;
        _highlightCam.depth = Camera.main.depth + 1f;
        return true;
    }

    // Runs on its own freshly-created GameObject, so Unity calls LateUpdate every real frame
    // with no Harmony tick involved - unlike patching the GAME's own classes (never observed
    // firing reliably in this project), components on objects WE create behave like any normal
    // Unity script.
    private class EspChamsCamSync : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera main = Camera.main;
            Camera self = GetComponent<Camera>();
            if (main == null || self == null) return;

            self.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            self.fieldOfView = main.fieldOfView;
            self.nearClipPlane = main.nearClipPlane;
            self.farClipPlane = main.farClipPlane;
            self.orthographic = main.orthographic;
            if (main.orthographic) self.orthographicSize = main.orthographicSize;
        }
    }
}
