using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    [System.Serializable]
    public class AudioSpectrumView {

        public System.Action<bool> onTrimEnabledChanged;
        public System.Action<float> onTrimStartChanged;
        public System.Action<float> onTrimEndChanged;
        public System.Action<bool> onClampToTrimChanged;
        public System.Action<Envelope> onVolumeEnvelopeChanged;
        public System.Action<Envelope> onPitchEnvelopeChanged;
        public System.Action<bool> onVolumeEnabledChanged;
        public System.Action<bool> onPitchEnabledChanged;

        // Fired on MouseDown before any mutation — caller should call Undo.RecordObject here.
        public System.Action onTrimDragStarted;
        public System.Action onVolumeDragStarted;
        public System.Action onPitchDragStarted;
        // The time curve (T-0482) and keep length on the pitch curve.
        public System.Action<Envelope> onTimeEnvelopeChanged;
        public System.Action<bool> onTimeEnabledChanged;
        public System.Action onTimeDragStarted;
        public System.Action<bool> onKeepLengthChanged;

        [SerializeField] private float m_height = 100f;

        public float height {
            get => m_height;
            set {
                if (Mathf.Approximately(m_height, value)) return;
                m_height = value;
                EditorUtility.SetDirty(m_window);
            }
        }

        [SerializeField] private EditorWindow m_window;
        [SerializeField] private AudioClip m_clip;
        [SerializeField] private AudioClip m_renderedClip;
        [SerializeField] private AudioSource m_audioSource;
        private AudioClip m_sourceClip;
        private AudioClip m_ownedExternalClip;
        private object m_sourceReference;
        private string m_externalSourcePath;
        private System.DateTime m_externalSourceWriteTimeUtc;

        public AudioClip sourceClip => m_sourceClip;

        public bool NeedsSourceRefresh(Klip klip) {
            if (klip == null || !ReferenceEquals(m_sourceReference, klip.audioClipRef) || m_externalSourcePath != klip.externalSourcePath) return true;
            if (!string.IsNullOrEmpty(m_externalSourcePath)) return File.GetLastWriteTimeUtc(m_externalSourcePath) != m_externalSourceWriteTimeUtc;
            return m_sourceClip == null && klip.audioClipRef != null && klip.audioClipRef.RuntimeKeyIsValid();
        }

        // When set, DrawWaveformSpectrum shows this instead of the source clip.
        public AudioClip renderedClip {
            get => m_renderedClip;
            set => m_renderedClip = value;
        }

        [SerializeField] private bool m_trimEnabled = true;
        [SerializeField] private bool m_showVolumeEnvelopeHandles = true;
        [SerializeField] private bool m_showPitchEnvelopeHandles = true;
        [SerializeField] private bool m_showTimeEnvelopeHandles = true;
        /// <summary>The Klip's time curve (an Envelope on Speed, T-0482), or the shared disabled placeholder. Not saved:
        /// re-read by every InitFromKlip.</summary>
        [System.NonSerialized] private Envelope m_timeEnvelope;

        [SerializeField] private float m_trimStart;
        [SerializeField] private float m_trimEnd;
        [SerializeField] private bool m_clampToTrim = true;
        [SerializeField] private Envelope m_volumeEnvelope;
        [SerializeField] private Envelope m_pitchEnvelope;
        /// <summary>The Klip whose chain curves this view shows (null for a view of legacy curves): what the pitch heard at
        /// a point is read from (T-0479). Not saved: re-set by every InitFromKlip.</summary>
        [System.NonSerialized] private Klip m_klip;

        private AudioClip originalClip;
        private bool isTrimStartDragged = false;
        private bool isTrimEndDragged = false;
        private bool isTrimBothDragged = false;
        private float dragTrimDistance = 0f;
        private float dragMouseOffset = 0f;
        private float zoomFactor = 1f;
        private float viewStart;
        private float viewEnd;
        private double lastViewportChangeTime;
        private Texture2D zoomTexture;
        private int zoomTextureClipId;
        private int zoomTextureWidth;
        private int zoomTextureHeight;
        private float zoomTextureStart;
        private float zoomTextureEnd;
        private Color zoomTextureColor;
        private bool zoomTextureHighQuality;
        private bool zoomTextureAttempted;
        private RangeWaveformJob rangeJob;
        private bool zoomBuildRequested;

        private sealed class RangeWaveformJob {
            private const int ChunkFrames = 16384;
            public readonly AudioClip clip;
            public readonly int width;
            public readonly int height;
            public readonly float start;
            public readonly float end;
            public readonly Color color;
            public readonly bool highQuality;
            public readonly float[] peaks;
            public bool failed;
            private readonly int firstFrame;
            private readonly int frameCount;
            private readonly int channels;
            private readonly float[] buffer;
            private int nextFrame;
            private int lastPixel = -1;

            public RangeWaveformJob(AudioClip clip, int width, int height, float start, float end, Color color, bool highQuality) {
                this.clip = clip;
                this.width = width;
                this.height = height;
                this.start = start;
                this.end = end;
                this.color = color;
                this.highQuality = highQuality;
                firstFrame = Mathf.Clamp(Mathf.FloorToInt(start * clip.frequency), 0, clip.samples - 1);
                int lastFrame = Mathf.Clamp(Mathf.CeilToInt(end * clip.frequency), firstFrame + 1, clip.samples);
                frameCount = lastFrame - firstFrame;
                channels = clip.channels;
                buffer = new float[Mathf.Min(ChunkFrames, frameCount) * channels];
                peaks = new float[width];
            }

            public bool Matches(AudioClip otherClip, int otherWidth, int otherHeight, float otherStart, float otherEnd, Color otherColor, bool otherHighQuality) {
                return clip == otherClip && width == otherWidth && height == otherHeight &&
                    Mathf.Approximately(start, otherStart) && Mathf.Approximately(end, otherEnd) &&
                    color == otherColor && highQuality == otherHighQuality;
            }

            public bool ProcessChunk() {
                int frames = Mathf.Min(ChunkFrames, frameCount - nextFrame);
                float[] data = frames * channels == buffer.Length ? buffer : new float[frames * channels];
                if (!clip.GetData(data, firstFrame + nextFrame)) { failed = true; return true; }
                for (int frame = 0; frame < frames; frame++) {
                    int pixel = (int)((long)(nextFrame + frame) * width / frameCount);
                    if (highQuality) {
                        for (int channel = 0; channel < channels; channel++) {
                            peaks[pixel] = Mathf.Max(peaks[pixel], Mathf.Abs(data[frame * channels + channel]));
                        }
                    }
                    else if (pixel != lastPixel) {
                        peaks[pixel] = Mathf.Abs(data[frame * channels]);
                        lastPixel = pixel;
                    }
                }
                nextFrame += frames;
                return nextFrame >= frameCount;
            }
        }
        // Per-envelope ZUI runtime state (domain, callbacks, interaction flags).
        // Stable stateKeys keep each envelope's drag/selection state isolated
        // inside ZUI.Envelope's state dictionary.
        private ZUIEnvelopeRuntime volumeRuntime;
        private ZUIEnvelopeRuntime pitchRuntime;
        private ZUIEnvelopeRuntime timeRuntime;
        private int timeStateKey;

        /// <summary>The time curve's colour: its own, distinct from volume (green) and pitch (red).</summary>
        internal static readonly Color TimeCurveColor = new Color(0.30f, 0.85f, 1f, 1f);
        /// <summary>The Gain curve's colour (violet: not the volume green, the pitch red, the time cyan, or the amber game code wears).</summary>
        internal static readonly Color GainCurveColor = new Color(0.78f, 0.55f, 1f, 1f);
        private int volumeStateKey;
        private int pitchStateKey;

        // Envelopes overlay the waveform — no background or border. Curve is
        // thickened for visibility over the spectrum and the handles take the
        // curve color so a muted volume/pitch envelope still reads correctly.
        // Rebuilt per-call (fields only, no GUIStyle baking) so curve color
        // and thickness can follow the live editorStyle settings.
        // TODO: replace with a named ZUIEnvelopeDef on the sheet once the
        // ZUI Style Editor gains an Envelope tab (see memory project_zui_envelope_editor_later).
        // Mark the first + last points as Y-only so the envelope always spans
        // the full time range, while the endpoints remain draggable vertically.
        // Matches legacy EnvelopeGUI behavior where first.time == xMin and
        // last.time == xMax were enforced on drag. Applied each frame so the
        // editState stays consistent even if a point was inserted or removed.
        internal static void PinEndpointsYOnly(List<ZUIEnvelopePoint> points) {
            if (points == null || points.Count == 0) return;
            points[0].editState = ZUIEnvelopeEditState.YEditable;
            for (int i = 1; i < points.Count - 1; i++) {
                points[i].editState = ZUIEnvelopeEditState.Editable;
            }
            if (points.Count > 1) {
                points[points.Count - 1].editState = ZUIEnvelopeEditState.YEditable;
            }
        }

        internal static ZUIEnvelopeDef BuildOverlayDef(Color curveColor, float curveThickness) {
            var handleColor = new ZUIColorRef(curveColor);
            var hoverColor  = new ZUIColorRef(new Color(
                Mathf.Clamp01(curveColor.r + 0.2f),
                Mathf.Clamp01(curveColor.g + 0.2f),
                Mathf.Clamp01(curveColor.b + 0.2f),
                1f));
            var handle = new ZUIEnvelopeHandleDef {
                radius          = 3f,
                hoverRadius     = 5f,
                fillColor       = handleColor,
                hoverFillColor  = hoverColor,
            };
            return new ZUIEnvelopeDef {
                name       = "__ZoundsOverlay",
                background = new ZUIColor(new Color(0f, 0f, 0f, 0f)),
                border     = new ZUIBorderDef(new Color(0f, 0f, 0f, 0f), 0f),
                paddingTop = 0f, paddingRight = 0f, paddingBottom = 0f, paddingLeft = 0f,
                curveThickness      = curveThickness,
                curveHoverThickness = curveThickness + 1.5f,
                editable    = handle,
                xEditable   = handle,
                yEditable   = handle,
                notEditable = handle,
            };
        }

        private static Texture m_editIcon;
        /// <summary>Icon for the envelope edit-handles toggle.</summary>
        public static Texture editIcon {
            get {
                if (m_editIcon == null) {
                    m_editIcon = EditorGUIUtility.IconContent("d_editicon.sml").image;
                }
                return m_editIcon;
            }
        }

        public AudioSource audioSource => m_audioSource;

        public AudioSpectrumView(EditorWindow window) {
            m_window = window;
            EditorApplication.update += ProcessRangeWaveform;
            var audioSourceGO = new GameObject("AudioSpectrumPreviewer");
            audioSourceGO.hideFlags = HideFlags.HideAndDontSave;
            m_audioSource = audioSourceGO.AddComponent<AudioSource>();
            m_audioSource.playOnAwake = false;
            m_audioSource.loop = false;
            // First/last points are pinned in X (so the envelope always spans
            // the full time range) but can still move in Y. Per-point
            // editState is applied below in DrawLayout before the ZUI call.
            volumeRuntime = new ZUIEnvelopeRuntime {
                onDragStarted = () => onVolumeDragStarted?.Invoke(),
                onDragUpdated = () => onVolumeEnvelopeChanged?.Invoke(m_volumeEnvelope),
                onMutated     = () => onVolumeEnvelopeChanged?.Invoke(m_volumeEnvelope),
            };
            pitchRuntime = new ZUIEnvelopeRuntime {
                onDragStarted = () => onPitchDragStarted?.Invoke(),
                onDragUpdated = () => onPitchEnvelopeChanged?.Invoke(m_pitchEnvelope),
                onMutated     = () => onPitchEnvelopeChanged?.Invoke(m_pitchEnvelope),
            };
            // Unique state keys per view instance — two envelopes share the
            // view's hash, disambiguated by a +1 salt.
            volumeStateKey = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
            pitchStateKey  = volumeStateKey + 1;
            timeRuntime = new ZUIEnvelopeRuntime {
                onDragStarted = () => onTimeDragStarted?.Invoke(),
                onDragUpdated = () => onTimeEnvelopeChanged?.Invoke(m_timeEnvelope),
                onMutated     = () => onTimeEnvelopeChanged?.Invoke(m_timeEnvelope),
            };
            timeStateKey = volumeStateKey + 2;
        }

        public void Destroy() {
            EditorApplication.update -= ProcessRangeWaveform;
            rangeJob = null;
            zoomBuildRequested = false;
            ClearZoomTexture();
            if (m_audioSource != null) {
                m_audioSource.Stop();
                m_audioSource.clip = null;
            }
            if (m_ownedExternalClip != null) {
                Object.DestroyImmediate(m_ownedExternalClip);
                m_ownedExternalClip = null;
            }
            if (m_audioSource != null) {
                if (Application.isPlaying) {
                    GameObject.Destroy(m_audioSource.gameObject);
                }
                else {
                    GameObject.DestroyImmediate(m_audioSource.gameObject);
                }
                m_audioSource = null;
            }
            m_clip = null;
            m_sourceClip = null;
        }

        public float trimStart {
            get => m_trimStart;
            private set {
                m_trimStart = value;
                onTrimStartChanged?.Invoke(m_trimStart);
            }
        }

        public float trimEnd {
            get => m_trimEnd;
            private set {
                m_trimEnd = value;
                onTrimEndChanged?.Invoke(m_trimEnd);
            }
        }

        /// <param name="useChainEnvelopes">
        /// True for a Klip that lives in the project (the normal Klip editor case): the overlay reads and
        /// writes the chain modulators via <see cref="KlipChainEnvelopes"/>, which is what real-time
        /// playback actually consults. False for a throwaway Klip that isn't part of any chain yet (the
        /// import-and-trim popup's temporary Klip) — the overlay falls back to the Klip's own legacy
        /// <c>volumeEnvelope</c>/<c>pitchEnvelope</c> fields, which is what that caller reads back afterward.
        /// </param>
        public void InitFromKlip(Klip klip, bool useChainEnvelopes) {
            m_sourceReference = klip.audioClipRef;
            m_externalSourcePath = klip.externalSourcePath;
            m_externalSourceWriteTimeUtc = string.IsNullOrEmpty(m_externalSourcePath) ? default : File.GetLastWriteTimeUtc(m_externalSourcePath);
            AudioClip newClip = null;
            AudioClip newExternalClip = null;
            AudioClip resolvedSource = null;
            originalClip = null;
            m_sourceClip = null;
            try {
                // Load source clip — external or internal.
                if (!string.IsNullOrEmpty(klip.externalSourcePath)) {
                    newExternalClip = WavDecoder.LoadFromDisk(klip.externalSourcePath);
                    originalClip = newExternalClip;
                }
                else {
                    try { originalClip = klip.audioClipRef.editorAsset as AudioClip; } catch { }
                }
                resolvedSource = originalClip;
                // Output clip for playback.
                try { newClip = klip.GetAudioClipReference().editorAsset as AudioClip; } catch { }
                // Fall back: source if no output, or output if no source (non-audio machine).
                if (newClip == null) newClip = originalClip;
                if (originalClip == null) originalClip = newClip;
            } catch { }
            m_sourceClip = resolvedSource;

            if (m_ownedExternalClip != null && m_ownedExternalClip != newExternalClip) {
                if (m_audioSource != null && m_audioSource.clip == m_ownedExternalClip) {
                    m_audioSource.Stop();
                    m_audioSource.clip = null;
                }
                Object.DestroyImmediate(m_ownedExternalClip);
            }
            m_ownedExternalClip = newExternalClip;

            if (newClip == null) {
                rangeJob = null;
                zoomBuildRequested = false;
                ClearZoomTexture();
                m_clip = null;
                m_audioSource.clip = null;
                return;
            }

            if (m_clip != newClip) {
                rangeJob = null;
                zoomBuildRequested = false;
                AudioWaveformUtility.ClearCache(newClip);
                zoomFactor = 1f;
                viewStart = 0f;
                ClearZoomTexture();
            }
            m_clip = newClip;
            m_audioSource.clip = m_clip;
            m_trimEnabled = klip.trimEnabled;
            m_trimStart = klip.trimStart;
            m_trimEnd = klip.trimEnd;
            m_clampToTrim = klip.clampToTrim;
            m_klip = useChainEnvelopes ? klip : null;
            if (useChainEnvelopes) {
                // KlipChainEnvelopes hands back the modulator's own curve object (or null when the Klip
                // has no volume/pitch modifier yet) — ZUI mutates it in place, same as the legacy fields
                // used to be mutated in place below. Falls back to a shared, permanently-disabled curve so
                // the toggle/overlay code below never has to null-check; it is never edited while disabled.
                m_volumeEnvelope = KlipChainEnvelopes.VolumeCurve(klip, create: false) ?? KlipChainEnvelopes.Disabled;
                m_pitchEnvelope = KlipChainEnvelopes.PitchCurve(klip, create: false) ?? KlipChainEnvelopes.Disabled;
                m_timeEnvelope = KlipChainEnvelopes.TimeCurve(klip, create: false) ?? KlipChainEnvelopes.Disabled;
            }
            else {
                m_volumeEnvelope = klip.volumeEnvelope;
                m_pitchEnvelope = klip.pitchEnvelope;
                m_timeEnvelope = KlipChainEnvelopes.Disabled;
            }
            ConstrainView();
        }

        private void ConstrainView() {
            float previousStart = viewStart;
            float previousEnd = viewEnd;
            float duration = originalClip == null ? 0f : originalClip.length;
            if (duration <= 0f) { viewStart = viewEnd = 0f; return; }
            if (!m_trimEnabled || m_trimEnd <= m_trimStart) zoomFactor = 1f;
            else {
                float minVisible = Mathf.Min(duration, Mathf.Max(m_trimEnd - m_trimStart, 1f / originalClip.frequency, duration * 0.000001f));
                zoomFactor = Mathf.Clamp(zoomFactor, 1f, duration / minVisible);
            }
            float visibleDuration = duration / zoomFactor;
            float minStart = Mathf.Max(0f, m_trimEnd - visibleDuration);
            float maxStart = Mathf.Min(m_trimStart, duration - visibleDuration);
            viewStart = Mathf.Clamp(viewStart, minStart, Mathf.Max(minStart, maxStart));
            viewEnd = viewStart + visibleDuration;
            if (!Mathf.Approximately(previousStart, viewStart) || !Mathf.Approximately(previousEnd, viewEnd)) {
                lastViewportChangeTime = EditorApplication.timeSinceStartup;
            }
        }

        private void ClearZoomTexture() {
            if (zoomTexture != null) Object.DestroyImmediate(zoomTexture);
            zoomTexture = null;
            zoomTextureAttempted = false;
        }

        /// <summary>The audio of the same clip changed on disk (destructive editing): the detailed picture is redrawn.</summary>
        internal void InvalidateWaveform() {
            ClearZoomTexture();
            rangeJob = null;
        }

        private void ProcessRangeWaveform() {
            double idle = EditorApplication.timeSinceStartup - lastViewportChangeTime;
            if (rangeJob == null) {
                if (zoomBuildRequested && idle >= 0.25) {
                    zoomBuildRequested = false;
                    if (m_window != null) m_window.Repaint();
                }
                return;
            }
            if (idle < 0.1) return;
            if (rangeJob.clip == null) { rangeJob = null; return; }
            try {
                // Spend only a few milliseconds per update, but use the whole budget on long clips.
                double deadline = EditorApplication.timeSinceStartup + 0.003;
                bool done;
                do {
                    done = rangeJob.ProcessChunk();
                } while (!done && EditorApplication.timeSinceStartup < deadline);
                if (!done) return;
            }
            catch {
                rangeJob.failed = true;
            }
            var completed = rangeJob;
            rangeJob = null;
            ClearZoomTexture();
            if (!completed.failed) zoomTexture = AudioWaveformUtility.CreatePeakTexture(completed.peaks,
                completed.width, completed.height, completed.color);
            zoomTextureClipId = completed.clip.GetInstanceID();
            zoomTextureWidth = completed.width;
            zoomTextureHeight = completed.height;
            zoomTextureStart = completed.start;
            zoomTextureEnd = completed.end;
            zoomTextureColor = completed.color;
            zoomTextureHighQuality = completed.highQuality;
            zoomTextureAttempted = true;
            if (m_window != null) m_window.Repaint();
        }

        private float TimeToX(float time, Rect rect) => rect.x + (time - viewStart) / (viewEnd - viewStart) * rect.width;
        private float XToTime(float x, Rect rect) => viewStart + (x - rect.x) / rect.width * (viewEnd - viewStart);

        private void HandleWheelZoom(Rect rect) {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel || !rect.Contains(e.mousePosition)) return;
            if (WheelZoom(e.mousePosition.x, rect, e.delta.y)) { e.Use(); m_window.Repaint(); }
        }

        // ── shared with the UI Toolkit twin (T-0468): state, geometry and edits, independent of how the view draws ──

        /// <summary>Zooms about the pointer within the trimmed range; false when zoom does not apply.</summary>
        internal bool WheelZoom(float mouseX, Rect rect, float deltaY) {
            if (!m_trimEnabled || originalClip == null) return false;
            float trimDuration = m_trimEnd - m_trimStart;
            if (trimDuration <= 0f || originalClip.length <= trimDuration) return false;
            float anchor = XToTime(mouseX, rect);
            float fraction = Mathf.Clamp01((mouseX - rect.x) / rect.width);
            zoomFactor = Mathf.Clamp(zoomFactor * Mathf.Pow(1.25f, -deltaY), 1f, originalClip.length / trimDuration);
            viewStart = anchor - fraction * originalClip.length / zoomFactor;
            ConstrainView();
            return true;
        }

        /// <summary>Shows just the trimmed part of the source (the owner's default on opening a Klip editor, 2026-10-08):
        /// zoomed so the trim fills the view. The wheel zooms back out to the whole recording. Nothing to do without a trim.</summary>
        internal void ShowTrim() {
            if (!m_trimEnabled || originalClip == null) return;
            float trimDuration = m_trimEnd - m_trimStart;
            if (trimDuration <= 0f || originalClip.length <= trimDuration) return;
            zoomFactor = originalClip.length / trimDuration;
            viewStart = m_trimStart;
            ConstrainView();
        }

        internal AudioClip OriginalClip => originalClip;
        internal bool TrimEnabled => m_trimEnabled;
        internal bool ClampToTrim => m_clampToTrim;
        internal Envelope VolumeEnvelope => m_volumeEnvelope;
        internal Envelope PitchEnvelope => m_pitchEnvelope;
        internal Envelope TimeEnvelope => m_timeEnvelope ?? KlipChainEnvelopes.Disabled;
        internal bool ShowTimeHandles => m_showTimeEnvelopeHandles;
        /// <summary>Whether the pitch curve keeps the play's length (T-0482); false for a view of legacy curves.</summary>
        internal bool KeepLength => m_klip != null && m_klip.timeStretch != null && m_klip.timeStretch.pitchKeepsLength;
        internal bool HasKlip => m_klip != null;
        internal void RequestTimeEnabled(bool v) { if (v != TimeEnvelope.enabled) onTimeEnabledChanged?.Invoke(v); }
        internal void RequestKeepLength(bool v) { if (v != KeepLength) onKeepLengthChanged?.Invoke(v); }
        internal void SetShowTimeHandles(bool v) {
            if (v == m_showTimeEnvelopeHandles) return;
            Undo.RecordObject(m_window, "toggle time curve editable");
            m_showTimeEnvelopeHandles = v;
            EditorUtility.SetDirty(m_window);
        }
        internal bool ShowVolumeHandles => m_showVolumeEnvelopeHandles;
        internal bool ShowPitchHandles => m_showPitchEnvelopeHandles;
        internal bool IsTrimDragging => isTrimStartDragged || isTrimEndDragged || isTrimBothDragged;

        /// <summary>The start of a frame: keep the view inside the clip and the trim, and note input that moves it.</summary>
        internal void BeginFrame(bool pointerOrKeyInput) {
            ConstrainView();
            if (zoomFactor > 1.5f && pointerOrKeyInput) lastViewportChangeTime = EditorApplication.timeSinceStartup;
        }

        internal void SetTrimEnabled(bool v) { if (v == m_trimEnabled) return; m_trimEnabled = v; onTrimEnabledChanged?.Invoke(v); }
        internal void SetClampToTrim(bool v) { if (v == m_clampToTrim) return; m_clampToTrim = v; onClampToTrimChanged?.Invoke(v); }
        /// <summary>The callback owns creating/enabling the real curve (see the toolbar note in DrawLayout).</summary>
        internal void RequestVolumeEnabled(bool v) { if (v != m_volumeEnvelope.enabled) onVolumeEnabledChanged?.Invoke(v); }
        internal void RequestPitchEnabled(bool v) { if (v != m_pitchEnvelope.enabled) onPitchEnabledChanged?.Invoke(v); }
        internal void SetShowVolumeHandles(bool v) {
            if (v == m_showVolumeEnvelopeHandles) return;
            Undo.RecordObject(m_window, "toggle volume envelope editable");
            m_showVolumeEnvelopeHandles = v;
            EditorUtility.SetDirty(m_window);
        }
        internal void SetShowPitchHandles(bool v) {
            if (v == m_showPitchEnvelopeHandles) return;
            Undo.RecordObject(m_window, "toggle pitch envelope editable");
            m_showPitchEnvelopeHandles = v;
            EditorUtility.SetDirty(m_window);
        }

        /// <summary>The length readout: the trimmed length when trimming, else the clip's.</summary>
        internal string LengthText => originalClip == null ? "" : $"{(m_trimEnabled ? (m_trimEnd - m_trimStart) : originalClip.length):F3}s";

        internal float TimeToXIn(float time, Rect rect) => TimeToX(time, rect);
        internal float XToTimeIn(float x, Rect rect) => XToTime(x, rect);

        /// <summary>The trim-start handle strip and the dimmed area before it, in <paramref name="spectrumRect"/>.</summary>
        internal Rect TrimStartHandle(Rect spectrumRect, out Rect dim) {
            float w = TimeToX(trimStart, spectrumRect) - spectrumRect.x;
            float th = ZoundsProject.Instance.projectSettings.editorStyle.trimHandleThickness;
            var area = spectrumRect;
            area.x = Mathf.Clamp(spectrumRect.x + w, spectrumRect.x, spectrumRect.xMax - th);
            area.width = th;
            dim = new Rect(spectrumRect.x, spectrumRect.y, w, spectrumRect.height);
            return area;
        }

        /// <summary>The trim-end handle strip and the dimmed area after it, in <paramref name="spectrumRect"/>.</summary>
        internal Rect TrimEndHandle(Rect spectrumRect, out Rect dim) {
            float w = TimeToX(trimEnd, spectrumRect) - spectrumRect.x;
            float th = ZoundsProject.Instance.projectSettings.editorStyle.trimHandleThickness;
            var area = spectrumRect;
            area.x = Mathf.Clamp(spectrumRect.x + w - th, spectrumRect.x, spectrumRect.xMax - th);
            area.width = th;
            dim = new Rect(spectrumRect.x + w, spectrumRect.y, spectrumRect.width - w, spectrumRect.height);
            return area;
        }

        /// <summary>Whether each trim handle is live (drawn and draggable) — the old DrawTrimHandles conditions, which also
        /// keep the end from sitting before the start.</summary>
        internal void TrimHandlesLive(out bool start, out bool end) {
            if (trimEnd < trimStart) trimEnd = trimStart;
            end = trimEnd >= originalClip.length ? trimStart < originalClip.length : true;
            start = trimStart == 0 ? trimEnd > 0 : true;
        }

        public enum TrimDrag { Start, End, Both }

        /// <summary>A trim drag begins: one handle, or (right button) both together keeping their distance.</summary>
        internal void BeginTrimDrag(TrimDrag which, float mouseTime) {
            onTrimDragStarted?.Invoke();
            isTrimStartDragged = which == TrimDrag.Start;
            isTrimEndDragged = which == TrimDrag.End;
            isTrimBothDragged = which == TrimDrag.Both;
            if (which == TrimDrag.Both) { dragTrimDistance = trimEnd - trimStart; dragMouseOffset = mouseTime - trimStart; }
        }

        /// <summary>Moves whichever trim drag is under way to <paramref name="mouseTime"/>; false when none is.</summary>
        internal bool DragTrim(float mouseTime) {
            float clipDuration = originalClip.length;
            if (isTrimStartDragged) {
                var v = mouseTime;
                if (v < 0) v = 0; else if (v >= trimEnd) v = trimEnd;
                trimStart = v;
                return true;
            }
            if (isTrimEndDragged) {
                var v = mouseTime;
                if (v < trimStart) v = trimStart; else if (v >= clipDuration) v = clipDuration;
                trimEnd = v;
                return true;
            }
            if (isTrimBothDragged) {
                var ns = mouseTime - dragMouseOffset;
                if (ns < 0) ns = 0; else if (ns + dragTrimDistance > clipDuration) ns = clipDuration - dragTrimDistance;
                trimStart = ns;
                trimEnd = ns + dragTrimDistance;
                return true;
            }
            return false;
        }

        internal void EndTrimDrag() { isTrimStartDragged = isTrimEndDragged = isTrimBothDragged = false; }

        /// <summary>
        /// Where each playhead sits across the trimmed area (0..1): the preview source while it plays, and every playing
        /// token of this sound — mapped back through the pitch envelope where playback was rendered with one.
        /// </summary>
        private static readonly double[] s_loopPositions = new double[2];

        private static readonly float[] s_headWeights = new float[8];
        private static readonly double[] s_headSeconds = new double[8];

        internal List<float> PlayheadFractions(IEnumerable<ZoundToken> playingTokens, out bool animating) => PlayheadFractions(playingTokens, out animating, null);

        /// <summary>As above; <paramref name="weights"/>, when given, gets how strongly each playhead should be drawn (1, or
        /// less for the copy fading out of a Looper's crossmix), in the same order.</summary>
        internal List<float> PlayheadFractions(IEnumerable<ZoundToken> playingTokens, out bool animating, List<float> weights) {
            var list = new List<float>();
            weights?.Clear();
            animating = false;
            if (m_audioSource != null && m_audioSource.clip != null && m_audioSource.isPlaying) {
                animating = true;
                if (m_pitchEnvelope.enabled) {
                    float totalTime = trimEnd - trimStart;
                    float step = totalTime / AudioRenderUtility.GetOptimalIntegrationSteps(totalTime);
                    float t = 0f, renderedTime = 0f;
                    // The pitch actually heard at each point (a chain curve's value is not a multiplier; T-0479).
                    var pitchChain = m_klip != null ? Dsp.ZoundDspPlayback.PlayChain(m_klip) : null;
                    while (t <= totalTime && renderedTime < m_audioSource.time) {
                        float heard = pitchChain != null ? Dsp.ZoundDspPlayback.PitchAtSource(pitchChain, t / totalTime, totalTime)
                                                         : m_pitchEnvelope.Evaluate(t / totalTime);
                        renderedTime += step / Mathf.Max(0.01f, heard);
                        t += step;
                    }
                    list.Add(t / totalTime); weights?.Add(1f);
                }
                else { list.Add(m_audioSource.time / m_audioSource.clip.length); weights?.Add(1f); }
            }
            if (playingTokens != null) {
                foreach (var token in playingTokens) {
                    if (token == null || token.state == ZoundToken.State.Killed) continue;
                    // Playing through its chain: the playheads are where the voice is actually reading its source (T-0493),
                    // not elapsed time over length, which drifts from the sound the moment pitch, speed or a time curve
                    // change how fast the source is gone through. One per read head: several while repeats overlap, two
                    // during a Looper's crossmix (the copy fading out drawn fainter).
                    if (token.zound is Klip lk && token.audioSource != null
                        && token.audioSource.generator is Dsp.ZoundSapVoiceGenerator lg) {
                        int n = lg.ReadSourcePositions(s_headSeconds, s_headWeights);
                        float from = lk.trimEnabled ? lk.trimStart : 0f;
                        float to = lk.trimEnabled && lk.trimEnd > lk.trimStart ? lk.trimEnd : (OriginalClip != null ? OriginalClip.length : 0f);
                        if (to > from) for (int i = 0; i < n; i++) {
                            list.Add(Mathf.Clamp01((float)((s_headSeconds[i] - from) / (to - from))));
                            weights?.Add(s_headWeights[i]);
                        }
                        animating = true;
                        continue;
                    }
                    // Use audioSource.time for accurate playhead — token.time is a manual counter
                    // that can drift from actual playback position.
                    float playTime = token.audioSource != null && token.audioSource.clip != null ? token.audioSource.time : token.time;
                    float clipLength = token.audioSource != null && token.audioSource.clip != null ? token.audioSource.clip.length : token.duration;
                    if (token.zound is Klip klip && klip.pitchEnvelope.enabled && !klip.needsRender && !token.isRealtime) {
                        // Pitch envelope changes duration: map rendered time back to source time
                        float totalTime = klip.trimEnd - klip.trimStart;
                        float step = totalTime / AudioRenderUtility.GetOptimalIntegrationSteps(totalTime);
                        float t = 0f, renderedTime = 0f;
                        while (t <= totalTime && renderedTime < playTime) {
                            renderedTime += step / Mathf.Max(0.01f, klip.pitchEnvelope.Evaluate(t / totalTime));
                            t += step;
                        }
                        list.Add(t / totalTime); weights?.Add(1f);
                    }
                    else { list.Add(clipLength > 0f ? playTime / clipLength : 0f); weights?.Add(1f); }
                    animating = true;
                }
            }
            return list;
        }

        /// <summary>Sets up an overlay envelope's runtime for this frame (visible window, range, editability, pinned
        /// endpoints) and returns its look; null when that envelope is off.</summary>
        /// <summary>The time curve toggle's tooltip (T-0482).</summary>
        internal const string TimeTip =
            "A curve over the waveform for how fast the sound moves through its source, without changing its pitch: the " +
            "middle is unchanged, the top four times as fast (a quarter of the length), the bottom a quarter as fast. Plays " +
            "through the live stretcher. Quality: fine within about half and double speed; beyond that, attacks smear and " +
            "dense material (chords, crowds) can sound phasey.";

        /// <summary>The keep-length toggle's tooltip, for its current state (T-0482).</summary>
        internal static string KeepLengthTip(bool on) => on
            ? "The pitch curve changes pitch only: the play keeps its length. Click to go back to tape-style, where raising the pitch also shortens the sound."
            : "Make the pitch curve change pitch without changing the play's length: the live stretcher slows the sound down by exactly as much as the curve raises it (and speeds it up as much as it lowers it). Quality: clean within about an octave either way; beyond that, and on chords and dense material, it can sound phasey or smeared. Off (tape-style) is how the sound has always played.";

        /// <summary>
        /// Random points' ellipses (T-0483), read-only, for the old IMGUI windows (the shared IMGUI envelope control is not
        /// ours to change; the radii are edited in the UI Toolkit windows by right-clicking a point).
        /// </summary>
        internal static void DrawRandomEllipses(Rect rect, List<ZUIEnvelopePoint> pts, float xMin, float xMax, float yMin, float yMax, Color c) {
            if (pts == null || Event.current.type != EventType.Repaint || xMax <= xMin || yMax <= yMin) return;
            var prev = Handles.color;
            Handles.color = new Color(c.r, c.g, c.b, 0.75f);
            var ring = new Vector3[41];
            foreach (var p in pts) {
                if (p.randomX <= 0f && p.randomY <= 0f) continue;
                float cx = rect.x + (p.time - xMin) / (xMax - xMin) * rect.width;
                float cy = rect.yMax - (p.value - yMin) / (yMax - yMin) * rect.height;
                float rx = Mathf.Max(p.randomX / (xMax - xMin) * rect.width, 1f), ry = Mathf.Max(p.randomY / (yMax - yMin) * rect.height, 1f);
                for (int k = 0; k <= 40; k++) { float a = k * Mathf.PI * 2f / 40f; ring[k] = new Vector3(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, 0f); }
                Handles.DrawAAPolyLine(1.5f, ring);
            }
            Handles.color = prev;
        }

        /// <summary>The old window's dotted "what the plays under way hear" line over an overlay curve (T-0484).</summary>
        private void DrawLiveDotted(Rect rect, Envelope authored, ZUIEnvelopeRuntime rt, Color c) {
            if (m_klip == null || authored == null || rt == null || Event.current.type != EventType.Repaint) return;
            int n = Uitk.LiveDrawnCurves.Fill(ref s_live, m_klip, authored, KlipChainEnvelopes.ModifierIndexOf(m_klip, authored));
            if (n == 0 || rt.xMax <= rt.xMin || rt.yMax <= rt.yMin) return;
            int steps = Mathf.Max(2, (int)(rect.width / 3f));
            var prev = Handles.color;
            Handles.color = Color.Lerp(c, Color.white, 0.45f);
            foreach (var f in s_live) {
                for (int i = 0; i + 1 <= steps; i += 2) {
                    float t0 = rt.xMin + (rt.xMax - rt.xMin) * i / steps, t1 = rt.xMin + (rt.xMax - rt.xMin) * (i + 1) / steps;
                    var a = new Vector3(rect.x + (float)i / steps * rect.width, rect.yMax - (f(t0) - rt.yMin) / (rt.yMax - rt.yMin) * rect.height);
                    var b = new Vector3(rect.x + (float)(i + 1) / steps * rect.width, rect.yMax - (f(t1) - rt.yMin) / (rt.yMax - rt.yMin) * rect.height);
                    Handles.DrawAAPolyLine(2f, a, b);
                }
            }
            Handles.color = prev;
        }
        private static List<System.Func<float, float>> s_live;

        /// <summary>The sound's own curves on the waveform. Gain is drawn only by the UI Toolkit editors' waveform surface.</summary>
        internal enum Curve { Volume, Pitch, Time, Gain }

        /// <summary>The modifier of <paramref name="env"/> when it is anchored to source seconds and the file is the real source.</summary>
        internal ZoundModifier SourceAnchoredModifier(Envelope env) {
            if (m_klip == null || env == null || originalClip == null) return null;
            return KlipChainEnvelopes.SourceAnchoredModifierOf(m_klip, env);
        }

        /// <summary>The part of a waveform curve's x (its own units) drawn across the overlay's rect: the view window, or
        /// the trim when the curves are clamped to it.</summary>
        internal void OverlayDomain(Envelope env, out float xMin, out float xMax) {
            var anchorMod = SourceAnchoredModifier(env);
            if (anchorMod != null) {
                // Source-anchored (T-0501): the curve's 0..1 is the whole file plus the extra time, in seconds, so the
                // visible window is simply the seconds on screen over that total -- the curve sits on its audio.
                float total = Mathf.Max(1e-6f, originalClip.length + Mathf.Max(0f, anchorMod.Param(0)));
                float a = m_clampToTrim && m_klip.trimEnabled ? m_klip.trimStart : viewStart;
                float b = m_clampToTrim && m_klip.trimEnabled ? m_klip.trimEnd : viewEnd;
                xMin = Mathf.Lerp(env.xMin, env.xMax, a / total);
                xMax = Mathf.Lerp(env.xMin, env.xMax, b / total);
            }
            else {
                xMin = m_clampToTrim ? env.xMin : Mathf.Lerp(env.xMin, env.xMax, viewStart / originalClip.length);
                xMax = m_clampToTrim ? env.xMax : Mathf.Lerp(env.xMin, env.xMax, viewEnd / originalClip.length);
            }
        }

        internal ZUIEnvelopeDef PrepareOverlay(bool volume, out ZUIEnvelopeRuntime runtime, out List<ZUIEnvelopePoint> pts, out Color colour)
            => PrepareOverlay(volume ? Curve.Volume : Curve.Pitch, out runtime, out pts, out colour);

        internal ZUIEnvelopeDef PrepareOverlay(Curve which, out ZUIEnvelopeRuntime runtime, out List<ZUIEnvelopePoint> pts, out Color colour) {
            var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;
            bool volume = which == Curve.Volume;
            var env = which == Curve.Volume ? m_volumeEnvelope : which == Curve.Pitch ? m_pitchEnvelope : TimeEnvelope;
            runtime = which == Curve.Volume ? volumeRuntime : which == Curve.Pitch ? pitchRuntime : timeRuntime;
            colour = which == Curve.Volume ? editorStyle.volumeEnvelopeColor : which == Curve.Pitch ? editorStyle.pitchEnvelopeColor : TimeCurveColor;
            pts = null;
            if (env == null || !env.enabled) return null;
            OverlayDomain(env, out runtime.xMin, out runtime.xMax);
            runtime.dataXMin = env.xMin;
            runtime.dataXMax = env.xMax;
            runtime.yMin = env.yMin;
            runtime.yMax = env.yMax;
            bool handles = which == Curve.Volume ? m_showVolumeEnvelopeHandles : which == Curve.Pitch ? m_showPitchEnvelopeHandles : m_showTimeEnvelopeHandles;
            runtime.editable = handles;
            runtime.allowAddPoints = handles;
            pts = env.GetPointsList();
            PinEndpointsYOnly(pts);
            return BuildOverlayDef(colour, volume ? editorStyle.volumeEnvelopeThickness : editorStyle.pitchEnvelopeThickness);
        }

        /// <summary>
        /// The waveform image for a <paramref name="w"/> × <paramref name="h"/> area and the part of it to show: the detailed
        /// texture of the visible range when zoomed in and ready, else the whole-clip texture with the view window as UV.
        /// Starts building the detailed one once the view has been still for a moment (only when
        /// <paramref name="mayStartBuild"/>, which the IMGUI view passes on repaint).
        /// </summary>
        internal Texture WaveformImage(int w, int h, bool mayStartBuild, out Rect uv) {
            var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;
            var audioClip = originalClip;
            // Show rendered output clip if available, otherwise fall back to source clip.
            var displayClip = m_renderedClip != null ? m_renderedClip : audioClip;
            uv = new Rect(0f, 0f, 1f, 1f);
            bool highQuality = ZoundsProject.Instance?.browserSettings?.highQualityWaveform ?? false;
            bool needsDetailedTexture = zoomFactor > 1.5f;
            bool rangeKeyMatches = zoomTextureAttempted && zoomTextureClipId == displayClip.GetInstanceID() &&
                zoomTextureWidth == w && zoomTextureHeight == h &&
                Mathf.Approximately(zoomTextureStart, viewStart) && Mathf.Approximately(zoomTextureEnd, viewEnd) &&
                zoomTextureColor == editorStyle.waveformColor && zoomTextureHighQuality == highQuality;
            bool buildingCurrentRange = rangeJob != null && rangeJob.Matches(displayClip, w, h, viewStart, viewEnd, editorStyle.waveformColor, highQuality);
            if (rangeJob != null && (!needsDetailedTexture || !buildingCurrentRange)) rangeJob = null;
            if (!needsDetailedTexture || rangeKeyMatches) zoomBuildRequested = false;
            bool detailedTextureMatches = rangeKeyMatches && zoomTexture != null;
            if (needsDetailedTexture && !rangeKeyMatches && !buildingCurrentRange && !IsTrimDragging &&
                EditorApplication.timeSinceStartup - lastViewportChangeTime >= 0.25 && mayStartBuild) {
                ClearZoomTexture();
                rangeJob = new RangeWaveformJob(displayClip, w, h, viewStart, viewEnd, editorStyle.waveformColor, highQuality);
                buildingCurrentRange = true;
                zoomBuildRequested = false;
            }
            if (needsDetailedTexture && detailedTextureMatches) return zoomTexture;
            var cacheKey = m_renderedClip != null ? "rendered_" + displayClip.GetInstanceID() : null;
            var audioTexture = AudioWaveformUtility.GetWaveformSpectrumTexture(displayClip, w, h, editorStyle.waveformColor, cacheKey);
            uv = new Rect(viewStart / audioClip.length, 0f, (viewEnd - viewStart) / audioClip.length, 1f);
            if (needsDetailedTexture && !rangeKeyMatches && !buildingCurrentRange && !IsTrimDragging) zoomBuildRequested = true;
            return audioTexture;
        }

        /// <summary>The window repainted when the detailed waveform lands (the IMGUI window, or the UI Toolkit twin's).</summary>
        internal EditorWindow window { get => m_window; set => m_window = value; }


        public void ResetStates() {
            isTrimStartDragged = false;
            isTrimEndDragged = false;
            isTrimBothDragged = false;
            ZUI.EnvelopeResetState(volumeStateKey);
            ZUI.EnvelopeResetState(pitchStateKey);
            ZUI.EnvelopeResetState(timeStateKey);
        }

        public void DrawLayout(IEnumerable<ZoundToken> playingTokens = null) {
            if (originalClip == null) return;
            ConstrainView();
            var input = Event.current.type;
            if (zoomFactor > 1.5f && (input == EventType.MouseMove || input == EventType.MouseDrag ||
                                      input == EventType.MouseDown || input == EventType.ScrollWheel ||
                                      input == EventType.KeyDown)) {
                lastViewportChangeTime = EditorApplication.timeSinceStartup;
            }

            var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;

            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 60f;
            GUILayout.BeginHorizontal();
            {
                var lineHeight = EditorGUIUtility.singleLineHeight;

                var trimEnabled = ZUI.Toggle(m_trimEnabled, "Trim", ZUI.Style.RichToggle, ZUICornerMask.Left, GUILayout.Height(lineHeight), GUILayout.Width(60f));
                if (trimEnabled != m_trimEnabled) {
                    m_trimEnabled = trimEnabled;
                    onTrimEnabledChanged?.Invoke(m_trimEnabled);
                }

                var clamp = ZUI.Toggle(m_clampToTrim, "Clamp", ZUI.Style.RichToggle, ZUICornerMask.Right, GUILayout.Height(lineHeight), GUILayout.Width(60f));
                if (clamp != m_clampToTrim) {
                    m_clampToTrim = clamp;
                    onClampToTrimChanged?.Invoke(m_clampToTrim);
                }

                GUILayout.Space(6f);
                var volEnabled = ZUI.Toggle(m_volumeEnvelope.enabled, "Volume", ZUI.Style.RichToggle, ZUICornerMask.Left, GUILayout.Height(lineHeight), GUILayout.Width(75f));
                if (volEnabled != m_volumeEnvelope.enabled) {
                    // Don't flip m_volumeEnvelope.enabled here directly: in chain mode this may still be
                    // the shared disabled-placeholder curve (no modifier created yet), and mutating that
                    // would wrongly "enable" it for every other Klip that has no modifier either. The
                    // callback owns creating/enabling the real curve; InitFromKlip re-reads it afterward.
                    onVolumeEnabledChanged?.Invoke(volEnabled);
                }
                var newShowVolumeHandles = ZUI.Toggle(m_showVolumeEnvelopeHandles, "", editIcon, editIcon, ZUI.Style.RichToggle, ZUICornerMask.Right, GUILayout.Width(25f), GUILayout.Height(lineHeight));
                if (newShowVolumeHandles != m_showVolumeEnvelopeHandles) {
                    Undo.RecordObject(m_window, "toggle volume envelope editable");
                    m_showVolumeEnvelopeHandles = newShowVolumeHandles;
                    EditorUtility.SetDirty(m_window);
                }

                GUILayout.Space(6f);
                var pitchEnabled = ZUI.Toggle(m_pitchEnvelope.enabled, "Pitch", ZUI.Style.RichToggle, ZUICornerMask.Left, GUILayout.Height(lineHeight), GUILayout.Width(65f));
                if (pitchEnabled != m_pitchEnvelope.enabled) {
                    // See the volume toggle above: the callback owns creating/enabling the real curve.
                    onPitchEnabledChanged?.Invoke(pitchEnabled);
                }
                var newShowPitchHandles = ZUI.Toggle(m_showPitchEnvelopeHandles, "", editIcon, editIcon, ZUI.Style.RichToggle, ZUICornerMask.Right, GUILayout.Width(25f), GUILayout.Height(lineHeight));
                if (newShowPitchHandles != m_showPitchEnvelopeHandles) {
                    Undo.RecordObject(m_window, "toggle pitch envelope editable");
                    m_showPitchEnvelopeHandles = newShowPitchHandles;
                    EditorUtility.SetDirty(m_window);
                }
                if (m_klip != null) {
                    // Keep length on the pitch curve and the time curve (T-0482), as in the UI Toolkit twin.
                    GUILayout.Space(2f);
                    bool keep = ZUI.Toggle(KeepLength, new GUIContent("Keep length", KeepLengthTip(KeepLength)), ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(90f), GUILayout.Height(lineHeight));
                    if (keep != KeepLength) onKeepLengthChanged?.Invoke(keep);
                    GUILayout.Space(6f);
                    bool timeOn = ZUI.Toggle(TimeEnvelope.enabled, new GUIContent("Time", TimeTip), ZUI.Style.RichToggle, ZUICornerMask.Left, GUILayout.Height(lineHeight), GUILayout.Width(60f));
                    if (timeOn != TimeEnvelope.enabled) onTimeEnabledChanged?.Invoke(timeOn);
                    bool th = ZUI.Toggle(m_showTimeEnvelopeHandles, "", editIcon, editIcon, ZUI.Style.RichToggle, ZUICornerMask.Right, GUILayout.Width(25f), GUILayout.Height(lineHeight));
                    if (th != m_showTimeEnvelopeHandles) SetShowTimeHandles(th);
                }

                GUILayout.FlexibleSpace();

                if (originalClip != null) {
                    float duration = m_trimEnabled ? (m_trimEnd - m_trimStart) : originalClip.length;
                    EditorGUILayout.LabelField($"{duration:F3}s", EditorStyles.miniLabel, GUILayout.Width(50f));
                }
            }
            GUILayout.EndHorizontal();

            EditorGUIUtility.labelWidth = labelWidth;
            ZUI.RowSpace(0.5f);

            Rect spectrumTotalRect = DrawWaveformSpectrum(originalClip, 0f);
            
            Rect trimStartHandleArea = spectrumTotalRect;
            Rect trimEndHandleArea = spectrumTotalRect;
            Rect trimmedRect = spectrumTotalRect;

            if (m_trimEnabled) {
                trimStartHandleArea = DrawTrimStartDim(spectrumTotalRect);
                trimEndHandleArea = DrawTrimEndDim(spectrumTotalRect);
                trimmedRect = Rect.MinMaxRect(TimeToX(trimStart, spectrumTotalRect), spectrumTotalRect.y,
                    TimeToX(trimEnd, spectrumTotalRect), spectrumTotalRect.yMax);
                
                // Right-click-drag to move both trim handles at once. Only
                // starts when hovering one of the thin handle lines (with a
                // small slop) — not anywhere in the middle area, which would
                // swallow clicks meant for the envelope control underneath
                // (e.g. shift-right-click for exponent edit).
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 1) {
                    const float slop = 3f;
                    Rect startHit = new Rect(trimStartHandleArea.x - slop, trimStartHandleArea.y,
                                             trimStartHandleArea.width + slop * 2f, trimStartHandleArea.height);
                    Rect endHit   = new Rect(trimEndHandleArea.x - slop,   trimEndHandleArea.y,
                                             trimEndHandleArea.width + slop * 2f,   trimEndHandleArea.height);
                    if (startHit.Contains(e.mousePosition) || endHit.Contains(e.mousePosition)) {
                        onTrimDragStarted?.Invoke();
                        isTrimBothDragged = true;
                        isTrimStartDragged = false;
                        isTrimEndDragged = false;
                        dragTrimDistance = trimEnd - trimStart;
                        float mouseTime = XToTime(e.mousePosition.x, spectrumTotalRect);
                        dragMouseOffset = mouseTime - trimStart;
                        GUI.changed = true;
                        e.Use();
                    }
                }
            }

            Rect envelopeRect = m_clampToTrim ? trimmedRect : spectrumTotalRect;

            // Every playhead, from the shared computation the UI Toolkit twin uses too.
            bool drawPlayingSource = m_audioSource != null && m_audioSource.clip != null && m_audioSource.isPlaying;
            bool needsRepaint = false;
            var playheads = PlayheadFractions(playingTokens, out bool animating);
            foreach (var fraction in playheads) AudioWaveformUtility.DrawPlayerHead(trimmedRect, fraction);
            if (animating) needsRepaint = true;

            if (m_trimEnabled) {
                DrawTrimHandles(spectrumTotalRect, trimStartHandleArea, trimEndHandleArea);
            }
            // The two overlays, each set up by the shared PrepareOverlay (visible window, range, editability, pinned ends).
            var volDef = PrepareOverlay(true, out var volRt, out var volPts, out var volColour);
            if (volDef != null && ZUI.Envelope(envelopeRect, volPts, new ZUIColorRef(volColour), volDef, volRt, volumeStateKey)) {
                onVolumeEnvelopeChanged?.Invoke(m_volumeEnvelope);
            }
            if (volDef != null) { DrawRandomEllipses(envelopeRect, volPts, volRt.xMin, volRt.xMax, volRt.yMin, volRt.yMax, volColour); DrawLiveDotted(envelopeRect, m_volumeEnvelope, volRt, volColour); }
            var pitDef = PrepareOverlay(false, out var pitRt, out var pitPts, out var pitColour);
            if (pitDef != null && ZUI.Envelope(envelopeRect, pitPts, new ZUIColorRef(pitColour), pitDef, pitRt, pitchStateKey)) {
                onPitchEnvelopeChanged?.Invoke(m_pitchEnvelope);
            }
            if (pitDef != null) { DrawRandomEllipses(envelopeRect, pitPts, pitRt.xMin, pitRt.xMax, pitRt.yMin, pitRt.yMax, pitColour); DrawLiveDotted(envelopeRect, m_pitchEnvelope, pitRt, pitColour); }
            var timDef = PrepareOverlay(Curve.Time, out var timRt, out var timPts, out var timColour);
            if (timDef != null && ZUI.Envelope(envelopeRect, timPts, new ZUIColorRef(timColour), timDef, timRt, timeStateKey)) {
                onTimeEnvelopeChanged?.Invoke(m_timeEnvelope);
            }
            if (timDef != null) {
                DrawRandomEllipses(envelopeRect, timPts, timRt.xMin, timRt.xMax, timRt.yMin, timRt.yMax, timColour);
                DrawLiveDotted(envelopeRect, m_timeEnvelope, timRt, timColour);
                // The time curve's axis on the right: x4 / x1 / x1/4 speed.
                var ts2 = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, alignment = TextAnchor.UpperRight };
                ts2.normal.textColor = timColour;
                float my2 = Mathf.Round(envelopeRect.y + envelopeRect.height * 0.5f);
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(envelopeRect.x, my2, envelopeRect.width, 1f), new Color(timColour.r, timColour.g, timColour.b, 0.3f));
                GUI.Label(new Rect(envelopeRect.xMax - 63f, envelopeRect.y + 1f, 60f, 13f), "×4", ts2);
                GUI.Label(new Rect(envelopeRect.xMax - 63f, my2 - 14f, 60f, 13f), "×1", ts2);
                GUI.Label(new Rect(envelopeRect.xMax - 63f, envelopeRect.yMax - 14f, 60f, 13f), "×¼", ts2);
            }
            // The pitch curve's axis (T-0479), as the UI Toolkit twin shows it: what its top, middle and bottom mean and
            // the "no change" line; for a curve still on its old scale only a warning, explained in its tooltip.
            if (m_klip != null && m_pitchEnvelope.enabled
                && KlipChainEnvelopes.PitchAxis(m_klip, out string pTop, out string pMid, out string pBottom, out bool pOld)) {
                var axisStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
                axisStyle.normal.textColor = new Color(pitColour.r, pitColour.g, pitColour.b, 0.9f);
                if (pOld) GUI.Label(new Rect(envelopeRect.x + 3f, envelopeRect.y + 1f, 16f, 14f), new GUIContent("⚠", KlipChainEnvelopes.OldScaleTip), axisStyle);
                else if (pTop != null) {
                    float my = Mathf.Round(envelopeRect.y + envelopeRect.height * 0.5f);
                    if (pMid != null && Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(envelopeRect.x, my, envelopeRect.width, 1f), new Color(pitColour.r, pitColour.g, pitColour.b, 0.35f));
                    GUI.Label(new Rect(envelopeRect.x + 3f, envelopeRect.y + 1f, 60f, 13f), pTop, axisStyle);
                    if (pMid != null) GUI.Label(new Rect(envelopeRect.x + 3f, my - 14f, 60f, 13f), pMid, axisStyle);
                    GUI.Label(new Rect(envelopeRect.x + 3f, envelopeRect.yMax - 14f, 60f, 13f), pBottom, axisStyle);
                }
            }

            if (needsRepaint) {
                m_window.Repaint();
            }
            else {
                // If there's an active interaction or playback, we still want to repaint
                // to keep the player head moving smoothly.
                bool isAnyTokenPlaying = false;
                if (playingTokens != null) {
                    foreach (var token in playingTokens) {
                        if (token != null && token.state == ZoundToken.State.Playing) {
                            isAnyTokenPlaying = true;
                            break;
                        }
                    }
                }
                
                if (drawPlayingSource || isAnyTokenPlaying) {
                    m_window.Repaint();
                }
            }
        }

        #region BASE-VIEW
        private Rect DrawWaveformSpectrum(AudioClip audioClip, float upperOffset) {
            var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;
            var spectrumRect = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
            var guiColor = GUI.color;
            GUI.Box(spectrumRect, GUIContent.none);

            var textureRect = spectrumRect;
            if (textureRect.height > 1 && textureRect.width > 1) {
                textureRect.x += 4;
                textureRect.width -= 8;
                textureRect.y += 4 + upperOffset;
                textureRect.height -= 8 + upperOffset;
            }
            GUI.color = editorStyle.klipWaveformBGColor;
            GUI.DrawTexture(textureRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
            HandleWheelZoom(textureRect);
            var waveform = WaveformImage(Mathf.FloorToInt(textureRect.width), Mathf.FloorToInt(textureRect.height),
                                         Event.current.type == EventType.Repaint, out var uv);
            if (waveform != null) GUI.DrawTextureWithTexCoords(textureRect, waveform, uv);
            GUI.Label(textureRect, new GUIContent("", "Mouse wheel: zoom within the trimmed range."));

            return textureRect;
        }

        private Rect DrawTrimStartDim(Rect spectrumRect) {
            var trimStartHandleArea = TrimStartHandle(spectrumRect, out var trimmedRect);
            Color guiColor = GUI.color;
            GUI.color = ZoundsProject.Instance.projectSettings.editorStyle.trimAreaColor;
            GUI.DrawTexture(trimmedRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
            return trimStartHandleArea;
        }

        private Rect DrawTrimEndDim(Rect spectrumRect) {
            var trimEndHandleArea = TrimEndHandle(spectrumRect, out var trimmedRect);
            Color guiColor = GUI.color;
            GUI.color = ZoundsProject.Instance.projectSettings.editorStyle.trimAreaColor;
            GUI.DrawTexture(trimmedRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
            return trimEndHandleArea;
        }
        #endregion

        #region TRIM-VIEW
        private void DrawTrimHandles(Rect spectrumRect, Rect trimStartHandleArea, Rect trimEndHandleArea) {
            if (trimEnd < trimStart) {
                trimEnd = trimStart;
            }

            if (trimEnd >= originalClip.length) {
                if (trimStart < originalClip.length) {
                    HandleResizeTrimEnd(trimEndHandleArea, originalClip.length, spectrumRect);
                }
            }
            else {
                HandleResizeTrimEnd(trimEndHandleArea, originalClip.length, spectrumRect);
            }

            if (trimStart == 0) {
                if (trimEnd > 0) {
                    HandleResizeTrimStart(trimStartHandleArea, originalClip.length, spectrumRect);
                }
            }
            else {
                HandleResizeTrimStart(trimStartHandleArea, originalClip.length, spectrumRect);
            }
        }

        private void HandleResizeTrimStart(Rect trimStartHandleArea, float clipDuration, Rect spectrumRect) {
            var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;
            Color guiColor = GUI.color;
            GUI.color = editorStyle.trimHandleColor;
            if (!GUI.enabled) GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.35f);
            GUI.DrawTexture(trimStartHandleArea, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            EditorGUIUtility.AddCursorRect(trimStartHandleArea, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            switch (e.type) {
                case EventType.MouseDown:
                    if (e.button == 0) {
                        if (trimStartHandleArea.Contains(e.mousePosition)) {
                            onTrimDragStarted?.Invoke();
                            isTrimStartDragged = true;
                            isTrimEndDragged = false;
                            isTrimBothDragged = false;
                            GUI.changed = true;
                            e.Use();
                        }
                    }
                    else if (e.button == 1) { // Right click
                        if (trimStartHandleArea.Contains(e.mousePosition)) {
                            onTrimDragStarted?.Invoke();
                            isTrimBothDragged = true;
                            isTrimStartDragged = false;
                            isTrimEndDragged = false;
                            dragTrimDistance = trimEnd - trimStart;
                            float mouseTime = XToTime(e.mousePosition.x, spectrumRect);
                            dragMouseOffset = mouseTime - trimStart;
                            GUI.changed = true;
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseUp:
                case EventType.Ignore:
                    isTrimStartDragged = false;
                    isTrimBothDragged = false;
                    break;

                case EventType.MouseDrag:
                    if (isTrimStartDragged) {
                        var newTrimStart = XToTime(e.mousePosition.x, spectrumRect);

                        if (newTrimStart < 0) newTrimStart = 0;
                        else if (newTrimStart >= trimEnd) newTrimStart = trimEnd;
                        trimStart = newTrimStart;
                        m_window.Repaint();
                        e.Use();
                    }
                    else if (isTrimBothDragged) {
                        var mouseTime = XToTime(e.mousePosition.x, spectrumRect);
                        var newTrimStart = mouseTime - dragMouseOffset;

                        if (newTrimStart < 0) newTrimStart = 0;
                        else if (newTrimStart + dragTrimDistance > clipDuration) newTrimStart = clipDuration - dragTrimDistance;
                        
                        trimStart = newTrimStart;
                        trimEnd = newTrimStart + dragTrimDistance;
                        m_window.Repaint();
                        e.Use();
                    }
                    break;
            }
        }

        private void HandleResizeTrimEnd(Rect trimEndHandleArea, float clipDuration, Rect spectrumRect) {
            var editorStyle = ZoundsProject.Instance.projectSettings.editorStyle;
            Color guiColor = GUI.color;
            GUI.color = editorStyle.trimHandleColor;
            if (!GUI.enabled) GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.35f);
            GUI.DrawTexture(trimEndHandleArea, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            EditorGUIUtility.AddCursorRect(trimEndHandleArea, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            switch (e.type) {
                case EventType.MouseDown:
                    if (e.button == 0) {
                        if (trimEndHandleArea.Contains(e.mousePosition)) {
                            onTrimDragStarted?.Invoke();
                            isTrimEndDragged = true;
                            isTrimStartDragged = false;
                            isTrimBothDragged = false;
                            GUI.changed = true;
                            e.Use();
                        }
                    }
                    else if (e.button == 1) { // Right click
                        if (trimEndHandleArea.Contains(e.mousePosition)) {
                            onTrimDragStarted?.Invoke();
                            isTrimBothDragged = true;
                            isTrimStartDragged = false;
                            isTrimEndDragged = false;
                            dragTrimDistance = trimEnd - trimStart;
                            float mouseTime = XToTime(e.mousePosition.x, spectrumRect);
                            dragMouseOffset = mouseTime - trimStart;
                            GUI.changed = true;
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseUp:
                case EventType.Ignore:
                    isTrimEndDragged = false;
                    isTrimBothDragged = false;
                    break;

                case EventType.MouseDrag:
                    if (isTrimEndDragged) {
                        var newTrimEnd = XToTime(e.mousePosition.x, spectrumRect);

                        if (newTrimEnd < trimStart) newTrimEnd = trimStart;
                        else if (newTrimEnd >= clipDuration) newTrimEnd = clipDuration;
                        trimEnd = newTrimEnd;
                        m_window.Repaint();
                        e.Use();
                    }
                    else if (isTrimBothDragged) {
                        var mouseTime = XToTime(e.mousePosition.x, spectrumRect);
                        var newTrimStart = mouseTime - dragMouseOffset;

                        if (newTrimStart < 0) newTrimStart = 0;
                        else if (newTrimStart + dragTrimDistance > clipDuration) newTrimStart = clipDuration - dragTrimDistance;
                        
                        trimStart = newTrimStart;
                        trimEnd = newTrimStart + dragTrimDistance;
                        m_window.Repaint();
                        e.Use();
                    }
                    break;
            }
        }
        #endregion

    }

}
