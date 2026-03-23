using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drives the Map Editor tab.
///
/// ── Modes ──────────────────────────────────────────────────────────────────
///   Paint Mode : interact with the tile layer (Land / River / Blocking / Road)
///   Place Mode : snap-place / right-click-remove GameObjects on the grid
///
/// ── Tools (Paint Mode only) ────────────────────────────────────────────────
///   Brush : left-drag paints selected layer; right-drag erases it
///   Fill  : left-click flood-fills connected empty-layer cells;
///            right-click flood-fills connected filled-layer cells (erase)
///
/// ── Layers (bottom → top) ──────────────────────────────────────────────────
///   Land → River → Blocking → Road
///   Each is independent — e.g. Road can sit on top of Land.
///
/// ── Object labels ──────────────────────────────────────────────────────────
///   Placed objects are drawn as coloured blocks with a white pixel-art letter
///   stamped into the texture (no extra UI GameObjects needed).
///
/// ── HIERARCHY (InstructorConfigScene ► Canvas ► MapEditorPanel) ────────────
///
///   MapEditorPanel  ← this script
///     ├─ ModeBar
///     │    ├─ PaintModeButton
///     │    ├─ PlaceModeButton
///     │    └─ ModeLabel (TMP)
///     │
///     ├─ PaintToolbar  (visible only in Paint Mode)
///     │    ├─ TilePalettePanel  (HorizontalLayoutGroup)
///     │    │    ├─ LandBtn      } each Button has a child TMP label
///     │    │    ├─ RiverBtn     }  and an Image whose color is set at runtime
///     │    │    ├─ BlockingBtn  }
///     │    │    └─ RoadBtn      }
///     │    │
///     │    └─ ToolPanel  (HorizontalLayoutGroup)
///     │         ├─ BrushButton   (Button "BRUSH")
///     │         ├─ FillButton    (Button "FILL")
///     │         └─ ToolLabel     (TMP – shows active tool)
///     │
///     ├─ ObjectPalettePanel  (HorizontalLayoutGroup, visible only in Place Mode)
///     │    ├─ ForestBtn
///     │    ├─ CommunityBtn
///     │    ├─ AbandonedSiteBtn
///     │    ├─ VehicleBtn
///     │    └─ MotelBtn
///     │
///     └─ GridScrollView (ScrollRect)
///          └─ GridContainer (RectTransform – Content of ScrollRect)
///               └─ GridImage (RawImage)    ← assign to gridImage / gridRect
///                    └─ GridInputHandler (on same GO) → editorCanvas = this
/// </summary>
public class MapEditorCanvas : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Grid Rendering")]
    public int cellPixelSize = 24;

    [Header("UI – Grid")]
    public RawImage      gridImage;
    public RectTransform gridRect;

    [Header("UI – Interaction Mode")]
    public Button          paintModeButton;
    public Button          placeModeButton;
    public TextMeshProUGUI modeLabel;

    [Header("UI – Paint Toolbar (shown in Paint mode)")]
    public GameObject paintToolbar;

    [Header("UI – Tile Palette (order: Land, River, Blocking, Road)")]
    public GameObject tilePalettePanel;
    public Button[]   tilePaletteButtons; // exactly 4

    [Header("UI – Paint Tool Buttons")]
    public Button          brushButton;
    public Button          fillButton;
    public Button          eraseButton;
    public TextMeshProUGUI toolLabel;

    [Header("Button Highlight Colors")]
    public Color normalButtonColor   = new Color(0.55f, 0.55f, 0.55f, 1f);
    public Color selectedButtonColor = new Color(1.00f, 0.85f, 0.15f, 1f); // gold

    [Header("UI – Object Palette (order matches PlacedObjectType enum)")]
    public GameObject objectPalettePanel;
    public Button[]   objectPaletteButtons; // exactly 5
    public Button     eraseObjectButton;

    [Header("Object Default Sizes (index = PlacedObjectType)")]
    public int[] objectDefaultWidths  = { 2, 3, 2, 1, 2 };
    public int[] objectDefaultHeights = { 2, 3, 2, 1, 2 };

    // ── Colours ───────────────────────────────────────────────────────────────

    static readonly Color32 BgColor    = new Color32( 30,  30,  30, 255);
    static readonly Color32 LandColor  = new Color32( 90, 170,  70, 255);
    static readonly Color32 RiverColor = new Color32( 70, 130, 200, 255);
    static readonly Color32 BlockColor = new Color32(120,  85,  50, 255);
    static readonly Color32 RoadColor  = new Color32(180, 180, 180, 255);
    static readonly Color32 GridLine   = new Color32( 15,  15,  15, 255);

    static readonly Color32[] ObjectColors =
    {
        new Color32( 20, 110,  20, 255), // Forest
        new Color32(220, 200,  40, 255), // Community
        new Color32(210, 130,  40, 255), // AbandonedSite
        new Color32( 40, 200, 200, 255), // Vehicle
        new Color32(190,  60, 190, 255), // Motel
    };

    // ── Pixel-art glyphs (5 wide × 7 tall, MSB = leftmost pixel, row 0 = top) ─
    // Letters: F C A V M  (one per PlacedObjectType)
    static readonly uint[][] Glyphs =
    {
        // F – Forest
        new uint[] { 0b11111, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000, 0b10000 },
        // C – Community
        new uint[] { 0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111 },
        // A – AbandonedSite
        new uint[] { 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
        // V – Vehicle
        new uint[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b01010, 0b00100 },
        // M – Motel
        new uint[] { 0b10001, 0b11011, 0b10101, 0b10001, 0b10001, 0b10001, 0b10001 },
    };

    // ── State ─────────────────────────────────────────────────────────────────

    EditorMode       _mode         = EditorMode.Paint;
    PaintTool        _tool         = PaintTool.Brush;
    TileType         _selectedTile = TileType.Land;
    PlacedObjectType _selectedObj  = PlacedObjectType.Forest;
    bool             _isDragging   = false;
    bool             _eraseObjects = false;
    Vector2Int       _hoverCell    = new Vector2Int(-1, -1);
    Texture2D        _tex;

    // Original normalColor per button, so highlight can restore it
    readonly Dictionary<Button, Color> _btnOriginalColors = new Dictionary<Button, Color>();

    MapConfig Config => InstructorConfigManager.Instance.CurrentConfig;

    // ─────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        BuildTexture();
        WireButtons();
        // Set initial selections so highlights fire after buttons are registered
        SelectTile(TileType.Land);
        SelectObject(PlacedObjectType.Forest);
        SetMode(EditorMode.Paint);
        SetTool(PaintTool.Brush);
        RefreshTexture();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Texture
    // ─────────────────────────────────────────────────────────────────────────

    void BuildTexture()
    {
        int texW = Config.gridWidth  * cellPixelSize;
        int texH = Config.gridHeight * cellPixelSize;

        _tex            = new Texture2D(texW, texH, TextureFormat.RGB24, false);
        _tex.filterMode = FilterMode.Point;
        gridImage.texture = _tex;
        gridRect.sizeDelta = new Vector2(texW, texH);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Button wiring
    // ─────────────────────────────────────────────────────────────────────────

    void WireButtons()
    {
        // Mode buttons
        RegisterButton(paintModeButton, normalButtonColor);
        RegisterButton(placeModeButton, normalButtonColor);
        paintModeButton.onClick.AddListener(() => SetMode(EditorMode.Paint));
        placeModeButton.onClick.AddListener(() => SetMode(EditorMode.Place));

        // Tool buttons
        RegisterButton(brushButton, normalButtonColor);
        RegisterButton(fillButton,  normalButtonColor);
        RegisterButton(eraseButton, normalButtonColor);
        brushButton.onClick.AddListener(() => SetTool(PaintTool.Brush));
        fillButton .onClick.AddListener(() => SetTool(PaintTool.Fill));
        eraseButton.onClick.AddListener(() => SetTool(PaintTool.Erase));

        // Tile palette — neutral background, label text only (no colour tinting)
        string[]   names = { "Land", "River", "Blocking", "Road" };
        TileType[] types = { TileType.Land, TileType.River, TileType.Blocking, TileType.Road };
        for (int i = 0; i < tilePaletteButtons.Length && i < types.Length; i++)
        {
            var btn  = tilePaletteButtons[i];
            var tile = types[i];
            RegisterButton(btn, normalButtonColor);
            btn.onClick.AddListener(() => SelectTile(tile));
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = names[(int)tile];
        }

        // Object palette — keep distinct colours; highlight tracks selection
        var objTypes = (PlacedObjectType[])System.Enum.GetValues(typeof(PlacedObjectType));
        for (int i = 0; i < objectPaletteButtons.Length && i < objTypes.Length; i++)
        {
            var btn = objectPaletteButtons[i];
            var obj = objTypes[i];
            Color32 c32 = ObjectColors[(int)obj];
            Color   col = new Color(c32.r / 255f, c32.g / 255f, c32.b / 255f);
            RegisterButton(btn, col);
            btn.onClick.AddListener(() => SelectObject(obj));
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = obj.ToString();
        }

        if (eraseObjectButton != null)
        {
            RegisterButton(eraseObjectButton, normalButtonColor);
            eraseObjectButton.onClick.AddListener(ToggleEraseObjects);
            var lbl = eraseObjectButton.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = "ERASE";
        }
    }

    // ── Button highlight helpers ───────────────────────────────────────────────

    void RegisterButton(Button btn, Color originalColor)
    {
        _btnOriginalColors[btn] = originalColor;
        var cb = btn.colors;
        cb.normalColor = originalColor;
        btn.colors = cb;
    }

    void SetButtonHighlight(Button btn, bool selected)
    {
        _btnOriginalColors.TryGetValue(btn, out Color original);
        var cb = btn.colors;
        cb.normalColor = selected ? selectedButtonColor : original;
        btn.colors = cb;
    }

    void SelectTile(TileType tile)
    {
        _selectedTile = tile;
        UpdateTilePaletteHighlight();
    }

    void SelectObject(PlacedObjectType obj)
    {
        _selectedObj  = obj;
        _eraseObjects = false;
        if (eraseObjectButton != null) SetButtonHighlight(eraseObjectButton, false);
        UpdateObjectPaletteHighlight();
    }

    void ToggleEraseObjects()
    {
        _eraseObjects = !_eraseObjects;
        if (_eraseObjects) UpdateObjectPaletteHighlight(); // deselect all object buttons visually
        if (eraseObjectButton != null) SetButtonHighlight(eraseObjectButton, _eraseObjects);
    }

    void UpdateTilePaletteHighlight()
    {
        TileType[] types = { TileType.Land, TileType.River, TileType.Blocking, TileType.Road };
        for (int i = 0; i < tilePaletteButtons.Length && i < types.Length; i++)
            SetButtonHighlight(tilePaletteButtons[i], types[i] == _selectedTile);
    }

    void UpdateToolHighlight()
    {
        SetButtonHighlight(brushButton, _tool == PaintTool.Brush);
        SetButtonHighlight(fillButton,  _tool == PaintTool.Fill);
        SetButtonHighlight(eraseButton, _tool == PaintTool.Erase);
    }

    void UpdateObjectPaletteHighlight()
    {
        var objTypes = (PlacedObjectType[])System.Enum.GetValues(typeof(PlacedObjectType));
        for (int i = 0; i < objectPaletteButtons.Length && i < objTypes.Length; i++)
            SetButtonHighlight(objectPaletteButtons[i], objTypes[i] == _selectedObj);
    }

    void UpdateModeHighlight()
    {
        SetButtonHighlight(paintModeButton, _mode == EditorMode.Paint);
        SetButtonHighlight(placeModeButton, _mode == EditorMode.Place);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mode / tool switching
    // ─────────────────────────────────────────────────────────────────────────

    public void SetMode(EditorMode mode)
    {
        _mode = mode;

        _eraseObjects = false;
        if (eraseObjectButton != null) SetButtonHighlight(eraseObjectButton, false);

        bool isPaint = mode == EditorMode.Paint;
        paintToolbar      .SetActive(isPaint);
        tilePalettePanel  .SetActive(isPaint);
        objectPalettePanel.SetActive(!isPaint);

        modeLabel.text = isPaint ? "PAINT MODE" : "PLACE MODE";
        UpdateModeHighlight();
    }

    void SetTool(PaintTool tool)
    {
        _tool = tool;
        toolLabel.text = tool switch
        {
            PaintTool.Brush => "BRUSH",
            PaintTool.Fill  => "FILL",
            PaintTool.Erase => "ERASE",
            _               => ""
        };
        UpdateToolHighlight();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Input callbacks (called by GridInputHandler)
    // ─────────────────────────────────────────────────────────────────────────

    public void OnGridPointerDown(PointerEventData e)
    {
        _isDragging = true;
        HandleInput(e);
    }

    public void OnGridPointerUp(PointerEventData e) => _isDragging = false;

    public void OnGridDrag(PointerEventData e)
    {
        // Drag painting works for Brush and Erase tools
        if (_isDragging && _mode == EditorMode.Paint &&
            (_tool == PaintTool.Brush || _tool == PaintTool.Erase))
            HandleInput(e);
    }

    public void OnGridPointerMove(PointerEventData e)
    {
        var cell = CellFrom(e);
        if (cell == _hoverCell) return;
        _hoverCell = cell;
        RefreshTexture();
    }

    public void OnGridPointerExit(PointerEventData e)
    {
        _hoverCell = new Vector2Int(-1, -1);
        RefreshTexture();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core interaction dispatch
    // ─────────────────────────────────────────────────────────────────────────

    void HandleInput(PointerEventData e)
    {
        var cell = CellFrom(e);
        if (cell.x < 0) return;

        bool isRight = e.button == PointerEventData.InputButton.Right;

        if (_mode == EditorMode.Paint)
        {
            switch (_tool)
            {
                case PaintTool.Brush:
                    // Left = paint selected layer; Right = erase selected layer
                    Config.SetLayer(cell.x, cell.y, _selectedTile, !isRight);
                    RefreshTexture();
                    InstructorConfigManager.Instance.NotifyConfigChanged();
                    break;
                case PaintTool.Erase:
                    // Both left and right erase selected layer (no ambiguity needed)
                    Config.SetLayer(cell.x, cell.y, _selectedTile, false);
                    RefreshTexture();
                    InstructorConfigManager.Instance.NotifyConfigChanged();
                    break;
                case PaintTool.Fill:
                    FillPaint(cell.x, cell.y, isRight);
                    break;
            }
        }
        else // Place
        {
            if (isRight || _eraseObjects) RemoveObjectAt(cell.x, cell.y);
            else                          PlaceObject(cell.x, cell.y);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Flood fill (BFS)
    // ─────────────────────────────────────────────────────────────────────────

    void FillPaint(int startX, int startY, bool erase)
    {
        // Fill doesn't apply in Erase-all mode (no defined layer to fill)
        if (_selectedTile == TileType.Empty) return;

        bool targetVal = Config.GetLayer(startX, startY, _selectedTile);
        bool fillVal   = !erase;

        // If already the desired value, nothing to do
        if (targetVal == fillVal) return;

        int gw = Config.gridWidth;
        int gh = Config.gridHeight;

        var queue   = new Queue<Vector2Int>();
        var visited = new HashSet<int>(); // flat index to track visited

        queue.Enqueue(new Vector2Int(startX, startY));

        while (queue.Count > 0)
        {
            var c   = queue.Dequeue();
            int idx = c.y * gw + c.x;

            if (!Config.InBounds(c.x, c.y)) continue;
            if (visited.Contains(idx))      continue;
            visited.Add(idx);

            // Only spread to cells matching the original value
            if (Config.GetLayer(c.x, c.y, _selectedTile) != targetVal) continue;

            Config.SetLayer(c.x, c.y, _selectedTile, fillVal);

            queue.Enqueue(new Vector2Int(c.x + 1, c.y));
            queue.Enqueue(new Vector2Int(c.x - 1, c.y));
            queue.Enqueue(new Vector2Int(c.x, c.y + 1));
            queue.Enqueue(new Vector2Int(c.x, c.y - 1));
        }

        RefreshTexture();
        InstructorConfigManager.Instance.NotifyConfigChanged();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Object placement
    // ─────────────────────────────────────────────────────────────────────────

    void PlaceObject(int x, int y)
    {
        int w = objectDefaultWidths [(int)_selectedObj];
        int h = objectDefaultHeights[(int)_selectedObj];
        Config.objects.RemoveAll(o => Overlaps(o, x, y, w, h));
        Config.objects.Add(new PlacedObjectData { type = _selectedObj, gridX = x, gridY = y, width = w, height = h });
        RefreshTexture();
        InstructorConfigManager.Instance.NotifyConfigChanged();
    }

    void RemoveObjectAt(int x, int y)
    {
        if (Config.objects.RemoveAll(o =>
            x >= o.gridX && x < o.gridX + o.width &&
            y >= o.gridY && y < o.gridY + o.height) > 0)
        {
            RefreshTexture();
            InstructorConfigManager.Instance.NotifyConfigChanged();
        }
    }

    static bool Overlaps(PlacedObjectData o, int x, int y, int w, int h) =>
        o.gridX < x + w && o.gridX + o.width  > x &&
        o.gridY < y + h && o.gridY + o.height > y;

    // ─────────────────────────────────────────────────────────────────────────
    // Pointer → grid cell
    // ─────────────────────────────────────────────────────────────────────────

    Vector2Int CellFrom(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                gridRect, e.position, e.pressEventCamera, out Vector2 local))
            return new Vector2Int(-1, -1);

        // Shift from center-origin to bottom-left origin
        local += gridRect.sizeDelta * 0.5f;

        int cx = Mathf.FloorToInt(local.x / cellPixelSize);
        int cy = Mathf.FloorToInt(local.y / cellPixelSize);

        return Config.InBounds(cx, cy) ? new Vector2Int(cx, cy) : new Vector2Int(-1, -1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Texture rendering
    // ─────────────────────────────────────────────────────────────────────────

    void RefreshTexture()
    {
        int gw = Config.gridWidth;
        int gh = Config.gridHeight;
        int cs = cellPixelSize;
        int pw = gw * cs; // texture pixel width

        Color32[] pixels = new Color32[pw * gh * cs];

        // 1 ── Tile layers (bottom → top: Land, River, Blocking, Road)
        for (int cy = 0; cy < gh; cy++)
        for (int cx = 0; cx < gw; cx++)
        {
            Color32 col = CompositeCell(cx, cy);
            FillCell(pixels, cx, cy, gw, cs, col);
        }

        // 2 ── Placed objects (coloured block + white border + pixel glyph)
        foreach (var obj in Config.objects)
            DrawObject(pixels, obj, gw, gh, cs, pw);

        // 3 ── Hover highlight
        if (Config.InBounds(_hoverCell.x, _hoverCell.y))
        {
            int hw = _mode == EditorMode.Place ? objectDefaultWidths [(int)_selectedObj] : 1;
            int hh = _mode == EditorMode.Place ? objectDefaultHeights[(int)_selectedObj] : 1;
            for (int oy = _hoverCell.y; oy < _hoverCell.y + hh && oy < gh; oy++)
            for (int ox = _hoverCell.x; ox < _hoverCell.x + hw && ox < gw; ox++)
                BlendCell(pixels, ox, oy, gw, cs, new Color32(255, 255, 255, 255), 0.25f);
        }

        _tex.SetPixels32(pixels);
        _tex.Apply();
    }

    // Layer compositing: higher layer wins; empty = dark background
    Color32 CompositeCell(int x, int y)
    {
        Color32 col = BgColor;
        if (Config.GetLand    (x, y)) col = LandColor;
        if (Config.GetRiver   (x, y)) col = RiverColor;
        if (Config.GetBlocking(x, y)) col = BlockColor;
        if (Config.GetRoad    (x, y)) col = RoadColor;
        return col;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw helpers
    // ─────────────────────────────────────────────────────────────────────────

    static void FillCell(Color32[] pixels, int cx, int cy, int gw, int cs, Color32 col)
    {
        int pw = gw * cs;
        for (int py = 0; py < cs; py++)
        for (int px = 0; px < cs; px++)
            pixels[(cy * cs + py) * pw + (cx * cs + px)] =
                (px == 0 || py == 0) ? GridLine : col;
    }

    static void BlendCell(Color32[] pixels, int cx, int cy, int gw, int cs, Color32 over, float t)
    {
        int pw = gw * cs;
        for (int py = 1; py < cs; py++)
        for (int px = 1; px < cs; px++)
        {
            int idx = (cy * cs + py) * pw + (cx * cs + px);
            pixels[idx] = Color32.Lerp(pixels[idx], over, t);
        }
    }

    // Draws object footprint (coloured block + white outer border + pixel glyph)
    void DrawObject(Color32[] pixels, PlacedObjectData obj, int gw, int gh, int cs, int pw)
    {
        Color32 col = ObjectColors[(int)obj.type];

        // ── Coloured block ────────────────────────────────────────────────────
        for (int oy = obj.gridY; oy < obj.gridY + obj.height && oy < gh; oy++)
        for (int ox = obj.gridX; ox < obj.gridX + obj.width  && ox < gw; ox++)
        {
            bool leftEdge   = ox == obj.gridX;
            bool rightEdge  = ox == obj.gridX + obj.width  - 1;
            bool bottomEdge = oy == obj.gridY;
            bool topEdge    = oy == obj.gridY + obj.height - 1;

            for (int py = 0; py < cs; py++)
            for (int px = 0; px < cs; px++)
            {
                // Outer border (2 px) — bright white so objects read clearly
                bool isBorder = (leftEdge   && px <= 1) ||
                                (rightEdge  && px >= cs - 2) ||
                                (bottomEdge && py <= 1) ||
                                (topEdge    && py >= cs - 2);

                // Shared grid-line stays dark
                bool isGridLine = px == 0 || py == 0;

                Color32 c = isGridLine ? GridLine : (isBorder ? new Color32(255, 255, 255, 255) : col);
                pixels[(oy * cs + py) * pw + (ox * cs + px)] = c;
            }
        }

        // ── Pixel-art glyph centred on object footprint ───────────────────────
        int texW   = gw * cs;
        int texH   = gh * cs;
        // Centre of footprint in texture pixel space
        int centerX = (obj.gridX * cs) + (obj.width  * cs) / 2;
        int centerY = (obj.gridY * cs) + (obj.height * cs) / 2;

        // Scale glyph up for larger objects; min scale 2 so it's always readable
        int glyphScale = (obj.width >= 3 && obj.height >= 3) ? 3 : 2;

        DrawGlyph(pixels, Glyphs[(int)obj.type], centerX, centerY, texW, texH, glyphScale);
    }

    // Stamp a 5×7 pixel-art glyph centred at (cx, cy) in the pixel buffer.
    // Row 0 of the glyph = top of the letter.
    // The texture has Y=0 at the bottom, so rows are drawn top→bottom by
    // placing row 0 at the highest Y.
    static void DrawGlyph(Color32[] pixels, uint[] glyph,
                           int cx, int cy, int texW, int texH, int scale)
    {
        const int GW = 5, GH = 7;
        int totalW = GW * scale;
        int totalH = GH * scale;
        int startX = cx - totalW / 2;
        int startY = cy - totalH / 2; // bottom of glyph in texture space

        Color32 white = new Color32(255, 255, 255, 255);
        // Optional 1-px dark shadow drawn first for contrast
        Color32 shadow = new Color32(0, 0, 0, 255);

        for (int pass = 0; pass < 2; pass++)
        {
            int offX = pass == 0 ? 1 : 0;
            int offY = pass == 0 ? -1 : 0;
            Color32 col = pass == 0 ? shadow : white;

            for (int row = 0; row < GH; row++)
            {
                uint bits = glyph[row];
                // row 0 = top of letter → highest Y in texture
                int baseY = startY + (GH - 1 - row) * scale;

                for (int col5 = 0; col5 < GW; col5++)
                {
                    if (((bits >> (GW - 1 - col5)) & 1) == 0) continue;
                    int baseX = startX + col5 * scale;

                    for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                    {
                        int px = baseX + sx + offX;
                        int py = baseY + sy + offY;
                        if (px < 0 || px >= texW || py < 0 || py >= texH) continue;
                        pixels[py * texW + px] = col;
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public actions
    // ─────────────────────────────────────────────────────────────────────────

    public void ClearMap()
    {
        int gw = Config.gridWidth;
        int gh = Config.gridHeight;
        for (int y = 0; y < gh; y++)
        for (int x = 0; x < gw; x++)
            Config.EraseAll(x, y);

        Config.objects.Clear();
        RefreshTexture();
        InstructorConfigManager.Instance.NotifyConfigChanged();
    }

    /// <summary>Call after loading an external config to rebuild the texture.</summary>
    public void ReloadFromConfig()
    {
        BuildTexture();
        RefreshTexture();
    }
}

// ── Enums ─────────────────────────────────────────────────────────────────────
public enum EditorMode { Paint, Place }
public enum PaintTool  { Brush, Fill, Erase }
