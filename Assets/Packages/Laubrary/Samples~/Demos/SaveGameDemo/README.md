# SaveGame Demo

This demo showcases the **SaveGame** system - a generic, game-agnostic save/load system with sidecar metadata, lazy-loading, and thumbnail support.

## Demo Overview

A fully interactive demonstration with three panels:
- **Left Panel (Controls)**: Create and manage save files, add snapshots, open save folder
- **Middle Panel (Save List)**: Scrollable list of all saves with expandable snapshots and thumbnails
- **Right Panel (Details)**: Selected save/snapshot details with full metadata and thumbnail preview

The demo features:
- **Mock Data Generation**: Automatically generates random world data and game states
- **Thumbnail System**: 128x128 blocky pixel art thumbnails (4px/8px/16px blocks, 2-5 random colors)
- **Expandable List**: Click arrows to expand/collapse snapshots for each save
- **Selection System**: Click any save or snapshot to view detailed information
- **Fast Metadata Browsing**: List reads only sidecar files (no zip extraction)
- **Folder Access**: One-click button to open the save files directory

## Features Demonstrated

- ✅ **Create Save Files** with mock world data and thumbnails
- ✅ **Add Snapshots** with mock game state and screenshot thumbnails  
- ✅ **Visual List** showing all saves and their snapshots with thumbnails
- ✅ **Expandable Hierarchy** - expand/collapse snapshots per save
- ✅ **Selection & Details** - click to view full metadata
- ✅ **Save to Disk** (creates `.gamesave` + `.gamesave.meta` sidecar)
- ✅ **Load from Disk** with full state restoration
- ✅ **Fast Browsing** via sidecar metadata (no zip extraction)
- ✅ **Delete Operations** for entire save files
- ✅ **Metadata Dictionaries** for custom searchable properties
- ✅ **Thumbnail Serialization** - images saved and loaded with saves
- ✅ **Open Save Folder** button for easy file access

## Quick Start

1. Open the scene: `/Assets/Demos/SaveGameDemo/SaveGameDemo.unity`
2. Enter Play mode
3. The demo shows three panels:
   - **Left Panel**: Controls for creating/loading saves  
   - **Middle Panel**: Scrollable list of saves with thumbnails
   - **Right Panel**: Detailed information for selected save/snapshot

### How to Use

**Create Your First Save:**
1. Type a name in the input field (or leave blank for "DemoSave")
2. Click **Create Save** - creates a save file with mock world data and a blocky pixel thumbnail

**Add Snapshots:**
1. Make sure you've created or loaded a save first
2. Click **Add Snapshot** - adds a checkpoint with mock game state and thumbnail
3. The middle panel updates to show your new snapshot

**Browse and Explore:**
1. Click the arrow (▶) next to a save name to expand its snapshots
2. Click on any save or snapshot to view full details in the right panel
3. Thumbnails show at different sizes: 128x128 in details, 64x64 for saves, 48x48 for snapshots

**Load a Save:**
1. Type the save name in the input field
2. Click **Load Save** - loads the save into memory for adding more snapshots

**Delete a Save:**
1. Type the save name
2. Click **Delete Save** - removes the entire save file and all snapshots

**Other Actions:**
- Click **Refresh List** to reload from disk
- Click **Open Save Folder** to browse files in your file system

### Visual Layout

The demo presents a three-panel interface:

```
┌──────────────┬───────────────────────┬──────────────────┐
│  Controls    │    Save Files         │    Details       │
├──────────────┼───────────────────────┼──────────────────┤
│ [Input]      │ ▶ [🖼] MySave         │  [128x128 IMG]   │
│ Create Save  │   3 snapshots         │                  │
│ Add Snapshot │   Modified: 3:30 PM   │  MySave          │
│ Load Save    │                       │  Created: ...    │
│ Delete Save  │ ▼ [🖼] TestSave       │  Modified: ...   │
│ Refresh      │   2 snapshots         │  Snapshots: 3    │
│ Open Folder  │   Modified: 2:15 PM   │                  │
│              │   ⤷ [🖼] Checkpoint 1  │  Properties:     │
│              │   ⤷ [🖼] Checkpoint 2  │  • class: Warrior│
│              │                       │  • mode: Demo    │
└──────────────┴───────────────────────┴──────────────────┘
```

**Thumbnails:**
- Generated as 128x128 blocky pixel art
- Random 4px, 8px, or 16px block sizes
- 2-5 random colors per thumbnail
- Saved with each save file and snapshot
- Displayed at different sizes throughout UI

## Key Concepts

### Mock Data System

The demo automatically generates realistic test data:

**World Data (per save):**
- Random seed (1000-9999)
- Difficulty: Easy, Normal, Hard, or Nightmare
- Player Class: Warrior, Mage, Rogue, Cleric, or Ranger
- World Name: "World_XXX"

**Game State (per snapshot):**
- Player Name: Hero, Adventurer, Champion, etc.
- Health: 50-100
- Level: 1-20
- Location: Forest, Castle, Dungeon, Village, Mountain, or Cave
- Gold: 100-10,000
- Experience: 0-1,000

### Thumbnail Generation

Thumbnails are procedurally generated blocky pixel art:
- Size: 128x128 pixels
- Block sizes: Randomly 4px, 8px, or 16px
- Colors: 2-5 random colors per image
- Saved as PNG with save files
- Automatically serialized/deserialized

### SaveGame Architecture

