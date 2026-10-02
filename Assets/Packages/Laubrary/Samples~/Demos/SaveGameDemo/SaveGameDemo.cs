using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SaveGame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Laubrary.Demos
{
    public class SaveGameDemo : MonoBehaviour
    {
        [Header("Left Panel")]
        public Button createSaveButton;
        public Button addSnapshotButton;
        public Button loadSaveButton;
        public Button deleteSaveButton;
        public Button openFolderButton;
        public TMP_InputField saveNameInput;

        [Header("Save List")]
        public Transform saveListContent;

        [Header("Game Panel")]
        public Button mutateButton;
        public RawImage gameImage;
        public TextMeshProUGUI detailsText;
        public TextMeshProUGUI playtimeText;

        // ── Game constants ──────────────────────────────────────────────
        private const int PaletteSize         = 5;
        private const int TextureSize         = 256;
        private const int ColorChangeInterval = 10;
        private const int StampSizeMin        = 4;
        private const int StampSizeMax        = 48;

        // ── Metadata keys used by this demo ────────────────────────────
        private const string MetaKeyPlayTime = "playTime";

        // ── Runtime state ───────────────────────────────────────────────
        private SaveManager _saveManager;
        private SaveFile    _currentSave;
        private string      _selectedSaveId;
        private string      _selectedSnapshotId;

        private GameObject               _saveItemTemplate;
        private readonly List<SaveListItem> _listItems = new();

        // ── Game state ──────────────────────────────────────────────────
        private Texture2D _gameTexture;
        private Color[]   _palette;
        private int       _turnNumber;
        private float     _playtimeSeconds;
        private float     _playtimeTick;

        // ── Lifecycle ───────────────────────────────────────────────────
        void Start()
        {
            _saveManager = new SaveManager
            {
                DebugMode           = true,
                MaxSnapshotsPerSave = 10
            };

            EnsureSaveItemTemplate();
            WireButtons();
            InitGame();
            RefreshList();
        }

        void OnDestroy()
        {
            _saveManager?.ClearThumbnailCache();
            if (_gameTexture != null) Destroy(_gameTexture);
        }

        void Update()
        {
            _playtimeSeconds += Time.deltaTime;
            _playtimeTick    += Time.deltaTime;
            if (_playtimeTick >= 1f)
            {
                _playtimeTick -= 1f;
                UpdatePlaytimeLabel();
            }
        }

        // ── Button wiring ───────────────────────────────────────────────
        void WireButtons()
        {
            if (createSaveButton  != null) createSaveButton .onClick.AddListener(OnCreateSave);
            if (addSnapshotButton != null) addSnapshotButton.onClick.AddListener(OnAddSnapshot);
            if (loadSaveButton    != null) loadSaveButton   .onClick.AddListener(OnLoadSave);
            if (deleteSaveButton  != null) deleteSaveButton .onClick.AddListener(OnDeleteSave);
            if (openFolderButton  != null) openFolderButton .onClick.AddListener(OnOpenFolder);
            if (mutateButton      != null) mutateButton     .onClick.AddListener(OnMutate);
        }

        // ── Game ────────────────────────────────────────────────────────

        /// <summary>Creates a fresh game state with a random palette and solid base canvas.</summary>
        void InitGame()
        {
            _turnNumber      = 0;
            _playtimeSeconds = 0f;
            _playtimeTick    = 0f;
            _palette         = new Color[PaletteSize];
            _gameTexture     = CreateBlankTexture();

            for (int i = 0; i < PaletteSize; i++)
                _palette[i] = RandomColor();

            // Fill canvas with the first palette color.
            Color[] fill = Enumerable.Repeat(_palette[0], TextureSize * TextureSize).ToArray();
            _gameTexture.SetPixels(fill);
            _gameTexture.Apply();

            if (gameImage != null) gameImage.texture = _gameTexture;
            UpdateDetails();
        }

        /// <summary>Advances the game one turn: stamps a random-color square onto the canvas.</summary>
        void OnMutate()
        {
            _turnNumber++;

            // Every ColorChangeInterval turns, replace one palette color.
            if (_turnNumber % ColorChangeInterval == 0)
                _palette[UnityEngine.Random.Range(0, PaletteSize)] = RandomColor();

            Color stampColor = _palette[UnityEngine.Random.Range(0, PaletteSize)];
            int   size       = UnityEngine.Random.Range(StampSizeMin, StampSizeMax + 1);
            int   cx         = UnityEngine.Random.Range(0, TextureSize);
            int   cy         = UnityEngine.Random.Range(0, TextureSize);

            Stamp(cx, cy, size, stampColor);
            _gameTexture.Apply();
            UpdateDetails();
        }

        /// <summary>Paints a filled square centered at (cx, cy) on _gameTexture.</summary>
        void Stamp(int cx, int cy, int size, Color color)
        {
            int half   = size / 2;
            int xStart = Mathf.Clamp(cx - half, 0, TextureSize - 1);
            int xEnd   = Mathf.Clamp(cx + half, 0, TextureSize - 1);
            int yStart = Mathf.Clamp(cy - half, 0, TextureSize - 1);
            int yEnd   = Mathf.Clamp(cy + half, 0, TextureSize - 1);

            for (int x = xStart; x <= xEnd; x++)
            for (int y = yStart; y <= yEnd; y++)
                _gameTexture.SetPixel(x, y, color);
        }

        /// <summary>Restores the game image and palette from a saved snapshot.</summary>
        void RestoreGameState(GameState state, byte[] imageBytes)
        {
            _turnNumber      = state.turnNumber;
            _playtimeSeconds = state.playtimeSeconds;
            _playtimeTick    = 0f;
            _palette         = new Color[PaletteSize];

            for (int i = 0; i < PaletteSize; i++)
            {
                if (state.paletteHex != null && i < state.paletteHex.Length &&
                    ColorUtility.TryParseHtmlString(state.paletteHex[i], out Color c))
                    _palette[i] = c;
                else
                    _palette[i] = RandomColor();
            }

            if (imageBytes != null && imageBytes.Length > 0)
            {
                if (_gameTexture == null) _gameTexture = CreateBlankTexture();
                _gameTexture.LoadImage(imageBytes);
                _gameTexture.Apply();
                if (gameImage != null) gameImage.texture = _gameTexture;
            }

            UpdateDetails();
        }

        Texture2D CreateBlankTexture() =>
            new Texture2D(TextureSize, TextureSize, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Point
            };

        /// <summary>HSV-based random for vibrant, distinct palette colors.</summary>
        static Color RandomColor() =>
            UnityEngine.Random.ColorHSV(0f, 1f, 0.55f, 1f, 0.7f, 1f);

        // ── Details display ─────────────────────────────────────────────

        /// <summary>Reflects current in-memory game state only. Never reads from disk.</summary>
        void UpdateDetails()
        {
            if (detailsText == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"<b>Turn {_turnNumber}</b>");
            sb.Append("Colors: ");
            if (_palette != null)
                foreach (Color c in _palette)
                    sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(c)}>■</color> ");

            detailsText.text = sb.ToString();
        }

        /// <summary>Formats _playtimeSeconds as MM:SS or HH:MM:SS and pushes it to the label.</summary>
        void UpdatePlaytimeLabel()
        {
            if (playtimeText == null) return;
            TimeSpan ts = TimeSpan.FromSeconds(_playtimeSeconds);
            playtimeText.text = ts.Hours > 0
                ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        // ── Template cleanup ────────────────────────────────────────────

        /// <summary>
        /// Keeps the first child of saveListContent as the runtime template.
        /// Uses DestroyImmediate to avoid deferred-destroy conflicts during Start().
        /// Finds SnapshotContainer by name — never trusts serialized refs.
        /// </summary>
        void EnsureSaveItemTemplate()
        {
            if (saveListContent == null) return;

            var children = new List<Transform>();
            foreach (Transform child in saveListContent) children.Add(child);

            if (children.Count == 0) return;

            _saveItemTemplate = children[0].gameObject;

            Transform snapContainer = _saveItemTemplate.transform.Find("SnapshotContainer");
            if (snapContainer != null)
            {
                var snapChildren = new List<Transform>();
                foreach (Transform c in snapContainer) snapChildren.Add(c);

                if (snapChildren.Count > 0)
                {
                    snapChildren[0].gameObject.SetActive(false);
                    for (int i = 1; i < snapChildren.Count; i++)
                        DestroyImmediate(snapChildren[i].gameObject);
                }
            }

            _saveItemTemplate.SetActive(false);

            for (int i = 1; i < children.Count; i++)
                DestroyImmediate(children[i].gameObject);
        }

        // ── Button handlers ─────────────────────────────────────────────

        void OnCreateSave()
        {
            string saveName = InputText();
            if (string.IsNullOrEmpty(saveName))
                saveName = $"Save_{UnityEngine.Random.Range(1, 10000)}";

            _currentSave    = _saveManager.CreateSave(saveName);
            _selectedSaveId = _currentSave.Identifier;

            SnapshotMetadata snap = AddSnapshotToCurrentSave("Initial Save");
            _selectedSnapshotId  = snap?.Identifier;

            Log($"Created save '{saveName}'");
            RefreshList();
        }

        void OnAddSnapshot()
        {
            if (_currentSave == null)
            {
                Log("No save selected — create or select a save first.");
                return;
            }

            string label = InputText();
            if (string.IsNullOrEmpty(label)) label = $"Turn {_turnNumber}";

            SnapshotMetadata snap = AddSnapshotToCurrentSave(label);
            _selectedSnapshotId  = snap?.Identifier;

            Log($"Added snapshot '{label}' to '{_currentSave.Name}'");
            RefreshList();
        }

        void OnLoadSave()
        {
            if (_currentSave == null) { Log("No save selected."); return; }
            if (string.IsNullOrEmpty(_selectedSnapshotId)) { Log("No snapshot selected."); return; }

            Snapshot snap = _currentSave.LoadSnapshot(_selectedSnapshotId);
            if (snap == null) { Log("Load failed."); return; }

            GameState state = JsonUtility.FromJson<GameState>(snap.SnapshotData);
            if (state != null) RestoreGameState(state, snap.ScreenshotPNG);

            SnapshotMetadata meta = _currentSave.Snapshots
                .FirstOrDefault(s => s.Identifier == _selectedSnapshotId);
            Log($"Loaded '{meta?.Name ?? "Unnamed"}' from '{_currentSave.Name}'");
            UpdateDetails();
        }

        void OnDeleteSave()
        {
            if (_currentSave == null) { Log("No save selected."); return; }

            int snapshotCount = _currentSave.SnapshotCount;

            if (snapshotCount <= 1 || string.IsNullOrEmpty(_selectedSnapshotId))
            {
                string name = _currentSave.Name;
                _currentSave.Delete();
                _currentSave        = null;
                _selectedSaveId     = null;
                _selectedSnapshotId = null;
                Log($"Deleted save '{name}'");
            }
            else
            {
                _currentSave.DeleteSnapshot(_selectedSnapshotId);
                _selectedSnapshotId = _currentSave.LatestSnapshot?.Identifier;
                Log($"Deleted snapshot from '{_currentSave.Name}'");
            }

            RefreshList();
        }

        void OnOpenFolder()
        {
            string path = _saveManager.RootSavesFolder;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Application.OpenURL("file://" + path);
        }

        // ── Save helpers ────────────────────────────────────────────────

        /// <summary>Captures current game state and adds a snapshot to the selected save.</summary>
        SnapshotMetadata AddSnapshotToCurrentSave(string label)
        {
            GameState state  = BuildGameState();
            byte[]    imgPng = _gameTexture?.EncodeToPNG();

            return _currentSave.AddSnapshot(
                snapshotData:  JsonUtility.ToJson(state),
                saveType:      SaveType.ManualSave,
                name:          label,
                screenshotPNG: imgPng,
                metadata: new Dictionary<string, string>
                {
                    { MetaKeyPlayTime, ((int)_playtimeSeconds).ToString() },
                    { "turn",          _turnNumber.ToString() }
                });
        }

        GameState BuildGameState()
        {
            var hex = new string[_palette?.Length ?? 0];
            if (_palette != null)
                for (int i = 0; i < _palette.Length; i++)
                    hex[i] = "#" + ColorUtility.ToHtmlStringRGB(_palette[i]);

            return new GameState
            {
                turnNumber      = _turnNumber,
                paletteHex      = hex,
                playtimeSeconds = _playtimeSeconds
            };
        }

        // ── List management ─────────────────────────────────────────────

        void RefreshList()
        {
            ClearList();

            List<SaveFile> allSaves = _saveManager.GetAllSaves();

            // If the previously selected save was deleted, clear the selection.
            if (_currentSave != null && allSaves.All(s => s.Identifier != _currentSave.Identifier))
            {
                _currentSave        = null;
                _selectedSaveId     = null;
                _selectedSnapshotId = null;
            }

            foreach (var save in allSaves)
            {
                bool isSelected = save.Identifier == _selectedSaveId;
                SpawnSaveItem(save, isSelected);
            }

            UpdateDetails();
        }

        void SpawnSaveItem(SaveFile save, bool isSelected)
        {
            if (_saveItemTemplate == null) return;

            GameObject go = Instantiate(_saveItemTemplate, saveListContent);
            go.SetActive(true);

            SaveListItem item = go.GetComponent<SaveListItem>();
            if (item == null) return;

            item.Setup(save, _saveManager, OnItemSelected, isSelected);

            if (isSelected && !string.IsNullOrEmpty(_selectedSnapshotId))
                item.SetSnapshotSelected(_selectedSnapshotId);

            _listItems.Add(item);
        }

        void ClearList()
        {
            foreach (var item in _listItems)
                if (item != null) Destroy(item.gameObject);
            _listItems.Clear();
        }

        void OnItemSelected(string type, object data)
        {
            if (type == "save")
            {
                var save        = (SaveFile)data;
                _currentSave    = _saveManager.GetSave(save.Identifier);
                _selectedSaveId = _currentSave?.Identifier;
                _selectedSnapshotId = _currentSave?.LatestSnapshot?.Identifier;
                RefreshList();
            }
            else if (type == "snapshot")
            {
                var snap            = (SnapshotMetadata)data;
                _selectedSnapshotId = snap.Identifier;

                foreach (var item in _listItems)
                    item.SetSnapshotSelected(_selectedSnapshotId);
            }
        }

        // ── Utilities ───────────────────────────────────────────────────

        string InputText() => saveNameInput != null ? saveNameInput.text.Trim() : string.Empty;

        void Log(string msg) => Debug.Log($"[SaveGameDemo] {msg}");

        // ── Data class ──────────────────────────────────────────────────

        [Serializable]
        private class GameState
        {
            public int      turnNumber;
            public string[] paletteHex;
            public float    playtimeSeconds;
        }
    }
}












































