using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SaveGame;

namespace Laubrary.Demos
{
    public class SaveGameDemo : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text saveListText;
        [SerializeField] private Text currentSaveText;
        [SerializeField] private InputField saveNameInput;
        [SerializeField] private InputField snapshotNameInput;
        [SerializeField] private RawImage thumbnailDisplay;

        [Header("Settings")]
        [SerializeField] private int maxSnapshots = 5;
        [SerializeField] private bool debugMode = true;

        private SaveManager _saveManager;
        private GameSaveFile _currentSave;
        private int _playTimeSeconds = 0;
        private Vector3 _playerPosition = Vector3.zero;
        private int _playerHealth = 100;
        private int _playerLevel = 1;

        void Start()
        {
            _saveManager = new SaveManager
            {
                DebugMode = debugMode,
                MaxSnapshotsPerSave = maxSnapshots,
                DefaultGameVersion = "1.0.0",
                DefaultSaveFormatVersion = "1.0"
            };

            UpdateStatus("SaveGame Demo initialized. Ready to create or load saves.");
            RefreshSaveList();
        }

        void Update()
        {
            _playTimeSeconds += (int)Time.deltaTime;

            _playerPosition += new Vector3(
                Random.Range(-0.1f, 0.1f),
                Random.Range(-0.1f, 0.1f),
                Random.Range(-0.1f, 0.1f)
            );
        }

        public void CreateNewSave()
        {
            string saveName = string.IsNullOrEmpty(saveNameInput.text) ? "DemoSave" : saveNameInput.text;

            var worldSettings = new
            {
                worldSeed = Random.Range(1000, 9999),
                difficulty = "Normal",
                mapSize = 100
            };

            string worldDataJson = JsonUtility.ToJson(worldSettings);

            _currentSave = _saveManager.CreateSaveFile(saveName, worldData: worldDataJson);

            _currentSave.MetadataProperties["difficulty"] = "Normal";
            _currentSave.MetadataProperties["characterClass"] = "Warrior";
            _currentSave.MetadataProperties["campaign"] = "Demo Campaign";

            UpdateStatus($"Created new save: {saveName}");
            UpdateCurrentSaveDisplay();
        }

        public void AddSnapshot()
        {
            if (_currentSave == null)
            {
                UpdateStatus("ERROR: No save file loaded. Create or load a save first.");
                return;
            }

            string snapshotName = string.IsNullOrEmpty(snapshotNameInput.text)
                ? $"Snapshot {_currentSave.Snapshots.Count + 1}"
                : snapshotNameInput.text;

            var gameState = new
            {
                playerPosition = _playerPosition,
                health = _playerHealth,
                level = _playerLevel,
                inventory = new[] { "Sword", "Shield", "Potion" }
            };

            Texture2D screenshot = CaptureSimpleScreenshot();

            _saveManager.AddSnapshot(
                _currentSave,
                snapshotData: JsonUtility.ToJson(gameState),
                saveType: SaveType.ManualSave,
                playTimeInSeconds: _playTimeSeconds,
                playerDefinedName: snapshotName,
                screenshotPNG: screenshot?.EncodeToPNG(),
                metadata: new Dictionary<string, string>
                {
                    {"location", "Demo Level"},
                    {"questProgress", Random.Range(0, 100).ToString()}
                }
            );

            UpdateStatus($"Added snapshot: {snapshotName}");
            UpdateCurrentSaveDisplay();

            snapshotNameInput.text = "";
        }

        public void SaveToDisk()
        {
            if (_currentSave == null)
            {
                UpdateStatus("ERROR: No save file to save.");
                return;
            }

            Texture2D saveThumb = CaptureSimpleScreenshot();
            if (saveThumb != null)
            {
                _currentSave.ThumbnailPNG = saveThumb.EncodeToPNG();
            }

            _saveManager.Save(_currentSave);

            UpdateStatus($"Saved '{_currentSave.GameName}' to disk. Files created in:\n{SavePaths.RootSavesFolder}");
            RefreshSaveList();
        }

        public void LoadSave()
        {
            string saveName = string.IsNullOrEmpty(saveNameInput.text) ? "DemoSave" : saveNameInput.text;

            _currentSave = _saveManager.Load(saveName);

            if (_currentSave != null)
            {
                UpdateStatus($"Loaded save: {saveName}");
                UpdateCurrentSaveDisplay();

                if (_currentSave.LatestSnapshot != null)
                {
                    var gameState = JsonUtility.FromJson<GameState>(_currentSave.LatestSnapshot.SnapshotData);
                    _playerPosition = gameState.playerPosition;
                    _playerHealth = gameState.health;
                    _playerLevel = gameState.level;
                    _playTimeSeconds = _currentSave.LatestSnapshot.PlayTimeInSeconds;
                }
            }
            else
            {
                UpdateStatus($"ERROR: Could not load save '{saveName}'");
            }
        }

