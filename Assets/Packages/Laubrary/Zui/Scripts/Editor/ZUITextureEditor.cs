// ZUITextureEditor.cs
// Pixel editor for creating monochrome white PNG textures (icons and patterns).
// ZUI custom textures are assumed to be monochrome white — tinting is applied at render time.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class ZUITextureEditor : ZUIWindow
{
    [MenuItem("Tools/ZUI/Texture Editor")]
    static void Open() => GetWindow<ZUITextureEditor>("ZUI Texture Editor");

    protected override string ConsumerSheetName => ZUI.EditorSheetConsumerName;
    protected override string RootBoxStyle => null;

    // ── Canvas ───────────────────────────────────────────────────────────────
    int _width = 16, _height = 16;
    Color[] _pixels;
    string _assetName = "new-icon";
    string _loadedAssetPath; // null = new, set = editing existing custom icon

    // ── Undo ─────────────────────────────────────────────────────────────────
    struct UndoEntry { public Color[] pixels; public int width, height; }
    List<UndoEntry> _undoStack = new List<UndoEntry>();
    int _undoIndex = -1;
    const int k_MaxUndo = 30;

    // ── Tools ────────────────────────────────────────────────────────────────
    enum Tool { Paint, Erase, Select, Stamp }
    Tool _tool = Tool.Paint;
    float _brushAlpha = 1f;
    int _eraserSize = 1;

    // ── Selection / Clipboard ────────────────────────────────────────────────
    bool _hasSelection;
    RectInt _selection;
    int _selStartX, _selStartY; // drag start for selection
    Color[] _clipboard;
    int _clipW, _clipH;

    // ── Stamp (ghost mode) ───────────────────────────────────────────────────
    // When clipboard is set and tool is Stamp, a ghost overlay follows the mouse.
    // Click to place. Arrow keys to move. R to rotate 90°. +/- to scale.
    int _stampX, _stampY;
    int _stampRotation; // 0, 90, 180, 270
    float _stampScale = 1f;

    // ── Preview ──────────────────────────────────────────────────────────────
    enum PreviewMode { Icon, Pattern }
    PreviewMode _previewMode = PreviewMode.Icon;
    Color _previewTint = Color.white;
    Color _editorBgColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    float _zoom = 12f;

    // ── Text ─────────────────────────────────────────────────────────────────
    string _textInput = "A";
    int _textSize = 8;

    // ── Init ─────────────────────────────────────────────────────────────────
    protected override void OnZUIEnable() { EnsureCanvas(); }

    void EnsureCanvas()
    {
        if (_pixels == null || _pixels.Length != _width * _height)
        {
            _pixels = new Color[_width * _height];
            ClearCanvas(false);
        }
    }

    void ClearCanvas(bool recordUndo = true)
    {
        if (recordUndo) PushUndo();
        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = new Color(1f, 1f, 1f, 0f);
        _hasSelection = false;
    }

    void ResizeCanvas(int newW, int newH)
    {
        PushUndo();
        var newPixels = new Color[newW * newH];
        for (int i = 0; i < newPixels.Length; i++)
            newPixels[i] = new Color(1f, 1f, 1f, 0f);
        int copyW = Mathf.Min(_width, newW), copyH = Mathf.Min(_height, newH);
        for (int y = 0; y < copyH; y++)
        for (int x = 0; x < copyW; x++)
            newPixels[y * newW + x] = _pixels[y * _width + x];
        _pixels = newPixels;
        _width = newW; _height = newH;
        _hasSelection = false;
    }

    // ── Undo system ──────────────────────────────────────────────────────────
    void PushUndo()
    {
        // Trim forward history
        if (_undoIndex < _undoStack.Count - 1)
            _undoStack.RemoveRange(_undoIndex + 1, _undoStack.Count - _undoIndex - 1);
        _undoStack.Add(new UndoEntry { pixels = (Color[])_pixels.Clone(), width = _width, height = _height });
        if (_undoStack.Count > k_MaxUndo)
            _undoStack.RemoveAt(0);
        _undoIndex = _undoStack.Count - 1;
    }

    void Undo()
    {
        if (_undoIndex <= 0) return;
        _undoIndex--;
        var entry = _undoStack[_undoIndex];
        _pixels = (Color[])entry.pixels.Clone();
        _width  = entry.width;
        _height = entry.height;
        Repaint();
    }

    void Redo()
    {
        if (_undoIndex >= _undoStack.Count - 1) return;
        _undoIndex++;
        var entry = _undoStack[_undoIndex];
        _pixels = (Color[])entry.pixels.Clone();
        _width  = entry.width;
        _height = entry.height;
        Repaint();
    }

    // ── OnGUI ────────────────────────────────────────────────────────────────
    protected override void OnZUI()
    {
        EnsureCanvas();
        HandleKeyboard();

        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(170f));
        DrawToolPanel();
        GUILayout.EndVertical();

        GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawCanvas();
        GUILayout.EndVertical();

        GUILayout.BeginVertical(GUILayout.Width(160f));
        DrawPreviewPanel();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    void HandleKeyboard()
    {
        // Scroll wheel: stamp scale (plain) or rotate (Ctrl)
        if (Event.current.type == EventType.ScrollWheel && _tool == Tool.Stamp && _clipboard != null)
        {
            float delta = Event.current.delta.y;
            if (Event.current.control)
            {
                _stampRotation = ((_stampRotation + (delta > 0 ? 90 : -90)) % 360 + 360) % 360;
            }
            else
            {
                _stampScale = Mathf.Clamp(_stampScale + (delta < 0 ? 0.5f : -0.5f), 0.5f, 4f);
            }
            Event.current.Use(); Repaint();
        }

        if (Event.current.type != EventType.KeyDown) return;
        if (Event.current.control && Event.current.keyCode == KeyCode.Z) { Undo(); Event.current.Use(); }
        if (Event.current.control && Event.current.keyCode == KeyCode.Y) { Redo(); Event.current.Use(); }
        if (Event.current.keyCode == KeyCode.Delete && _hasSelection) { PushUndo(); DeleteSelection(); Event.current.Use(); }
        if (_tool == Tool.Stamp && _clipboard != null)
        {
            if (Event.current.keyCode == KeyCode.R) { _stampRotation = (_stampRotation + 90) % 360; Event.current.Use(); Repaint(); }
            if (Event.current.keyCode == KeyCode.Equals || Event.current.keyCode == KeyCode.Plus)
                { _stampScale = Mathf.Min(4f, _stampScale + 0.5f); Event.current.Use(); Repaint(); }
            if (Event.current.keyCode == KeyCode.Minus)
                { _stampScale = Mathf.Max(0.5f, _stampScale - 0.5f); Event.current.Use(); Repaint(); }
        }
    }

    // ── Tool panel ───────────────────────────────────────────────────────────
    void DrawToolPanel()
    {
        EditorGUILayout.LabelField("Canvas", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        EditorGUIUtility.labelWidth = 20f;
        int newW = Mathf.Clamp(EditorGUILayout.IntField("W", _width, GUILayout.Width(52f)), 4, 64);
        int newH = Mathf.Clamp(EditorGUILayout.IntField("H", _height, GUILayout.Width(52f)), 4, 64);
        EditorGUIUtility.labelWidth = 0f;
        GUILayout.EndHorizontal();
        if (newW != _width || newH != _height) ResizeCanvas(newW, newH);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear", EditorStyles.miniButton)) ClearCanvas();
        if (GUILayout.Button("Undo", EditorStyles.miniButton)) Undo();
        if (GUILayout.Button("Redo", EditorStyles.miniButton)) Redo();
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Tool", EditorStyles.boldLabel);
        _tool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "Paint", "Erase", "Sel", "Stamp" }, EditorStyles.miniButton);

        if (_tool == Tool.Paint)
        {
            EditorGUIUtility.labelWidth = 40f;
            _brushAlpha = EditorGUILayout.Slider("Alpha", _brushAlpha, 0f, 1f);
            EditorGUIUtility.labelWidth = 0f;
        }
        if (_tool == Tool.Erase)
        {
            EditorGUIUtility.labelWidth = 40f;
            _eraserSize = EditorGUILayout.IntSlider("Size", _eraserSize, 1, 8);
            EditorGUIUtility.labelWidth = 0f;
        }
        if (_tool == Tool.Stamp && _clipboard != null)
        {
            EditorGUILayout.LabelField($"Stamp: {_clipW}x{_clipH}  R:{_stampRotation}°  S:{_stampScale:F1}x", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("R=rotate  +/-=scale  Click=place", EditorStyles.miniLabel);
        }

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledGroupScope(!_hasSelection))
        {
            if (GUILayout.Button("Cut", EditorStyles.miniButton)) { PushUndo(); CutSelection(); }
            if (GUILayout.Button("Copy", EditorStyles.miniButton)) CopySelection();
            if (GUILayout.Button("Del", EditorStyles.miniButton)) { PushUndo(); DeleteSelection(); }
        }
        using (new EditorGUI.DisabledGroupScope(_clipboard == null))
        {
            if (GUILayout.Button("Paste", EditorStyles.miniButton)) { _tool = Tool.Stamp; _stampRotation = 0; _stampScale = 1f; }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
        if (GUILayout.Button("From Icon...", EditorStyles.miniButton))
            ShowIconPickerPopover();

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("Text", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        _textInput = EditorGUILayout.TextField(_textInput, GUILayout.Width(60f));
        _textSize = EditorGUILayout.IntField(_textSize, GUILayout.Width(30f));
        if (GUILayout.Button("Stamp", EditorStyles.miniButton, GUILayout.Width(46f))) { PushUndo(); StampText(); }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        _zoom = EditorGUILayout.Slider("Zoom", _zoom, 4f, 24f);

        GUILayout.FlexibleSpace();

        // ── Save ─────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Save", EditorStyles.boldLabel);
        _assetName = EditorGUILayout.TextField("Name", _assetName);
        if (GUILayout.Button("Save to Data Folder", EditorStyles.miniButton))
            SaveAsset();
        if (!string.IsNullOrEmpty(_loadedAssetPath))
        {
            if (GUILayout.Button("Overwrite Original", EditorStyles.miniButton))
                SaveAsset(_loadedAssetPath);
        }
    }

    // ── Canvas drawing ───────────────────────────────────────────────────────
    void DrawCanvas()
    {
        var canvasRect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        float ps = _zoom;
        float totalW = _width * ps, totalH = _height * ps;
        float ox = canvasRect.x + (canvasRect.width - totalW) * 0.5f;
        float oy = canvasRect.y + (canvasRect.height - totalH) * 0.5f;

        // Mouse pixel coords
        var mp = Event.current.mousePosition;
        int mx = Mathf.FloorToInt((mp.x - ox) / ps);
        int my = _height - 1 - Mathf.FloorToInt((mp.y - oy) / ps);
        bool inCanvas = mx >= 0 && mx < _width && my >= 0 && my < _height;

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(canvasRect, _editorBgColor);

            // Pixels
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                var c = _pixels[y * _width + x];
                if (c.a > 0.01f)
                {
                    var r = new Rect(ox + x * ps, oy + (_height - 1 - y) * ps, ps, ps);
                    EditorGUI.DrawRect(r, new Color(_previewTint.r, _previewTint.g, _previewTint.b, c.a));
                }
            }

            // Grid
            var gridColor = new Color(1f, 1f, 1f, 0.05f);
            for (int x = 0; x <= _width; x++)
                EditorGUI.DrawRect(new Rect(ox + x * ps, oy, 1f, totalH), gridColor);
            for (int y = 0; y <= _height; y++)
                EditorGUI.DrawRect(new Rect(ox, oy + y * ps, totalW, 1f), gridColor);

            // Selection
            if (_hasSelection)
            {
                var selColor = new Color(0.3f, 0.6f, 1f, 0.25f);
                for (int y = _selection.y; y < _selection.y + _selection.height && y < _height; y++)
                for (int x = _selection.x; x < _selection.x + _selection.width && x < _width; x++)
                    EditorGUI.DrawRect(new Rect(ox + x * ps, oy + (_height - 1 - y) * ps, ps, ps), selColor);
            }

            // Ghost stamp preview (renders even when mouse is outside canvas)
            if (_tool == Tool.Stamp && _clipboard != null)
            {
                var ghostColor = new Color(0.5f, 0.8f, 1f, 0.35f);
                int sw = Mathf.RoundToInt(_clipW * _stampScale);
                int sh = Mathf.RoundToInt(_clipH * _stampScale);
                for (int cy = 0; cy < sh; cy++)
                for (int cx = 0; cx < sw; cx++)
                {
                    int srcX = Mathf.FloorToInt((float)cx / _stampScale);
                    int srcY = Mathf.FloorToInt((float)cy / _stampScale);
                    RotateCoords(ref srcX, ref srcY, _clipW, _clipH, _stampRotation);
                    if (srcX < 0 || srcX >= _clipW || srcY < 0 || srcY >= _clipH) continue;
                    if (_clipboard[srcY * _clipW + srcX].a < 0.01f) continue;
                    int dx = mx + cx, dy = my + cy;
                    // Draw ghost pixel at screen position even if outside canvas bounds
                    float gx = ox + dx * ps, gy = oy + (_height - 1 - dy) * ps;
                    EditorGUI.DrawRect(new Rect(gx, gy, ps, ps), ghostColor);
                }
            }
        }

        // ── Right-click context menu ─────────────────────────────────
        if (Event.current.type == EventType.ContextClick && canvasRect.Contains(mp))
        {
            var menu = new GenericMenu();
            if (_hasSelection) menu.AddItem(new GUIContent("Cut"), false, () => { PushUndo(); CutSelection(); });
            if (_hasSelection) menu.AddItem(new GUIContent("Copy"), false, CopySelection);
            if (_hasSelection) menu.AddItem(new GUIContent("Delete"), false, () => { PushUndo(); DeleteSelection(); });
            if (_clipboard != null) menu.AddItem(new GUIContent("Paste (Stamp mode)"), false, () => { _tool = Tool.Stamp; _stampRotation = 0; _stampScale = 1f; });
            menu.AddItem(new GUIContent("Clear Canvas"), false, () => ClearCanvas());
            menu.ShowAsContext();
            Event.current.Use();
        }

        // ── Mouse interaction ────────────────────────────────────────
        if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && inCanvas)
        {
            if (_tool == Tool.Paint)
            {
                if (Event.current.type == EventType.MouseDown) PushUndo();
                _pixels[my * _width + mx] = new Color(1f, 1f, 1f, _brushAlpha);
                Event.current.Use(); Repaint();
            }
            else if (_tool == Tool.Erase)
            {
                if (Event.current.type == EventType.MouseDown) PushUndo();
                int half = _eraserSize / 2;
                for (int ey = my - half; ey < my - half + _eraserSize; ey++)
                for (int ex = mx - half; ex < mx - half + _eraserSize; ex++)
                {
                    if (ex >= 0 && ex < _width && ey >= 0 && ey < _height)
                        _pixels[ey * _width + ex] = new Color(1f, 1f, 1f, 0f);
                }
                Event.current.Use(); Repaint();
            }
            else if (_tool == Tool.Select)
            {
                if (Event.current.type == EventType.MouseDown)
                    { _selStartX = mx; _selStartY = my; _hasSelection = true; }
                int sx = Mathf.Min(_selStartX, mx), sy = Mathf.Min(_selStartY, my);
                int ex = Mathf.Max(_selStartX, mx), ey = Mathf.Max(_selStartY, my);
                _selection = new RectInt(sx, sy, ex - sx + 1, ey - sy + 1);
                Event.current.Use(); Repaint();
            }
            else if (_tool == Tool.Stamp && _clipboard != null && Event.current.type == EventType.MouseDown)
            {
                PushUndo();
                PlaceStamp(mx, my);
                Event.current.Use(); Repaint();
            }
        }

        // Stamp click works even when mouse is outside the pixel area
        if (_tool == Tool.Stamp && _clipboard != null && !inCanvas
            && Event.current.type == EventType.MouseDown && canvasRect.Contains(mp))
        {
            PushUndo();
            PlaceStamp(mx, my);
            Event.current.Use(); Repaint();
        }

        // Continuous repaint for ghost stamp (always, so ghost follows mouse outside canvas)
        if (_tool == Tool.Stamp && _clipboard != null)
            Repaint();
    }

    void PlaceStamp(int px, int py)
    {
        int sw = Mathf.RoundToInt(_clipW * _stampScale);
        int sh = Mathf.RoundToInt(_clipH * _stampScale);
        for (int cy = 0; cy < sh; cy++)
        for (int cx = 0; cx < sw; cx++)
        {
            int srcX = Mathf.FloorToInt((float)cx / _stampScale);
            int srcY = Mathf.FloorToInt((float)cy / _stampScale);
            RotateCoords(ref srcX, ref srcY, _clipW, _clipH, _stampRotation);
            if (srcX < 0 || srcX >= _clipW || srcY < 0 || srcY >= _clipH) continue;
            var src = _clipboard[srcY * _clipW + srcX];
            if (src.a < 0.01f) continue;
            int dx = px + cx, dy = py + cy;
            if (dx >= 0 && dx < _width && dy >= 0 && dy < _height)
                _pixels[dy * _width + dx] = src;
        }
    }

    void RotateCoords(ref int x, ref int y, int w, int h, int degrees)
    {
        int ox = x, oy = y;
        switch (degrees % 360)
        {
            case 90:  x = oy; y = w - 1 - ox; break;
            case 180: x = w - 1 - ox; y = h - 1 - oy; break;
            case 270: x = h - 1 - oy; y = ox; break;
        }
    }

    // ── Preview panel ────────────────────────────────────────────────────────
    void DrawPreviewPanel()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        _previewMode = (PreviewMode)GUILayout.Toolbar((int)_previewMode, new[] { "Icon", "Pattern" }, EditorStyles.miniButton);
        GUILayout.Space(4f);
        _previewTint = EditorGUILayout.ColorField("Tint", _previewTint);
        _editorBgColor = EditorGUILayout.ColorField("Bg", _editorBgColor);
        GUILayout.Space(8f);

        var tex = BuildTexture();

        if (_previewMode == PreviewMode.Icon)
        {
            EditorGUILayout.LabelField("1x:", EditorStyles.miniLabel);
            var r1 = GUILayoutUtility.GetRect(_width, _height, GUILayout.Width(Mathf.Max(_width, 16)), GUILayout.Height(Mathf.Max(_height, 16)));
            if (tex != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(r1, tex, ScaleMode.ScaleToFit, true, 0f, _previewTint, Vector4.zero, Vector4.zero);
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("3x:", EditorStyles.miniLabel);
            var r3 = GUILayoutUtility.GetRect(_width * 3, _height * 3, GUILayout.Width(Mathf.Max(_width * 3, 48)), GUILayout.Height(Mathf.Max(_height * 3, 48)));
            if (tex != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(r3, tex, ScaleMode.ScaleToFit, true, 0f, _previewTint, Vector4.zero, Vector4.zero);
        }
        else
        {
            EditorGUILayout.LabelField("Tiled:", EditorStyles.miniLabel);
            var pr = GUILayoutUtility.GetRect(140f, 80f, GUILayout.Width(140f), GUILayout.Height(80f));
            if (tex != null && Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(pr, _editorBgColor);
                int tx = Mathf.CeilToInt(pr.width / _width) + 1;
                int ty = Mathf.CeilToInt(pr.height / _height) + 1;
                for (int iy = 0; iy < ty; iy++)
                for (int ix = 0; ix < tx; ix++)
                    GUI.DrawTexture(new Rect(pr.x + ix * _width, pr.y + iy * _height, _width, _height),
                        tex, ScaleMode.StretchToFill, true, 0f, _previewTint, Vector4.zero, Vector4.zero);
            }
        }

        if (tex != null) DestroyImmediate(tex);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    Texture2D BuildTexture()
    {
        if (_pixels == null) return null;
        var tex = new Texture2D(_width, _height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        tex.SetPixels(_pixels);
        tex.Apply();
        return tex;
    }

    void CopySelection()
    {
        if (!_hasSelection) return;
        _clipW = _selection.width; _clipH = _selection.height;
        _clipboard = new Color[_clipW * _clipH];
        for (int y = 0; y < _clipH; y++)
        for (int x = 0; x < _clipW; x++)
        {
            int sx = _selection.x + x, sy = _selection.y + y;
            _clipboard[y * _clipW + x] = (sx < _width && sy < _height) ? _pixels[sy * _width + sx] : new Color(1f, 1f, 1f, 0f);
        }
    }

    void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    void DeleteSelection()
    {
        if (!_hasSelection) return;
        for (int y = _selection.y; y < _selection.y + _selection.height && y < _height; y++)
        for (int x = _selection.x; x < _selection.x + _selection.width && x < _width; x++)
            _pixels[y * _width + x] = new Color(1f, 1f, 1f, 0f);
        Repaint();
    }

    // ── Icon picker popover ──────────────────────────────────────────────────
    void ShowIconPickerPopover()
    {
        var popup = new ZUIIconPickerPopup(iconName =>
        {
            // Resolve name to path, then import
            var tex = ZUI.FindIcon(iconName);
            if (tex != null) ImportIcon(AssetDatabase.GetAssetPath(tex));
        }, ZUI.ActiveSheet?.dataFolderPath);
        var btnRect = GUILayoutUtility.GetLastRect();
        PopupWindow.Show(btnRect, popup);
    }

    void ImportIcon(string assetPath)
    {
        var srcTex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (srcTex == null) return;

        var rt = RenderTexture.GetTemporary(srcTex.width, srcTex.height, 0);
        Graphics.Blit(srcTex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(srcTex.width, srcTex.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, srcTex.width, srcTex.height), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        PushUndo();
        int tw = Mathf.Min(readable.width, 64), th = Mathf.Min(readable.height, 64);
        ResizeCanvas(tw, th);
        _assetName = Path.GetFileNameWithoutExtension(assetPath);

        for (int y = 0; y < th; y++)
        for (int x = 0; x < tw; x++)
        {
            var c = readable.GetPixelBilinear((float)x / tw, (float)y / th);
            float luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            _pixels[y * _width + x] = new Color(1f, 1f, 1f, c.a * luma);
        }
        DestroyImmediate(readable);
        _loadedAssetPath = assetPath;
        Repaint();
    }

    /// <summary>Opens an icon for editing. Called from the Assets tab context menu.</summary>
    public void LoadIconForEditing(string assetPath, bool isSystemIcon)
    {
        ImportIcon(assetPath);
        if (isSystemIcon)
            _loadedAssetPath = null; // system icons can't be overwritten — save as new
        else
            _loadedAssetPath = assetPath; // custom icons can be overwritten
    }

    void StampText()
    {
        if (string.IsNullOrEmpty(_textInput)) return;
        var rt = RenderTexture.GetTemporary(_width * 4, _height * 4, 0);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        var style = new GUIStyle(GUI.skin.label) { fontSize = _textSize, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        GUI.Label(new Rect(0, 0, rt.width, rt.height), _textInput, style);
        var readable = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width; x++)
        {
            var c = readable.GetPixelBilinear((float)x / _width, (float)y / _height);
            if (c.a > 0.1f) _pixels[y * _width + x] = new Color(1f, 1f, 1f, c.a);
        }
        DestroyImmediate(readable);
        Repaint();
    }

    void SaveAsset(string overridePath = null)
    {
        string folder;
        if (!string.IsNullOrEmpty(overridePath))
        {
            folder = Path.GetDirectoryName(overridePath).Replace('\\', '/');
        }
        else
        {
            var sheet = ZUI.ActiveSheet;
            folder = (sheet != null && !string.IsNullOrEmpty(sheet.dataFolderPath)) ? sheet.dataFolderPath : "Assets/ZUIData";
            folder = folder.Replace('\\', '/');
        }

        if (!AssetDatabase.IsValidFolder(folder))
        {
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(parent).Replace('\\', '/'), Path.GetFileName(parent));
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        string path = overridePath ?? Path.Combine(folder, _assetName + ".png").Replace('\\', '/');
        var tex = BuildTexture();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        Debug.Log($"[ZUI Texture Editor] Saved {path}");
    }
}

// ── Shared icon grid component ───────────────────────────────────────────────
// Used by: icon picker popover, Assets tab icon grid, alias editor icon chooser.
// Modes:
//   Pick mode (onPicked != null): click selects and closes. No context menu.
//   Browse mode (onPicked == null): right-click shows context menu.

public class ZUIIconPickerPopup : PopupWindowContent
{
    Action<string> _onPicked;
    string _dataFolder;
    List<(string name, string path)> _icons;
    string _filter = "";
    bool _customOnly;
    Vector2 _scroll;

    public ZUIIconPickerPopup(Action<string> onPicked, string dataFolder = null)
    {
        _onPicked = onPicked;
        _dataFolder = dataFolder;
        Refresh();
    }

    void Refresh() => _icons = ZUIAssetLibrary.GetAvailableIcons(_dataFolder);

    public override Vector2 GetWindowSize() => new Vector2(340f, 380f);

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        _filter = EditorGUILayout.TextField(_filter);
        _customOnly = GUILayout.Toggle(_customOnly, "Custom", EditorStyles.miniButton, GUILayout.Width(54f));
        if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(22f))) Refresh();
        GUILayout.EndHorizontal();
        GUILayout.Space(4f);

        _scroll = GUILayout.BeginScrollView(_scroll);
        string filterLower = _filter?.ToLower() ?? "";
        string sysPath = ZUIAssetLibrary.k_SystemIconsPath.Replace('\\', '/').ToLower();
        int perRow = Mathf.Max(1, (int)(rect.width - 16f) / 50);
        int count = 0;

        GUILayout.BeginHorizontal();
        foreach (var (name, path) in _icons)
        {
            if (!string.IsNullOrEmpty(filterLower) && !name.ToLower().Contains(filterLower)) continue;
            if (_customOnly && path.Replace('\\', '/').ToLower().StartsWith(sysPath)) continue;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) continue;

            GUILayout.BeginVertical(GUILayout.Width(46f));
            var iconRect = GUILayoutUtility.GetRect(38f, 38f, GUILayout.Width(38f), GUILayout.Height(38f));
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);

            // Left click: pick (if in pick mode)
            if (_onPicked != null && Event.current.type == EventType.MouseDown
                && Event.current.button == 0 && iconRect.Contains(Event.current.mousePosition))
            {
                _onPicked.Invoke(name); // pass name, not path — aliases use names
                editorWindow?.Close();
                Event.current.Use();
            }

            EditorGUILayout.LabelField(name, EditorStyles.miniLabel, GUILayout.Width(44f), GUILayout.Height(11f));
            GUILayout.EndVertical();

            count++;
            if (count % perRow == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
        }
        GUILayout.EndHorizontal();

        if (count == 0)
            EditorGUILayout.LabelField("No icons found.", EditorStyles.miniLabel);

        GUILayout.EndScrollView();
    }
}
