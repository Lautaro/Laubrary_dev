using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// One track of a playing Zound, reached through its token (T-0497): every Zound has tracks -- a Klip is its own track 0,
    /// a Zequence's tracks are 0..n-1 in authored order -- so generic game code can say "track 0" to anything. A track can
    /// also be reached by the optional ZPOC id set on it in the Zequence editor, which is found anywhere in the Zound's tree
    /// and reaches every track that has it.
    ///
    /// Everything set here is a SETTING of the token: it lasts across the token's runs until changed or cleared, and never
    /// touches the saved Zound. A track that does not exist gives a handle whose every call does nothing (reported once).
    /// </summary>
    public readonly struct ZoundTrack {

        readonly ZoundToken token;
        readonly CompositeZound.ZoundEntry[] entries;   // null: the token's own track (a Klip's track 0)
        readonly bool self;

        internal ZoundTrack(ZoundToken token, CompositeZound.ZoundEntry[] entries, bool self) {
            this.token = token; this.entries = entries; this.self = self;
        }

        /// <summary>Whether this handle reaches anything.</summary>
        public bool isValid => token != null && (self || (entries != null && entries.Length > 0));

        IEnumerable<ZoundToken.TrackSettings> All() {
            if (!isValid) yield break;
            if (self) yield return token.TrackSettingsFor(null, create: true);
            else foreach (var e in entries) yield return token.TrackSettingsFor(e, create: true);
        }

        ZoundToken.TrackSettings First => !isValid ? null : token.TrackSettingsFor(self ? null : entries[0], create: false);

        /// <summary>The track's volume multiplier (1 = as authored). Setting it cancels a fade under way.</summary>
        public float volume {
            get => First?.volume ?? 1f;
            set { foreach (var t in All()) { t.volume = Mathf.Max(0f, value); t.fadeSeconds = 0f; } token?.RefreshTracks(); }
        }

        /// <summary>The track's pitch multiplier (1 = as authored).</summary>
        public float pitch {
            get => First?.pitch ?? 1f;
            set { foreach (var t in All()) t.pitch = Mathf.Max(0.01f, value); token?.RefreshTracks(); }
        }

        /// <summary>The track's speed multiplier, without changing pitch (1 = as authored; heard on a Klip with live speed on).</summary>
        public float speed {
            get => First?.speed ?? 1f;
            set { foreach (var t in All()) t.speed = Mathf.Max(0.01f, value); token?.RefreshTracks(); }
        }

        /// <summary>Glides the track's volume to <paramref name="target"/> over <paramref name="seconds"/>, from where it is now.</summary>
        public void FadeTo(float target, float seconds) {
            float now = Time.realtimeSinceStartup;
            foreach (var t in All()) {
                float from = t.Gain(now);
                t.volume = Mathf.Max(0f, target);
                t.fadeFrom = from; t.fadeStart = now; t.fadeSeconds = Mathf.Max(0f, seconds);
            }
            token?.RefreshTracks();
        }

        /// <summary>Silences the track (it is still chosen by a random or round-robin Zequence; see <see cref="enabled"/>).</summary>
        public bool mute {
            get => First?.mute ?? false;
            set { foreach (var t in All()) t.mute = value; token?.RefreshTracks(); }
        }

        /// <summary>Plays only the soloed tracks of their Zequence, in this play only.</summary>
        public bool solo {
            get => First?.solo ?? false;
            set { foreach (var t in All()) t.solo = value; token?.RefreshTracks(); }
        }

        /// <summary>
        /// False: the track is silent AND never chosen -- a random, round-robin or playlist Zequence skips it, so it never
        /// picks a track only to play nothing. If it is the track sounding now it fades out quickly; the Zequence does not
        /// pick another one until its next run.
        /// </summary>
        public bool enabled {
            get => First?.enabled ?? true;
            set {
                float now = Time.realtimeSinceStartup;
                foreach (var t in All()) {
                    if (t.enabled == value) continue;
                    t.enabled = value;
                    t.enableFadeFrom = t.EnableGain(now); t.enableFadeStart = now;
                }
                token?.RefreshTracks();
            }
        }

        /// <summary>Fades the track out and leaves it silent for this token (enabled is left as it is).</summary>
        public void Stop(float fadeSeconds = 0.03f) => FadeTo(0f, fadeSeconds);
    }
}