        public void DeleteSave()
        {
            string saveName = string.IsNullOrEmpty(saveNameInput.text) ? "DemoSave" : saveNameInput.text;

            _saveManager.Delete(saveName);
            UpdateStatus($"Deleted save: {saveName}");

            if (_currentSave != null && _currentSave.GameName == saveName)
            {
                _currentSave = null;
                UpdateCurrentSaveDisplay();
            }

            RefreshSaveList();
        }

        public void RefreshSaveList()
        {
            List<SaveFileMetadata> allSaves = _saveManager.GetAllSaveMetadata();

            if (saveListText != null)
            {
                string listText = $"=== SAVE FILES ({allSaves.Count}) ===\n\n";

                foreach (var meta in allSaves)
                {
                    listText += $"<b>{meta.GameName}</b>\n";
                    listText += $"  Created: {meta.CreatedAt:yyyy-MM-dd HH:mm:ss}\n";
                    listText += $"  Modified: {meta.LastModified:yyyy-MM-dd HH:mm:ss}\n";
                    listText += $"  Snapshots: {meta.SnapshotCount}\n";

                    if (meta.MetadataProperties.Count > 0)
                    {
                        listText += "  Metadata:\n";
                        foreach (var kvp in meta.MetadataProperties)
                        {
                            listText += $"    {kvp.Key}: {kvp.Value}\n";
                        }
                    }

                    listText += "\n";
                }

                saveListText.text = listText;
            }
        }

        public void SimulateGameplay()
        {
            _playerHealth = Random.Range(50, 100);
            _playerLevel = Random.Range(1, 10);
            _playTimeSeconds += Random.Range(300, 1800);

            UpdateStatus("Simulated gameplay. Health, level, and playtime changed.");
            UpdateCurrentSaveDisplay();
        }

        private void UpdateCurrentSaveDisplay()
        {
            if (currentSaveText == null) return;

            if (_currentSave == null)
            {
                currentSaveText.text = "No save loaded";
                return;
            }

            string text = $"=== CURRENT SAVE ===\n\n";
            text += $"<b>Name:</b> {_currentSave.GameName}\n";
            text += $"<b>Created:</b> {_currentSave.CreatedAt:yyyy-MM-dd HH:mm:ss}\n";
            text += $"<b>Modified:</b> {_currentSave.LastModified:yyyy-MM-dd HH:mm:ss}\n";
            text += $"<b>Version:</b> {_currentSave.GameVersion}\n\n";

            if (_currentSave.MetadataProperties.Count > 0)
            {
                text += "<b>Metadata:</b>\n";
                foreach (var kvp in _currentSave.MetadataProperties)
                {
                    text += $"  {kvp.Key}: {kvp.Value}\n";
                }
                text += "\n";
            }

            text += $"<b>Snapshots ({_currentSave.Snapshots.Count}):</b>\n";
            foreach (var snapshot in _currentSave.Snapshots)
            {
                text += $"  • {snapshot.PlayerDefinedName ?? snapshot.Identifier}\n";
                text += $"    Time: {snapshot.Timestamp:HH:mm:ss}\n";
                text += $"    Type: {snapshot.SaveType}\n";
                text += $"    Play Time: {snapshot.PlayTimeInSeconds}s\n";
            }

            text += $"\n<b>Current Game State:</b>\n";
            text += $"  Position: {_playerPosition}\n";
            text += $"  Health: {_playerHealth}\n";
            text += $"  Level: {_playerLevel}\n";
            text += $"  Play Time: {_playTimeSeconds}s\n";

            currentSaveText.text = text;
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = $"[{System.DateTime.Now:HH:mm:ss}] {message}";
            }
            Debug.Log($"SaveGameDemo: {message}");
        }

        private Texture2D CaptureSimpleScreenshot()
        {
            Texture2D screenshot = new Texture2D(256, 256);

            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    Color color = new Color(
                        Random.Range(0.2f, 0.8f),
                        Random.Range(0.2f, 0.8f),
                        Random.Range(0.2f, 0.8f)
                    );
                    screenshot.SetPixel(x, y, color);
                }
            }

            screenshot.Apply();
            return screenshot;
        }

        void OnDestroy()
        {
            _saveManager?.ClearThumbnailCache();
        }

        [System.Serializable]
        private class GameState
        {
            public Vector3 playerPosition;
            public int health;
            public int level;
            public string[] inventory;
        }
    }
}
