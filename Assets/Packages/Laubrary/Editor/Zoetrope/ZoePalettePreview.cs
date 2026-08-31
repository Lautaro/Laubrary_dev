using UnityEditor;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Play/preview button backing (ZOE_PALETTE_BUILD_PLAN.md task 7: "a play/preview button on each row so a
    /// state can be previewed without the game running"). Spawns a throwaway character via the SAME
    /// <see cref="ZoeSpawner.SpawnCharacter"/> path Mirage's own edit-mode preview already uses —
    /// <c>MirageSubject.Update</c> calls it directly from an <c>[ExecuteAlways]</c> component, which is the
    /// existing proof this works outside Play mode at all — then fires the requested reaction through the REAL
    /// <see cref="ReactionFxPlayer"/>/<see cref="Health"/> path, never a shortcut that only looks like the
    /// reaction. The throwaway is cleaned up a few seconds later.
    ///
    /// Deliberately NOT the existing "Preview in Mirage" flow (<c>ZoetropeWindows.PreviewInMirage</c>): that
    /// opens a whole window, requires the Mirage stage scene, and is built for a person to drive a character by
    /// hand across many directions. A one-click "does this state look right" preview from a single row needs
    /// neither — spawning directly into whatever scene is already open is the smaller, more honest tool for
    /// exactly this job.
    /// </summary>
    public static class ZoePalettePreview
    {
        const float LingerSeconds = 4f;

        static GameObject _root;
        static double _destroyAt;

        [InitializeOnLoadMethod]
        static void Install() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (_root == null) return;
            if (EditorApplication.timeSinceStartup < _destroyAt) return;
            Object.DestroyImmediate(_root);
            _root = null;
        }

        static GameObject Spawn(Zoe zoe)
        {
            if (zoe == null) return null;
            if (_root != null) Object.DestroyImmediate(_root);

            _root = new GameObject($"[Palette Preview] {zoe.name}") { hideFlags = HideFlags.DontSave };
            _root.transform.position = FocusPoint();
            var spawned = ZoeSpawner.SpawnCharacter(zoe, _root.transform.position, _root.transform);
            _destroyAt = EditorApplication.timeSinceStartup + LingerSeconds;
            Selection.activeGameObject = spawned != null ? spawned : _root;
            return spawned;
        }

        static Vector3 FocusPoint()
        {
            var sv = SceneView.lastActiveSceneView;
            return sv != null ? (Vector3)sv.pivot : Vector3.zero;
        }

        /// Preview a custom named event — the exact <see cref="ReactionFxPlayer.Raise(string)"/> call gameplay
        /// code would make, so a bad id shows the SAME "no such state" warning it would in the real game.
        public static void PreviewEvent(Zoe zoe, string id)
        {
            var go = Spawn(zoe);
            var player = go != null ? go.GetComponent<ReactionFxPlayer>() : null;
            if (player == null)
            {
                Debug.LogWarning($"[Zoe Palette Preview] '{zoe?.name}' has no spawnable/animated view — nothing to preview.");
                return;
            }
            Try(() => player.Raise(id));
        }

        /// Preview the built-in Hit reaction — a small, non-lethal <see cref="Health.Damage"/> through the REAL
        /// Health → Damaged → ReactionFxPlayer.OnHit path, never a synthetic shortcut around it.
        public static void PreviewHit(Zoe zoe)
        {
            var go = Spawn(zoe);
            var health = go != null ? go.GetComponent<Health>() : null;
            if (health == null) { Debug.LogWarning("[Zoe Palette Preview] Spawned character has no Health."); return; }
            Try(() => health.Damage(Mathf.Max(1f, health.Max * 0.1f)));
        }

        /// Preview the built-in Death reaction — a real, lethal <see cref="Health.Kill"/> through the same path.
        public static void PreviewDeath(Zoe zoe)
        {
            var go = Spawn(zoe);
            var health = go != null ? go.GetComponent<Health>() : null;
            if (health == null) { Debug.LogWarning("[Zoe Palette Preview] Spawned character has no Health."); return; }
            Try(() => health.Kill());
        }

        /// Some effect content can still be genuinely Play-mode-only in ways this tool cannot know about ahead
        /// of time (a third-party IEffect, a future pooled resource). Rather than let a raw exception replace
        /// the whole preview click with a stack trace, report it as what it is — this specific reaction couldn't
        /// preview outside Play mode — and leave the rest of the editor exactly as it was.
        static void Try(System.Action action)
        {
            try { action(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Zoe Palette Preview] This reaction couldn't fully preview outside Play " +
                    $"mode ({e.GetType().Name}: {e.Message}). The declared row itself is unaffected — try Play " +
                    "mode for a reaction that depends on this.");
            }
        }
    }
}
