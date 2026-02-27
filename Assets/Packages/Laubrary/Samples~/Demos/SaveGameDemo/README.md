# SaveGame Demo

This demo showcases the **SaveGame** system - a generic, game-agnostic save/load system with minimal sidecar metadata and lazy-loading capabilities.

## Features Demonstrated

- ✅ **Create Save Files** with optional WorldData
- ✅ **Add Snapshots** with game state data
- ✅ **Save to Disk** (creates .gamesave + .gamesave.meta sidecar)
- ✅ **Load from Disk** 
- ✅ **Fast Metadata Browsing** (reads sidecar files without unzipping)
- ✅ **Delete Saves** and snapshots
- ✅ **Metadata Properties** for quick-access custom data
- ✅ **Optional Thumbnails** for saves and snapshots

## How to Use

### UI Buttons

1. **Create New Save** - Creates a new save file with the name from the input field
2. **Add Snapshot** - Adds a new snapshot to the current save with current game state
3. **Save to Disk** - Writes the current save to disk (.gamesave zip file)
4. **Load Save** - Loads a save from disk by name
5. **Delete Save** - Deletes a save file completely
6. **Simulate Gameplay** - Changes game state (health, level, position)
7. **Refresh List** - Updates the list of all saves

### Workflow Example

1. Enter a save name (e.g., "MySave")
2. Click **Create New Save**
3. Click **Simulate Gameplay** to change game state
4. Enter a snapshot name (e.g., "Before Boss Fight")
5. Click **Add Snapshot**
6. Repeat steps 3-5 to create more snapshots
7. Click **Save to Disk**
8. Click **Refresh List** to see your save in the list
9. Try **Load Save** to restore the save

## Save File Structure

```
Saves/
├── MySave.gamesave.meta      (Lightweight sidecar - fast access)
└── MySave.gamesave           (Zip archive)
    ├── metadata.json         (Save metadata)
    ├── savefile_thumb.png    (Optional save thumbnail)
    ├── WorldData.json        (Optional shared data)
    └── Snapshots/
        ├── snapshot_001.meta (Snapshot metadata)
        ├── snapshot_001.data (Game state JSON)
        ├── snapshot_001.png  (Optional screenshot)
        └── ...
```

## Save Location

Saves are stored in:
- **Windows**: `%USERPROFILE%\AppData\LocalLow\CompanyName\ProductName\Saves\`
- **Mac**: `~/Library/Application Support/CompanyName/ProductName/Saves/`
- **Linux**: `~/.config/unity3d/CompanyName/ProductName/Saves/`

You can find the exact path in the status message after saving.

## Code Overview

### Creating a Save

```csharp
SaveManager saveManager = new SaveManager();
GameSaveFile save = saveManager.CreateSaveFile("MySave", worldData: myJson);
save.MetadataProperties["difficulty"] = "Hard";
```

### Adding Snapshots

```csharp
var gameState = new { health = 100, position = Vector3.zero };
saveManager.AddSnapshot(
    save,
    snapshotData: JsonUtility.ToJson(gameState),
    saveType: SaveType.ManualSave,
    playTimeInSeconds: 3600,
    playerDefinedName: "Checkpoint 1",
    screenshotPNG: screenshot?.EncodeToPNG(),
    metadata: new Dictionary<string, string> { {"location", "Castle"} }
);
```

### Saving and Loading

```csharp
// Save
saveManager.Save(save);

// Fast browse (no zip extraction)
List<SaveFileMetadata> allSaves = saveManager.GetAllSaveMetadata();

// Load
GameSaveFile loaded = saveManager.Load("MySave");
```

## Concepts

### WorldData
Optional string stored once per save file, shared across all snapshots. Use for:
- World seed
- Campaign settings
- Difficulty level
- Player profile data

### SnapshotData
Required string stored per snapshot. Use for:
- Player position/stats
- Inventory
- Quest progress
- Current enemies

### Metadata Properties
Dictionary<string, string> on both saves and snapshots for quick-access data without loading the full save.

## Performance

- **Fast Browsing**: ~500ms for 100 saves (reads sidecar files only)
- **Single Metadata**: ~1-5ms (no zip extraction)
- **Load Thumbnail**: ~10-20ms (from zip, then cached)
- **Full Load**: ~100-500ms (depends on data size)
