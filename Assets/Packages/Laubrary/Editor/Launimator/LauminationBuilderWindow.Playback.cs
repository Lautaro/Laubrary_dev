using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        private bool _phasePreviewMode;
        private int _previewPhaseIndex;
        private ZonedAnimationPlayer _phasePreviewPlayer;
        private LauminaryVersion _phasePreviewVersion;
        private int _phasePreviewHash;
        private Texture2D _previewZeroTex;
        private bool _stageOneToOne;
        private Vector2 _stagePan;
        private bool _stagePanning;
        private double _previewPausedAt;
        private Button _transportPlayButton;
        private Label _transportPhaseLabel;

        private VisualElement BuildPreviewTransport(bool phases)
        {
            SetPhasePreviewMode(phases);
            var row = Z.Row();
            row.style.flexWrap = Wrap.NoWrap;
            row.style.height = 28;
            row.style.flexShrink = 0;
            _transportPlayButton = Z.Button(_animPlaying ? "Pause" : "Play", "Pause or resume the preview at its current frame.", TogglePreviewPlayback);
            _transportPlayButton.style.width = 54;
            row.Add(_transportPlayButton);
            var restart = Z.Button("Restart", "Play again from the beginning of the sequence or selected phase.", () =>
            {
                EnsurePreviewBake(); RestartPreview(); _animPlaying = _previewDef != null; UpdatePreviewTransport(); Repaint();
            });
            restart.style.width = 58;
            row.Add(restart);
            var fit = Z.Button("Fit", "Fit every visible frame around the registration point. Middle-drag pans the stage.", () => FitPreviewStage());
            fit.style.width = 34; row.Add(fit);
            var actual = Z.Button("1:1", "Show one source pixel per editor point; middle-drag to pan within the stage.", () => FitPreviewStage(true));
            actual.style.width = 34; row.Add(actual);
            if (phases)
            {
                var choices = _zones.Where(z => z != null).ToList();
                if (choices.Count > 0)
                {
                    _previewPhaseIndex = Mathf.Clamp(_previewPhaseIndex, 0, choices.Count - 1);
                    row.Add(Z.Dropdown(_previewPhaseIndex, choices.Select(z => z.name).ToList(),
                        "Choose the phase to audition. Play starts here; play-through phases continue, loop phases wait for Advance.", i =>
                        {
                            _previewPhaseIndex = i;
                            EnsurePreviewBake(); StartPhasePreview(); _animPlaying = _previewDef != null;
                            UpdatePreviewTransport(); Repaint();
                        }, 110));
                }
                var advance = Z.Button("Advance", "Leave the current phase immediately and enter the next phase, matching runtime Advance.", () =>
                {
                    EnsurePhasePreview();
                    if (_phasePreviewPlayer == null) return;
                    _phasePreviewPlayer.Advance(); _animFrame = _phasePreviewPlayer.CurrentFrame;
                    _animPlaying = _phasePreviewPlayer.IsPlaying;
                    RefreshFrameLabels(); UpdatePreviewTransport(); Repaint();
                });
                advance.style.width = 65; advance.SetEnabled(_zonesEnabled && choices.Count > 0); row.Add(advance);
                _transportPhaseLabel = Z.Text("", tooltip: "The phase currently playing; frame numbers elsewhere remain the real sequence positions.");
                _transportPhaseLabel.style.width = 90;
                _transportPhaseLabel.style.overflow = Overflow.Hidden;
                _transportPhaseLabel.style.whiteSpace = WhiteSpace.NoWrap;
                _transportPhaseLabel.style.textOverflow = TextOverflow.Ellipsis;
                row.Add(_transportPhaseLabel);
            }
            else _transportPhaseLabel = null;
            UpdatePreviewTransport();
            return row;
        }

        private void SetPhasePreviewMode(bool phases)
        {
            if (_phasePreviewMode == phases) return;
            PausePreview(); _phasePreviewMode = phases;
            _showingFrameZero = false; _inDivider = false;
            if (_sequence.Count > 0) SeekPreviewFrame(_animFrame);
        }

        private void TogglePreviewPlayback()
        {
            if (_animPlaying) PausePreview();
            else
            {
                EnsurePreviewBake();
                if (_previewDef == null) return;
                if (_previewPausedAt > 0)
                {
                    double pausedFor = EditorApplication.timeSinceStartup - _previewPausedAt;
                    if (_showingFrameZero) _frameZeroUntil += pausedFor;
                    if (_inDivider) _dividerUntil += pausedFor;
                    _previewPausedAt = 0;
                }
                if (_phasePreviewMode)
                {
                    EnsurePhasePreview();
                    if (_phasePreviewPlayer == null || !_phasePreviewPlayer.IsPlaying) StartPhasePreview();
                }
                else if (!_previewPlayer.IsPlaying && !_showingFrameZero && !_inDivider) RestartPreview();
                _animLastStep = EditorApplication.timeSinceStartup;
                _animPlaying = true;
            }
            UpdatePreviewTransport(); Repaint();
        }

        private void PausePreview()
        {
            // Do not stop either clock: pausing must preserve its fractional frame and phase.
            if (_animPlaying) _previewPausedAt = EditorApplication.timeSinceStartup;
            _animPlaying = false;
            UpdatePreviewTransport();
        }

        private void SeekPreviewFrame(int frame)
        {
            PausePreview();
            if (_sequence.Count == 0) return;
            frame = Mathf.Clamp(frame, 0, _sequence.Count - 1);
            EnsurePreviewBake();
            _showingFrameZero = false; _inDivider = false; _previewPausedAt = 0;
            if (_previewDef != null)
            {
                // Both clocks remain armed; the window's pause gate controls whether they are ticked.
                // A subsequent Play resumes here even if the user switches between Timing and Phases.
                if (_previewPlayer.Anim != _previewDef || !_previewPlayer.IsPlaying) StartClip();
                _previewPlayer.SeekFrame(frame);
                EnsurePhasePreview();
                if (_phasePreviewPlayer != null)
                {
                    if (!_phasePreviewPlayer.IsPlaying) _phasePreviewPlayer.Play(_previewDef.name);
                    _phasePreviewPlayer.SeekFrame(frame);
                }
            }
            _animFrame = frame;
            RefreshFrameLabels(); SyncFrameTimelinePlayhead(); UpdatePreviewTransport();
            _playIM?.MarkDirtyRepaint(); _regCanvasIM?.MarkDirtyRepaint(); Repaint();
        }

        private void UpdatePreviewTransport()
        {
            if (_transportPlayButton != null)
            {
                _transportPlayButton.text = _animPlaying ? "Pause" : "Play";
                _transportPlayButton.tooltip = _animPlaying ? "Pause the preview on this frame." : "Resume the preview; a finished sequence starts again.";
            }
            if (_transportPhaseLabel != null)
            {
                _transportPhaseLabel.text = _phasePreviewPlayer != null ? _phasePreviewPlayer.CurrentZoneName ?? "Sequence" : "";
                _transportPhaseLabel.tooltip = "Currently playing: " + _transportPhaseLabel.text;
            }
        }

        private int PhasePreviewHash()
        {
            unchecked
            {
                int h = _zonesEnabled ? 1 : 0;
                foreach (var z in _zones)
                {
                    if (z == null) continue;
                    h = h * 31 + (z.name ?? "").GetHashCode(); h = h * 31 + z.startFrame;
                    h = h * 31 + z.endFrame; h = h * 31 + (int)z.behavior;
                }
                return h;
            }
        }

        private void EnsurePhasePreview()
        {
            if (_previewDef == null) return;
            int hash = PhasePreviewHash();
            if (_phasePreviewPlayer != null && _phasePreviewHash == hash) return;
            DestroyPhasePreview();
            _phasePreviewHash = hash;
            _previewDef.zonesEnabled = _zonesEnabled;
            _previewDef.zones = _zones.Where(z => z != null).Select(z => new AnimZone
                { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }).ToList();
            _phasePreviewVersion = ScriptableObject.CreateInstance<LauminaryVersion>();
            _phasePreviewVersion.hideFlags = HideFlags.HideAndDontSave;
            _phasePreviewVersion.animations = new List<Laumination> { _previewDef };
            var host = new GameObject("Builder phase audition") { hideFlags = HideFlags.HideAndDontSave };
            // The runtime player supplies the clock, never scene output. The inactive host cannot render or Update.
            host.SetActive(false);
            _phasePreviewPlayer = host.AddComponent<ZonedAnimationPlayer>();
            _phasePreviewPlayer.SetVersion(_phasePreviewVersion);
        }

        private void StartPhasePreview()
        {
            EnsurePhasePreview();
            if (_phasePreviewPlayer == null) return;
            _showingFrameZero = false; _inDivider = false; _previewPlayer.Stop();
            _phasePreviewPlayer.Play(_previewDef.name);
            var choices = _zones.Where(z => z != null).ToList();
            if (_zonesEnabled && choices.Count > 0)
            {
                _previewPhaseIndex = Mathf.Clamp(_previewPhaseIndex, 0, choices.Count - 1);
                _phasePreviewPlayer.EnterAt(choices[_previewPhaseIndex].name);
            }
            _animFrame = _phasePreviewPlayer.CurrentFrame;
            UpdatePreviewTransport();
        }

        private void DestroyPhasePreview()
        {
            if (_phasePreviewPlayer != null) Object.DestroyImmediate(_phasePreviewPlayer.gameObject);
            if (_phasePreviewVersion != null) Object.DestroyImmediate(_phasePreviewVersion);
            _phasePreviewPlayer = null; _phasePreviewVersion = null;
        }

        private void FitPreviewStage(bool oneToOne = false)
        {
            _stageOneToOne = oneToOne; _stagePan = Vector2.zero;
            _metaPan = Vector2.zero;
            _metaZoom = oneToOne ? 1f : 0f;
            _playIM?.MarkDirtyRepaint(); _regCanvasIM?.MarkDirtyRepaint(); Repaint();
        }

        // Coordinates are local to the supplied stage; call before BeginClip, or with a local rect inside it.
        private float PreviewStageScale(Rect box, Rect bounds, Vector2 anchor, float maxScale = 12f)
        {
            if (_stageOneToOne) return 1f;
            float fit = FramePreview.FitScale(box, bounds, anchor, maxScale);
            return fit >= 1f ? Mathf.Floor(fit) : fit;
        }

        private void HandlePreviewPan(Rect box)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 2 && box.Contains(e.mousePosition))
            { _stagePanning = true; e.Use(); }
            else if (e.type == EventType.MouseDrag && _stagePanning)
            { _stagePan += e.delta; e.Use(); Repaint(); }
            else if (e.rawType == EventType.MouseUp && _stagePanning)
            { _stagePanning = false; e.Use(); }
        }
    }
}
