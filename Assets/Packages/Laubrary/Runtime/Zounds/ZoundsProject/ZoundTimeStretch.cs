using UnityEngine;

namespace Laubrary.Zounds {

    public enum TimeStretchAlgorithm { Granular = 0, Wsola = 1, PhaseVocoder = 2, Psola = 3 }
    public enum TimeStretchMode { Uniform = 0, Region = 1, Envelope = 2 }
    /// <summary>The live stretcher's algorithm (T-0409). Numbered as <see cref="Dsp.SapStretch"/> expects; append only.</summary>
    public enum LiveStretchAlgorithm { Wsola = 0, Granular = 1 }

    /// <summary>
    /// Source-material property of a Klip: change how long the audio lasts without changing its pitch.
    /// Applied offline to the cached PCM (a runtime cache entry, never a rendered asset), so the source
    /// stage reads the stretched buffer like any other. Beside trim, ahead of the chain.
    /// </summary>
    [System.Serializable]
    public class ZoundTimeStretch {
        public bool enabled;
        public TimeStretchAlgorithm algorithm = TimeStretchAlgorithm.Granular;
        public TimeStretchMode mode = TimeStretchMode.Uniform;
        /// <summary>Duration multiplier (2 = twice as long) for Uniform and Region.</summary>
        public float factor = 1f;
        /// <summary>Envelope mode: playback speed over the clip (1 = unchanged, 0.5 = half speed = twice as long).</summary>
        public Envelope speedEnvelope = new Envelope(0.25f, 4f);
        /// <summary>Region mode: the span (seconds into the clip) that is stretched; the rest plays unchanged.</summary>
        public float regionStart, regionEnd;
        /// <summary>Positional parameters per the algorithm descriptor.</summary>
        public float[] algorithmParams = new float[0];

        // ── live speed (T-0409): stretching while the sound plays, independent of everything above ──

        /// <summary>Runs the live stretcher for this sound, so its speed can change while it plays (a modifier, game code,
        /// an edit) without changing its pitch. Off: the sound reads its source directly, exactly as before.</summary>
        public bool liveEnabled;
        /// <summary>Authored speed: 1 unchanged, 0.5 half speed (twice as long), 2 double. Game code multiplies on top.</summary>
        public float liveSpeed = 1f;
        public LiveStretchAlgorithm liveAlgorithm = LiveStretchAlgorithm.Wsola;
        /// <summary>Window length in ms: 20–30 suits speech and hits, 40–50 pads and chords.</summary>
        public float liveWindowMs = 30f;
        /// <summary>Lay each detected hit down once, whole, at speed 1, so hits stay sharp and never double.</summary>
        public bool liveKeepHits = true;

        /// <summary>The live stretcher's setup for a play of this sound, or off.</summary>
        public Dsp.SapStretchConfig LiveConfig() => !liveEnabled ? Dsp.SapStretchConfig.Off : new Dsp.SapStretchConfig {
            enabled = true, algorithm = (int)liveAlgorithm, windowMs = Mathf.Clamp(liveWindowMs, 10f, 100f),
            keepHits = liveKeepHits, keepMs = 40f,
        };

        public ZoundTimeStretch DeepCopy() {
            return new ZoundTimeStretch {
                liveEnabled = liveEnabled, liveSpeed = liveSpeed, liveAlgorithm = liveAlgorithm,
                liveWindowMs = liveWindowMs, liveKeepHits = liveKeepHits,
                enabled = enabled, algorithm = algorithm, mode = mode, factor = factor,
                speedEnvelope = speedEnvelope != null ? speedEnvelope.DeepCopy() : new Envelope(0.25f, 4f),
                regionStart = regionStart, regionEnd = regionEnd,
                algorithmParams = algorithmParams != null ? (float[])algorithmParams.Clone() : new float[0]
            };
        }

        public float Param(int index) {
            if (algorithmParams != null && index < algorithmParams.Length) return algorithmParams[index];
            return Dsp.TimeStretchDescriptors.Get(algorithm).parameters[index].def;
        }

        public void EnsureParams() {
            var desc = Dsp.TimeStretchDescriptors.Get(algorithm);
            if (algorithmParams != null && algorithmParams.Length >= desc.parameters.Length) return;
            var np = new float[desc.parameters.Length];
            for (int i = 0; i < np.Length; i++) np[i] = (algorithmParams != null && i < algorithmParams.Length) ? algorithmParams[i] : desc.parameters[i].def;
            algorithmParams = np;
        }

        /// <summary>Whether the settings change the audio at all (a 1× uniform stretch is a no-op).</summary>
        public bool IsEffective(float clipLength) {
            if (!enabled) return false;
            switch (mode) {
                case TimeStretchMode.Uniform: return !Mathf.Approximately(factor, 1f);
                case TimeStretchMode.Region: return !Mathf.Approximately(factor, 1f) && regionEnd > regionStart + 0.001f;
                default: return speedEnvelope != null && speedEnvelope.Count > 0;
            }
        }

        /// <summary>A stable hash of everything that affects the rendered buffer (cache key).</summary>
        public int SettingsHash(float trimStart, float trimEnd) {
            unchecked {
                int h = 17;
                h = h * 31 + (int)algorithm; h = h * 31 + (int)mode;
                h = h * 31 + factor.GetHashCode();
                h = h * 31 + regionStart.GetHashCode(); h = h * 31 + regionEnd.GetHashCode();
                h = h * 31 + trimStart.GetHashCode(); h = h * 31 + trimEnd.GetHashCode();
                if (algorithmParams != null) for (int i = 0; i < algorithmParams.Length; i++) h = h * 31 + algorithmParams[i].GetHashCode();
                if (mode == TimeStretchMode.Envelope && speedEnvelope != null) {
                    var pts = speedEnvelope.GetPointsList();
                    for (int i = 0; i < pts.Count; i++) { h = h * 31 + pts[i].time.GetHashCode(); h = h * 31 + pts[i].value.GetHashCode(); h = h * 31 + pts[i].exponent.GetHashCode(); }
                }
                return h;
            }
        }
    }

}
