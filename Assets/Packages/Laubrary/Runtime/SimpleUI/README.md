# SimpleUI - POCO to TextMeshPro Auto-Binding System

A lightweight, deterministic UI binding system for Unity that automatically maps POCO (Plain Old CLR Objects) data to TextMeshPro UI components.

## Features

✓ Generic root binder with type-safe POCO binding  
✓ Attribute-based and convention-based resolution  
✓ Deterministic GameObject and component resolution  
✓ Automatic two-way binding for interactive components  
✓ Comprehensive diagnostics with suggested fixes  
✓ Editor inspector integration  
✓ TMP-only (TextMeshPro components)  
✓ Fail-loud with detailed error reporting  

## Quick Start

### 1. Define Your POCO

```csharp
using Laubrary.SimpleUI;

[SimpleUI]
public class PlayerData
{
    public string playerName;
    public int health;
    public float stamina;
    public bool isDead;
    public Sprite avatar;
    public PlayerRank rank;
    
    [SimpleUIPath("UI/HUD/ExperienceBar")]
    [SimpleUIFormat("XP: {0}")]
    public int experience;
    
    [SimpleUIIgnore]
    public float internalTimer;
}

public enum PlayerRank { Novice, Veteran, Master }
```

### 2. Create UI Hierarchy

Create GameObjects in your scene that match your POCO field names:
```
Canvas
├── playerName (TMP_Text)
├── health (TMP_Text)
├── stamina (Slider)
├── isDead (Toggle)
├── avatar (Image)
├── rank (TMP_Dropdown)
└── UI
    └── HUD
        └── ExperienceBar (TMP_Text)
```

### 3. Add SimpleUIView Component

```csharp
public class PlayerUIController : MonoBehaviour
{
    private SimpleUIView<PlayerData> _view;
    
    void Start()
    {
        _view = GetComponent<SimpleUIView<PlayerData>>();
        
        var data = new PlayerData 
        { 
            playerName = "Hero",
            health = 100,
            stamina = 75.5f,
            isDead = false,
            rank = PlayerRank.Veteran,
            experience = 1250
        };
        
        _view.UpdateUI(data);
    }
    
    void UpdateStats()
    {
        // Modify data
        _view.pocoData.health = 50;
        
        // Refresh UI
        _view.UpdateUI(_view.pocoData);
    }
}
```

## Attributes

### `[SimpleUI]` - Class Level

Configure binding behavior for the entire POCO. Properties can be set directly on the attribute:

```csharp
[SimpleUI]
public class MyData 
{ 
    // Default configuration:
    // bindAll = true
    // nameMatching = NameMatchMode.Exact
    // maxSearchDepth = 3
    // multipleMatchBehavior = MultiMatchMode.Fail
    // allowNullPoco = false
}
```

**Available Properties:**
- `bindAll` (bool) - Bind all fields by default
- `nameMatching` (NameMatchMode) - Name matching mode
- `maxSearchDepth` (int) - BFS search depth limit
- `multipleMatchBehavior` (MultiMatchMode) - How to handle multiple matches
- `allowNullPoco` (bool) - Allow null POCO in UpdateUI

**NameMatchMode:**
- `Exact` - Field name must match GameObject name exactly
- `CaseInsensitive` - Ignore case differences
- `Loose` - Ignore case, underscores, spaces, hyphens

**MultiMatchMode:**
- `Fail` - Throw exception if multiple GameObjects match (recommended)
- `UseFirst` - Use first match and log warning

### `[SimpleUIPath]` - Field Level

Override GameObject resolution with explicit path:

```csharp
[SimpleUIPath("UI/Panels/HealthBar")]
public int health;
```

Path is relative to the GameObject with `SimpleUIView` component.

### `[SimpleUIComponent]` - Field Level

Specify exact component type to use:

```csharp
[SimpleUIComponent(typeof(TMP_Text))]
public int score;  // Force TMP_Text even if Slider is present
```

### `[SimpleUIFormat]` - Field Level

Format string for text display:

```csharp
[SimpleUIFormat("HP: {0}/100")]
public int health;

[SimpleUIFormat("Gold: {0:N0}")]
public int gold;

[SimpleUIFormat("{0:F2}%")]
public float accuracy;
```

### `[SimpleUIIgnore]` - Field Level

Exclude field from binding:

```csharp
[SimpleUIIgnore]
public float cachedValue;
```

## Type Mapping

SimpleUI automatically maps POCO field types to compatible TMP components:

| Field Type | Compatible Components | Binding Mode |
|------------|----------------------|--------------|
| `string` | TMP_Text, TMP_InputField | One-way / Two-way |
| `int`, `float` | TMP_Text, Slider | One-way / Two-way |
| `bool` | TMP_Text, Toggle | One-way / Two-way |
| `Sprite` | Image | One-way |
| `Color` | Image, TMP_Text | One-way |
| `enum` | TMP_Dropdown (auto-populated) | Two-way |