```
Saves/
├── MyGame.gamesave.meta      (Sidecar - fast access)
│   └── Contains: metadata, snapshot list, properties
└── MyGame.gamesave           (Zip archive)
    ├── metadata.json         (Save metadata)
    ├── WorldData.json        (Optional shared data)
    └── Snapshots/
        ├── snapshot_XXX.meta (Snapshot metadata)
        ├── snapshot_XXX.data (SnapshotData)
        ├── snapshot_XXX.png  (Optional screenshot)
        └── ...
```

### Data Separation

- **WorldData**: Shared across all snapshots (e.g., world seed, difficulty, player class)
- **SnapshotData**: Specific to each snapshot (e.g., player state, health, location)
- **MetadataProperties**: Quick-access searchable data in sidecar file
- **Thumbnails**: PNG images serialized as byte arrays, saved in save files and snapshots

## Code Example

The demo shows how to use the SaveGame system with thumbnails:

```csharp
// Initialize manager
SaveManager manager = new SaveManager
{
    DebugMode = true,
    MaxSnapshotsPerSave = 10,
    DefaultGameVersion = "1.0.0"
};

// Generate a thumbnail (128x128 blocky pixel art)
byte[] thumbnail = GenerateMockThumbnail();

// Create save with world data and thumbnail
var worldData = new { seed = 12345, difficulty = "Normal", playerClass = "Warrior" };
GameSaveFile save = manager.CreateSaveFile("MySave", JsonUtility.ToJson(worldData));
save.MetadataProperties["gameMode"] = "Demo";
save.ThumbnailPNG = thumbnail;

// Add snapshot with game state and screenshot
var gameState = new { health = 100, level = 5, location = "Castle" };
byte[] screenshot = GenerateMockThumbnail();

manager.AddSnapshot(
    save,
    snapshotData: JsonUtility.ToJson(gameState),
    saveType: SaveType.ManualSave,
    playerDefinedName: "Checkpoint 1",
    screenshotPNG: screenshot,
    metadata: new Dictionary<string, string> {
        {"location", "Castle"},
        {"health", "100"}
    }
);

// Save to disk
manager.Save(save);

// Fast browse (reads sidecar only - no zip extraction)
List<SaveFileMetadata> allSaves = manager.GetAllSaveMetadata();

// Load thumbnails on demand (lazy loading with cache)
Texture2D saveThumb = manager.GetSaveFileThumbnail("MySave");
Texture2D snapshotThumb = manager.GetSnapshotThumbnail("MySave", "snapshot_001");

// Load full save
GameSaveFile loaded = manager.Load("MySave");
var state = JsonUtility.FromJson<MyState>(loaded.LatestSnapshot.SnapshotData);
```

## What You'll Learn

- ✅ How to create save files with custom world data
- ✅ How to add snapshots with game state checkpoints
- ✅ How to generate and save thumbnails with saves
- ✅ How to use metadata for quick filtering and display
- ✅ Fast browsing with sidecar files (no zip extraction)
- ✅ Lazy-loading thumbnails from zip archives
- ✅ Loading and restoring complete game state
- ✅ Managing save file lifecycles (create, load, delete)
- ✅ Opening save folder for debugging
- ✅ Building interactive save/load UIs

## Technical Implementation

### Thumbnail System

The demo includes a complete thumbnail generation system:

```csharp
byte[] GenerateMockThumbnail()
{
    int size = 128;
    int pixelSize = Random.choice(4, 8, 16);  // Block size
    int colorCount = Random.Range(2, 6);      // 2-5 colors
    
    // Generate random color palette
    Color[] palette = new Color[colorCount];
    for (int i = 0; i < colorCount; i++)
        palette[i] = new Color(Random.value, Random.value, Random.value);
    
    // Create blocky pixel art texture
    Texture2D texture = new Texture2D(size, size);
    // ... fill with blocks of random colors
    
    // Encode as PNG and return bytes
    return texture.EncodeToPNG();
}
```

**Key Features:**
- Procedurally generated unique thumbnails
- Saved as PNG byte arrays with save files
- Automatically serialized/deserialized by SaveManager
- Lazy-loaded from zip archives (cached after first load)
- Displayed at multiple sizes in UI (128x128, 64x64, 48x48)

### UI Architecture

The demo uses a three-panel responsive layout:

**Left Panel (30% width):**
- Input field and action buttons
- VerticalLayoutGroup with LayoutElements
- Color-coded buttons for different actions

**Middle Panel (30% width):**
- ScrollRect with ContentSizeFitter
- Dynamic list of SaveListItem prefabs
- Expandable/collapsible snapshot hierarchy
- Thumbnails displayed at 64x64 and 48x48

**Right Panel (33% width):**
- Details display for selected item
- Large 128x128 thumbnail preview
- Scrollable metadata information
- Hidden when nothing is selected

## Performance Notes

- **Browse 100 saves**: ~500ms (reads sidecar files only)
- **Load single save**: ~100-500ms (depends on data size)
- **Metadata access**: ~1-5ms per save (no zip extraction)
- **Thumbnail loading**: ~10-20ms first load, then cached
- **List refresh**: Instant for metadata, lazy-loads thumbnails

## File Locations

- **Save files**: `Application.persistentDataPath/Saves/`
  - Windows: `%USERPROFILE%\AppData\LocalLow\CompanyName\ProductName\Saves\`
  - Mac: `~/Library/Application Support/CompanyName/ProductName/Saves/`
  - Linux: `~/.config/unity3d/CompanyName/ProductName/Saves/`

## Additional Resources

For complete documentation and advanced usage:
- `/Assets/Packages/Laubrary/Runtime/SaveGame/` - Source code
- `SaveGame System Refactoring` page - Architecture details
- `/Assets/Demos/SaveGameDemo/PREFAB_SETUP.md` - UI setup instructions
