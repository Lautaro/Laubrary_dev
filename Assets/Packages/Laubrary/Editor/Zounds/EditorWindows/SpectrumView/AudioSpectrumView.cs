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

        [SerializeField] private float m_trimStart;
        [SerializeField] private float m_trimEnd;
        [SerializeField] private bool m_clampToTrim = true;
        [SerializeField] private Envelope m_volumeEnvelope;
        [SerializeField] private Envelope m_pitchEnvelope;

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
        private static void PinEndpointsYOnly(List<ZUIEnvelopePoint> points) {
            if (points == null || points.Count == 0) return;
            points[0].editState = ZUIEnvelopeEditState.YEditable;
            for (int i = 1; i < points.Count - 1; i++) {
                points[i].editState = ZUIEnvelopeEditState.Editable;
            }
            if (points.Count > 1) {
                points[points.Count - 1].editState = ZUIEnvelopeEditState.YEditable;
            }
        }

        private static ZUIEnvelopeDef BuildOverlayDef(Color curveColor, float curveThickness) {
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

        public void InitFromKlip(Klip klip) {
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
            m_volumeEnvelope = klip.volumeEnvelope;
            m_pitchEnvelope = klip.pitchEnvelope;
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
            if (!m_trimEnabled || e.type != EventType.ScrollWheel || !rect.Contains(e.mousePosition)) return;
            float trimDuration = m_trimEnd - m_trimStart;
            if (trimDuration <= 0f || originalClip.length <= trimDuration) return;
            float anchor = XToTime(e.mousePosition.x, rect);
            float fraction = Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
            zoomFactor = Mathf.Clamp(zoomFactor * Mathf.Pow(1.25f, -e.delta.y), 1f, originalClip.length / trimDuration);
            viewStart = anchor - fraction * originalClip.length / zoomFactor;
            ConstrainView();
            e.Use();
            m_window.Repaint();
        }

        public void ResetStates() {
            isTrimStartDragged = false;
            isTrimEndDragged = false;
            isTrimBothDragged = false;
            ZUI.EnvelopeResetState(volumeStateKey);
            ZUI.EnvelopeResetState(pitchStateKey);
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
                    m_volumeEnvelope.enabled = volEnabled;
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
                    m_pitchEnvelope.enabled = pitchEnabled;
                    onPitchEnabledChanged?.Invoke(pitchEnabled);
                }
                var newShowPitchHandles = ZUI.Toggle(m_showPitchEnvelopeHandles, "", editIcon, editIcon, ZUI.Style.RichToggle, ZUICornerMask.Right, GUILayout.Width(25f), GUILayout.Height(lineHeight));
                if (newShowPitchHandles != m_showPitchEnvelopeHandles) {
                    Undo.RecordObject(m_window, "toggle pitch envelope editable");
                    m_showPitchEnvelopeHandles = newShowPitchHandles;
                    EditorUtility.SetDirty(m_window);
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

            bool drawPlayingSource = false;
            if (m_audioSource != null && m_audioSource.clip != null) {
                if (m_audioSource.isPlaying) {
                    drawPlayingSource = true;
                }
            }

            bool needsRepaint = false;

            if (drawPlayingSource) {
                needsRepaint = true;
                float timePercentage;
                if (m_pitchEnvelope.enabled) {
                    float totalTime = trimEnd - trimStart;
                    float integrationSteps = AudioRenderUtility.GetOptimalIntegrationSteps(totalTime);
                    float step = totalTime / integrationSteps;

                    float t = 0f;
                    float renderedTime = 0f;

                    while (t <= totalTime && renderedTime < m_audioSource.time) {
                        float pitch = Mathf.Max(0.01f, m_pitchEnvelope.Evaluate(t / totalTime));
                        float dt = step;
                        renderedTime += dt / pitch;
                        t += dt;
                    }

                    timePercentage = t / totalTime;
                }
                else {
                    timePercentage = m_audioSource.time / m_audioSource.clip.length;
                }
                AudioWaveformUtility.DrawPlayerHead(trimmedRect, timePercentage);
            }

            if (playingTokens != null && playingTokens.Count() > 0) {
                foreach (var token in playingTokens) {
                    if (token == null || token.state == ZoundToken.State.Killed) continue;

                    // Use audioSource.time for accurate playhead — token.time is a manual counter
                    // that can drift from actual playback position.
                    float playTime = token.audioSource != null && token.audioSource.clip != null
                        ? token.audioSource.time : token.time;
                    float clipLength = token.audioSource != null && token.audioSource.clip != null
                        ? token.audioSource.clip.length : token.duration;

                    if (token.zound is Klip klip && klip.pitchEnvelope.enabled && !klip.needsRender && !token.isRealtime) {
                        // Pitch envelope changes duration: map rendered time back to source time
                        float totalTime = klip.trimEnd - klip.trimStart;
                        float integrationSteps = AudioRenderUtility.GetOptimalIntegrationSteps(totalTime);
                        float step = totalTime / integrationSteps;

                        float t = 0f;
                        float renderedTime = 0f;

                        while (t <= totalTime && renderedTime < playTime) {
                            float pitch = Mathf.Max(0.01f, klip.pitchEnvelope.Evaluate(t / totalTime));
                            float dt = step;
                            renderedTime += dt / pitch;
                            t += dt;
                        }

                        AudioWaveformUtility.DrawPlayerHead(trimmedRect, t / totalTime);
                    }
                    else {
                        // No pitch envelope (or needs render / realtime): linear mapping
                        float percentage = clipLength > 0f ? playTime / clipLength : 0f;
                        AudioWaveformUtility.DrawPlayerHead(trimmedRect, percentage);
                    }
                    needsRepaint = true;
                }
            }

            if (m_trimEnabled) {
                DrawTrimHandles(spectrumTotalRect, trimStartHandleArea, trimEndHandleArea);
            }
            if (m_volumeEnvelope.enabled) {
                volumeRuntime.xMin = m_clampToTrim ? m_volumeEnvelope.xMin : Mathf.Lerp(m_volumeEnvelope.xMin, m_volumeEnvelope.xMax, viewStart / originalClip.length);
                volumeRuntime.xMax = m_clampToTrim ? m_volumeEnvelope.xMax : Mathf.Lerp(m_volumeEnvelope.xMin, m_volumeEnvelope.xMax, viewEnd / originalClip.length);
                volumeRuntime.dataXMin = m_volumeEnvelope.xMin;
                volumeRuntime.dataXMax = m_volumeEnvelope.xMax;
                volumeRuntime.yMin = m_volumeEnvelope.yMin;
                volumeRuntime.yMax = m_volumeEnvelope.yMax;
                volumeRuntime.editable       = m_showVolumeEnvelopeHandles;
                volumeRuntime.allowAddPoints = m_showVolumeEnvelopeHandles;
                var volPts = m_volumeEnvelope.GetPointsList();
                PinEndpointsYOnly(volPts);
                var volDef = BuildOverlayDef(editorStyle.volumeEnvelopeColor,
                                             editorStyle.volumeEnvelopeThickness);
                if (ZUI.Envelope(envelopeRect, volPts,
                                 new ZUIColorRef(editorStyle.volumeEnvelopeColor),
                                 volDef, volumeRuntime, volumeStateKey)) {
                    onVolumeEnvelopeChanged?.Invoke(m_volumeEnvelope);
                }
            }

            if (m_pitchEnvelope.enabled) {
                pitchRuntime.xMin = m_clampToTrim ? m_pitchEnvelope.xMin : Mathf.Lerp(m_pitchEnvelope.xMin, m_pitchEnvelope.xMax, viewStart / originalClip.length);
                pitchRuntime.xMax = m_clampToTrim ? m_pitchEnvelope.xMax : Mathf.Lerp(m_pitchEnvelope.xMin, m_pitchEnvelope.xMax, viewEnd / originalClip.length);
                pitchRuntime.dataXMin = m_pitchEnvelope.xMin;
                pitchRuntime.dataXMax = m_pitchEnvelope.xMax;
                pitchRuntime.yMin = m_pitchEnvelope.yMin;
                pitchRuntime.yMax = m_pitchEnvelope.yMax;
                pitchRuntime.editable       = m_showPitchEnvelopeHandles;
                pitchRuntime.allowAddPoints = m_showPitchEnvelopeHandles;
                var pitPts = m_pitchEnvelope.GetPointsList();
                PinEndpointsYOnly(pitPts);
                var pitDef = BuildOverlayDef(editorStyle.pitchEnvelopeColor,
                                             editorStyle.pitchEnvelopeThickness);
                if (ZUI.Envelope(envelopeRect, pitPts,
                                 new ZUIColorRef(editorStyle.pitchEnvelopeColor),
                                 pitDef, pitchRuntime, pitchStateKey)) {
                    onPitchEnvelopeChanged?.Invoke(m_pitchEnvelope);
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
            // Show rendered output clip if available, otherwise fall back to source clip.
            var displayClip = m_renderedClip != null ? m_renderedClip : audioClip;
            int textureWidth = Mathf.FloorToInt(textureRect.width);
            int textureHeight = Mathf.FloorToInt(textureRect.height);
            bool highQuality = ZoundsProject.Instance?.browserSettings?.highQualityWaveform ?? false;
            bool needsDetailedTexture = zoomFactor > 1.5f;
            bool rangeKeyMatches = zoomTextureAttempted && zoomTextureClipId == displayClip.GetInstanceID() &&
                zoomTextureWidth == textureWidth && zoomTextureHeight == textureHeight &&
                Mathf.Approximately(zoomTextureStart, viewStart) && Mathf.Approximately(zoomTextureEnd, viewEnd) &&
                zoomTextureColor == editorStyle.waveformColor && zoomTextureHighQuality == highQuality;
            bool buildingCurrentRange = rangeJob != null && rangeJob.Matches(displayClip, textureWidth, textureHeight,
                viewStart, viewEnd, editorStyle.waveformColor, highQuality);
            if (rangeJob != null && (!needsDetailedTexture || !buildingCurrentRange)) rangeJob = null;
            if (!needsDetailedTexture || rangeKeyMatches) zoomBuildRequested = false;
            bool detailedTextureMatches = rangeKeyMatches && zoomTexture != null;
            if (needsDetailedTexture && !rangeKeyMatches && !buildingCurrentRange &&
                !isTrimStartDragged && !isTrimEndDragged && !isTrimBothDragged &&
                EditorApplication.timeSinceStartup - lastViewportChangeTime >= 0.25 && Event.current.type == EventType.Repaint) {
                ClearZoomTexture();
                rangeJob = new RangeWaveformJob(displayClip, textureWidth, textureHeight, viewStart, viewEnd,
                    editorStyle.waveformColor, highQuality);
                buildingCurrentRange = true;
                zoomBuildRequested = false;
            }
            if (needsDetailedTexture && detailedTextureMatches) {
                GUI.DrawTexture(textureRect, zoomTexture);
            }
            else {
                var cacheKey = m_renderedClip != null ? "rendered_" + displayClip.GetInstanceID() : null;
                var audioTexture = AudioWaveformUtility.GetWaveformSpectrumTexture(displayClip, textureWidth, textureHeight, editorStyle.waveformColor, cacheKey);
                if (audioTexture != null) {
                    var uv = new Rect(viewStart / audioClip.length, 0f, (viewEnd - viewStart) / audioClip.length, 1f);
                    GUI.DrawTextureWithTexCoords(textureRect, audioTexture, uv);
                }
                if (needsDetailedTexture && !rangeKeyMatches && !buildingCurrentRange &&
                    !isTrimStartDragged && !isTrimEndDragged && !isTrimBothDragged) zoomBuildRequested = true;
            }
            GUI.Label(textureRect, new GUIContent("", "Mouse wheel: zoom within the trimmed range."));

            return textureRect;
        }

        private Rect DrawTrimStartDim(Rect spectrumRect) {
            float trimStartWidth = TimeToX(trimStart, spectrumRect) - spectrumRect.x;
            var trimStartHandleArea = spectrumRect;
            trimStartHandleArea.x = Mathf.Clamp(spectrumRect.x + trimStartWidth, spectrumRect.x, spectrumRect.xMax - ZoundsProject.Instance.projectSettings.editorStyle.trimHandleThickness);
            trimStartHandleArea.width = ZoundsProject.Instance.projectSettings.editorStyle.trimHandleThickness;

            Color guiColor = GUI.color;
            var trimmedRect = new Rect(spectrumRect.x, spectrumRect.y, trimStartWidth, spectrumRect.height);
            GUI.color = ZoundsProject.Instance.projectSettings.editorStyle.trimAreaColor;
            GUI.DrawTexture(trimmedRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
            return trimStartHandleArea;
        }

        private Rect DrawTrimEndDim(Rect spectrumRect) {
            float trimEndWidth = TimeToX(trimEnd, spectrumRect) - spectrumRect.x;
            var trimEndHandleArea = spectrumRect;
            trimEndHandleArea.x = Mathf.Clamp(spectrumRect.x + trimEndWidth - ZoundsProject.Instance.projectSettings.editorStyle.trimHandleThickness, spectrumRect.x, spectrumRect.xMax - ZoundsProject.Instance.projectSettings.editorStyle.trimHandleThickness);
            trimEndHandleArea.width = ZoundsProject.Instance.projectSettings.editorStyle.trimHandleThickness;

            Color guiColor = GUI.color;
            var trimmedRect = new Rect(spectrumRect.x + trimEndWidth, spectrumRect.y, spectrumRect.width - trimEndWidth, spectrumRect.height);
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