**Two-way binding** automatically syncs UI input back to POCO for:
- TMP_InputField
- Slider
- Toggle
- TMP_Dropdown

## Resolution Pipeline

SimpleUI resolves bindings in this order:

1. **Field Selection**
   - Include all public fields
   - Skip fields with `[SimpleUIIgnore]`
   - Skip fields if `bindAll=false` and no binding attributes

2. **GameObject Resolution**
   - If `[SimpleUIPath]` exists: Use exact path
   - Otherwise: Search by field name
     - Check direct children first
     - Then BFS search up to `maxSearchDepth`
     - Apply `nameMatching` mode
     - Handle multiple matches per `multipleMatchBehavior`

3. **Component Resolution**
   - If `[SimpleUIComponent]` exists: Use specified type
   - Otherwise: Type inference
     - Get compatible components for field type
     - Fail if zero compatible components found
     - Fail if multiple compatible components found (ambiguous)

4. **Delegate Creation**
   - Create one-way setter delegate
   - Create two-way binding setup if component supports it
   - Store cleanup actions for event unsubscription

## Diagnostics

### Console Output

SimpleUI logs detailed binding reports on Awake:

```
[SimpleUI] PlayerData binding report:
Summary: 6 Success, 0 Errors
✓ playerName → playerName (TMP_Text) [Strong]
✓ health → health (TMP_Text) [Strong]
✓ stamina → stamina (Slider) [Weak]
✓ rank → rank (TMP_Dropdown) [Weak]
✓ experience → UI/HUD/ExperienceBar (TMP_Text) [Explicit]
```

### Editor Inspector

In Play mode, the custom inspector shows:
- Per-field binding status
- GameObject paths
- Component types
- Binding modes (One-way / Two-way)
- Confidence levels (Explicit / Strong / Weak)
- Error messages with suggested fixes

### Error Messages

When bindings fail, you get actionable error messages:

```
✗ healthBar: GameObject at path 'UI/HealthBar' not found
  → Create GameObject at path 'UI/HealthBar' or verify path is correct

✗ score: Ambiguous: GameObject 'Canvas' has 2 compatible components: TMP_Text, TMP_InputField
  → Add [SimpleUIComponent(typeof(...))] to specify which component to use

✗ playerIcon: No compatible component on 'playerIcon' for field type Sprite
  → Add one of these components: Image
```

## Best Practices

1. **Use `[SimpleUIPath]` for complex hierarchies** to avoid ambiguity
2. **Set `multipleMatchBehavior: MultiMatchMode.Fail`** to catch duplicate names early
3. **Use `NameMatchMode.CaseInsensitive`** for flexibility
4. **Add `[SimpleUIComponent]`** when GameObject has multiple compatible components
5. **Keep UI hierarchy shallow** (within maxSearchDepth)
6. **Use meaningful GameObject names** that match field names
7. **Call `UpdateUI()` manually** when data changes (v1 has no automatic tracking)

## Limitations (v1)

- TMP-only (no legacy UI Text support)
- No nested POCO support
- No automatic change tracking
- No slider value display binding
- Manual UpdateUI workflow required
- Per-instance resolution (re-resolves on each Awake)

## Manual Setup Required

**Add TextMeshPro reference to assembly definition:**

1. Select `/Assets/Packages/Laubrary/Runtime/SimpleUI/SimpleUI.asmdef`
2. In Inspector → Assembly Definition References → click **+**
3. Add `Unity.TextMeshPro`
4. Click **Apply**

## Example: Complete Setup

```csharp
// POCO definition
[SimpleUI(bindAll: true)]
public class ShopItemData
{
    public string itemName;
    public int price;
    public Sprite icon;
    public bool inStock;
}

// Controller
public class ShopItemUI : MonoBehaviour
{
    private SimpleUIView<ShopItemData> _view;
    
    void Start()
    {
        _view = GetComponent<SimpleUIView<ShopItemData>>();
        
        var item = new ShopItemData
        {
            itemName = "Health Potion",
            price = 50,
            icon = Resources.Load<Sprite>("Icons/Potion"),
            inStock = true
        };
        
        _view.UpdateUI(item);
    }
    
    public void OnPurchase()
    {
        if (_view.pocoData.inStock)
        {
            Debug.Log($"Purchased {_view.pocoData.itemName}");
            _view.pocoData.inStock = false;
            _view.UpdateUI(_view.pocoData);
        }
    }
}
```

UI Hierarchy:
```
ShopItemPanel (with SimpleUIView<ShopItemData>)
├── itemName (TMP_Text)
├── price (TMP_Text)
├── icon (Image)
└── inStock (Toggle)
```

---

**SimpleUI** - Built for deterministic, fail-loud POCO→UI binding in Unity 6.
