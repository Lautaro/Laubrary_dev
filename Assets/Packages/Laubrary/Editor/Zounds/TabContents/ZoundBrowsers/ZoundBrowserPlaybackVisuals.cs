using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    // ── Playback state colors ─────────────────────────────────────────────────
    internal static class ZoundsEditorColors {
        internal static Color flashColorStart          = new Color(0.5f, 0.5f, 0.8f, 1f);
        internal static Color flashColorEnd            = new Color(0.7f, 0.7f, 0.9f, 1f);
        internal static Color flashColorStartSelected  = new Color(0.7f, 0.7f, 0.9f, 1f);
        internal static Color flashColorEndSelected    = new Color(0.9f, 0.9f, 1f, 1f);
        internal static Color flashColorStartMuted     = new Color(0.8f, 0.5f, 0.5f, 1f);
        internal static Color flashColorEndMuted       = new Color(0.9f, 0.7f, 0.7f, 1f);
        internal static Color clipFlashColorStartSelected = new Color(0f, 0.7f, 0.9f, 1f);
        internal static Color clipFlashColorEndSelected   = new Color(0f, 0.9f, 1f, 1f);
        internal static Color clipFlashColorStart      = new Color(0f, 0.5f, 0.8f, 1f);
        internal static Color clipFlashColorEnd        = new Color(0f, 0.7f, 0.9f, 1f);
    }

    // ── Playback visual helpers ───────────────────────────────────────────────

    /// <summary>
    /// Stateless helpers for drawing playback state visuals in the Zound Browser.
    /// Used by ZoundListItemView and ZoundGridItemView.
    /// </summary>
    internal static class ZoundBrowserPlaybackVisuals {

        // Returns a stable pulse key for a zound button.
        // Uses object identity (not zound.id, which is 0 for all ClipZounds and therefore not unique).
        internal static string ZoundPulseKey(Zound zound)
            => "zound:btn:" + RuntimeHelpers.GetHashCode(zound);

        // Starts or stops the ZUI pulse for a zound button based on current token state.
        // Call once per repaint before drawing the button. DrawPulse is called after.
        internal static void UpdateZoundButtonPulse(Zound zound, bool isClipZound, bool hasToken, ZoundToken token) {
            string key = ZoundPulseKey(zound);
            if (hasToken && token.state != ZoundToken.State.Paused && token.audioSource.volume >= Mathf.Epsilon) {
                if (!ZUI.IsPulsing(key)) {
                    var baseColor = isClipZound
                        ? ZoundsEditorColors.clipFlashColorEnd
                        : token.audioSource.mute
                            ? ZoundsEditorColors.flashColorEndMuted
                            : ZoundsEditorColors.flashColorEnd;
                    var fillColor   = new Color(baseColor.r, baseColor.g, baseColor.b, 0.25f);
                    var borderColor = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);
                    ZUI.StartPulse(key, new ZUI.PulseParams {
                        color         = fillColor,
                        borderColor   = borderColor,
                        mode          = ZUI.PulseMode.FillAndBorder,
                        blend         = ZUI.PulseBlend.Alpha,
                        cycleDuration = 0.5f,
                        totalDuration = float.PositiveInfinity,
                        borderWidth   = 3f,
                    });
                }
            }
            else {
                ZUI.StopPulse(key);
            }
        }

        // Pass 1 — call BEFORE drawing controls: fills the background tint.
        internal static void DrawMuteSoloBackground(Rect rowRect, Zound currentZound) {
            if (!currentZound.mute && !currentZound.solo) return;
            var guiColor   = GUI.color;
            var stateColor = currentZound.mute
                ? ZUI.PaletteColor("Warning", new Color(0.8f, 0.2f, 0.2f, 1f))
                : ZUI.PaletteColor("Confirm", new Color(0f,   0.7f, 0.2f, 1f));
            GUI.color = stateColor;
            GUI.DrawTexture(rowRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
        }

        // Pass 2 — call AFTER drawing controls: draws the top border stripe and Zequence bottom stripe.
        internal static void DrawMuteSoloIndicator(Rect rowRect, Zound currentZound) {
            var guiColor = GUI.color;

            if (currentZound.mute || currentZound.solo) {
                var stateColor = currentZound.mute
                    ? ZUI.PaletteColor("Warning", new Color(0.8f, 0.2f, 0.2f, 1f))
                    : ZUI.PaletteColor("Confirm", new Color(0f,   0.7f, 0.2f, 1f));
                GUI.color = stateColor;
                GUI.DrawTexture(new Rect(rowRect.x + 1f, rowRect.y, rowRect.width - 2f, 2f), EditorGUIUtility.whiteTexture);
            }

            if (currentZound is Zequence zeq && zeq.HasLocalMuteOrSoloEntry()) {
                GUI.color = new Color(1f, 1f, 0f, 1f);
                GUI.DrawTexture(new Rect(rowRect.x + 1f, rowRect.yMax - 1.5f, rowRect.width - 2f, 1.5f), EditorGUIUtility.whiteTexture);
            }

            GUI.color = guiColor;
        }

        /// <summary>
        /// Finds the best ZoundToken to represent the current play state of a zound.
        /// Prefers a token where isDelayFinished == true (the audio has actually started).
        /// Falls back to the first found token (still in its pre-delay phase) so a highlight
        /// can be shown for queued/delayed zounds too.
        /// Returns true if any fully-started token exists; token is always set if any exists.
        /// </summary>
        internal static bool TryGetAnyInstanceToken(Zound currentZound, out ZoundToken token) {
            token = null;
            bool hasAnyInstancePlaying = false;
            ZoundToken firstFoundToken = null;
            if (ZoundEngine.CullingGroups.TryGetValue(currentZound, out var cullingGroup)) {
                foreach (var t in cullingGroup) {
                    if (firstFoundToken == null) firstFoundToken = t;
                    if (t.isDelayFinished) {
                        token = t;
                        hasAnyInstancePlaying = true;
                        break;
                    }
                }
            }
            if (!hasAnyInstancePlaying) token = firstFoundToken;
            return hasAnyInstancePlaying;
        }

        internal static void CopyToClipboard(string zoundName) {
            GUIUtility.systemCopyBuffer = zoundName;
        }
    }
}
