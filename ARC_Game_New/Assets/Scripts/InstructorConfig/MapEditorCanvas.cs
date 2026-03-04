using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drives the Map Editor tab.
/// Renders the grid as a Texture2D on a RawImage — no Tilemap required.
/// Supports two interaction modes:
///   • Paint Mode  – click/drag to set TileType per cell
///   • Place Mode  – click to snap-place a GameObject record; right-click removes
///
/// HIERARCHY (InstructorConfigScene ► Canvas ► MapEditorPanel):
///
///   MapEditorPanel (this script lives here)
///     ├─ ModeBar
///     │    ├─ PaintModeButton   → OnClick: SetMode(Paint)
///     │    ├─ PlaceModeButton   → OnClick: SetMode(Place)
///     │    └─ ModeLabel (TMP)
///     ├─ TilePalettePanel
///     │    └─ (5 Buttons: Road / Land / Blocking / River / Erase)
///     │         each button has a child TMP label with the tile name
///     ├─ ObjectPalettePanel
///     │    └─ (5 Buttons: Forest / Community / AbandonedSite / Vehicle / Motel)
///     │         each button has a child TMP label with the object name
///     ├─ GridScrollView (ScrollRect, optional)
///     │    └─ GridContainer (RectTransform — assign to gridRect)
///     │         └─ GridImage (RawImage — assign to gridImage)
///     │              └─ GridInputHandler (script) → editorCanvas = this
///     └─ LegendPanel (optional visual key)
///
/// GridImage must have a GridInputHandler component that forwards pointer events here.
/// The GridContainer/GridScrollView lets instructors pan if the grid is large.
/// </summary>
public class MapEditorCanvas : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Grid Rendering")]
    [Tooltip("Pixel size of each grid cell in the texture")]
    public int cellPixelSize = 24;

    [Header("UI – Grid")]
    public RawImage      gridImage;
    public RectTransform gridRect;

    [Header("UI – Mode")]
    public Button          paintModeButton;
    public Button          placeModeButton;
    public TextMeshProUGUI modeLabel;

    [Header("UI – Tile Palette (order: Road, Land, Blocking, River, Erase)")]
    public GameObject tilePalettePanel;
    public Button[]   tilePaletteButtons;

    [Header("UI – Object Palette (order matches PlacedObjectType enum)")]
    public GameObject objectPalettePanel;
    public Button[]   objectPaletteButtons;

    [Header("Object Default Sizes (index matches PlacedObjectType)")]
    [Tooltip("Forest=2, Community=3, AbandonedSite=2, Vehicle=1, Motel=2")]
    public int[] objectDefaultWidths  = { 2, 3, 2, 1, 2 };
    public int[] objectDefaultHeights = { 2, 3, 2, 1, 2 };

    // ── Colours ───────────────────────────────────────────────────────────────

    static readonly Color32[] TileColors =
    {
        new Color32( 50,  50,  50, 255), // Empty    – very dark
        new Color32(170, 170, 170, 255), // Road     – light grey
        new Color32( 90, 170,  70, 255), // Land     – green
        new Color32(120,  85,  50, 255), // Blocking – brown
        new Color32( 70, 130, 200, 255), // River    – blue
    };

    static readonly Color32[] ObjectColors =
    {
        new Color32( 20, 110,  20, 255), // Forest        – dark green
        new Color32(230, 210,  40, 255), // Community     – yellow
        new Color32(220, 140,  40, 255), // AbandonedSite – orange
        new Color32( 40, 200, 200, 255), // Vehicle       – cyan
        new Color32(200,  70, 200, 255), // Motel         – magenta
    };

    static readonly Color32 GridLineColor  = new Color32(25, 25, 25, 255);
    static readonly Color32 HoverTileColor = new Color32(255, 255, 255, 255);
    // Hover alpha-blend weight applied on top of underlying colour
    const float HoverBlend = 0.35f;

    // ── State ─────────────────────────────────────────────────────────────────

    EditorMode      _mode             = EditorMode.Paint;
    TileType        _selectedTile     = TileType.Road;
    PlacedObjectType _selectedObject  = PlacedObjectType.Forest;
    bool            _isDragging       = false;
    Vector2Int      _hoverCell        = new Vector2Int(-1, -1);
    Texture2D       _tex;

    MapConfig Config => InstructorConfigManager.Instance.CurrentConfig;

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        BuildTexture();
        WireButtons();
        RefreshTexture();
        SetMode(EditorMode.Paint);
    }

    // ── Texture setup ─────────────────────────────────────────────────────────

    void BuildTexture()
    {
        int texW = Config.gridWidth  * cellPixelSize;
        int texH = Config.gridHeight * cellPixelSize;

        _tex            = new Texture2D(texW, texH, TextureFormat.RGB24, false);
        _tex.filterMode = FilterMode.Point;
        gridImage.texture = _tex;

        gridRect.sizeDelta = new Vector2(texW, texH);
    }

    // ── Button wiring ─────────────────────────────────────────────────────────

    void WireButtons()
    {
        paintModeButton.onClick.AddListener(() => SetMode(EditorMode.Paint));
        placeModeButton.onClick.AddListener(() => SetMode(EditorMode.Place));

        // Tile palette (Road, Land, Blocking, River, Erase/Empty)
        TileType[] tileOrder = { TileType.Road, TileType.Land, TileType.Blocking, TileType.River, TileType.Empty };
        string[]   tileNames = { "Road", "Land", "Blocking", "River", "Erase" };
        for (int i = 0; i < tilePaletteButtons.Length && i < tileOrder.Length; i++)
        {
            var btn  = tilePaletteButtons[i];
            var tile = tileOrder[i];
            btn.onClick.AddListener(() => _selectedTile = tile);
            // Tint button body to show tile colour
            btn.GetComponent<Image>().color = TileColors[(int)tile];
            // Label
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = tileNames[i];
        }

        // Object palette
        var allObjTypes = (PlacedObjectType[])System.Enum.GetValues(typeof(PlacedObjectType));
        for (int i = 0; i < objectPaletteButtons.Length && i < allObjTypes.Length; i++)
        {
            var btn = objectPaletteButtons[i];
            var obj = allObjTypes[i];
            btn.onClick.AddListener(() => _selectedObject = obj);
            btn.GetComponent<Image>().color = ObjectColors[(int)obj];
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = obj.ToString();
        }
    }

    // ── Mode switch ───────────────────────────────────────────────────────────

    public void SetMode(EditorMode mode)
    {
        _mode = mode;
        tilePalettePanel.SetActive(mode == EditorMode.Paint);
        objectPalettePanel.SetActive(mode == EditorMode.Place);
        modeLabel.text              = mode == EditorMode.Paint ? "PAINT MODE" : "PLACE MODE";
        paintModeButton.interactable = mode != EditorMode.Paint;
        placeModeButton.interactable = mode != EditorMode.Place;
    }

    // ── Input callbacks (called by GridInputHandler) ──────────────────────────

    public void OnGridPointerDown(PointerEventData e)
    {
        _isDragging = true;
        HandleInput(e);
    }

    public void OnGridPointerUp(PointerEventData e)
    {
        _isDragging = false;
    }

    public void OnGridDrag(PointerEventData e)
    {
        if (_isDragging && _mode == EditorMode.Paint)
            HandleInput(e);
    }

    public void OnGridPointerMove(PointerEventData e)
    {
        Vector2Int cell = CellFromPointer(e);
        if (cell == _hoverCell) return;
        _hoverCell = cell;
        RefreshTexture();
    }

    public void OnGridPointerExit(PointerEventData e)
    {
        _hoverCell = new Vector2Int(-1, -1);
        RefreshTexture();
    }

    // ── Core interaction ──────────────────────────────────────────────────────

    void HandleInput(PointerEventData e)
    {
        Vector2Int cell = CellFromPointer(e);
        if (cell.x < 0) return;

        if (_mode == EditorMode.Paint)
        {
            TileType paintWith = e.button == PointerEventData.InputButton.Right
                ? TileType.Empty
                : _selectedTile;
            Config.SetTile(cell.x, cell.y, paintWith);
            RefreshTexture();
            InstructorConfigManager.Instance.NotifyConfigChanged();
        }
        else // Place
        {
            if (e.button == PointerEventData.InputButton.Left)
                PlaceObject(cell.x, cell.y);
            else if (e.button == PointerEventData.InputButton.Right)
                RemoveObjectAt(cell.x, cell.y);
        }
    }

    void PlaceObject(int x, int y)
    {
        int w = objectDefaultWidths [(int)_selectedObject];
        int h = objectDefaultHeights[(int)_selectedObject];

        // Remove anything already occupying these cells
        Config.objects.RemoveAll(o => Overlaps(o, x, y, w, h));

        Config.objects.Add(new PlacedObjectData
        {
            type   = _selectedObject,
            gridX  = x, gridY = y,
            width  = w, height = h
        });
        RefreshTexture();
        InstructorConfigManager.Instance.NotifyConfigChanged();
    }

    void RemoveObjectAt(int x, int y)
    {
        bool removed = Config.objects.RemoveAll(
            o => x >= o.gridX && x < o.gridX + o.width &&
                 y >= o.gridY && y < o.gridY + o.height) > 0;
        if (removed)
        {
            RefreshTexture();
            InstructorConfigManager.Instance.NotifyConfigChanged();
        }
    }

    static bool Overlaps(PlacedObjectData o, int x, int y, int w, int h)
    {
        return o.gridX < x + w && o.gridX + o.width  > x &&
               o.gridY < y + h && o.gridY + o.height > y;
    }

    // ── Pointer → grid cell ───────────────────────────────────────────────────

    Vector2Int CellFromPointer(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                gridRect, e.position, e.pressEventCamera, out Vector2 local))
            return new Vector2Int(-1, -1);

        // RectTransform pivot is (0.5, 0.5) → shift origin to bottom-left
        local += gridRect.sizeDelta * 0.5f;

        int cx = Mathf.FloorToInt(local.x / cellPixelSize);
        int cy = Mathf.FloorToInt(local.y / cellPixelSize);

        if (!Config.InBounds(cx, cy)) return new Vector2Int(-1, -1);
        return new Vector2Int(cx, cy);
    }

    // ── Texture rendering ─────────────────────────────────────────────────────

    void RefreshTexture()
    {
        int gw = Config.gridWidth;
        int gh = Config.gridHeight;
        int cs = cellPixelSize;
        int pw = gw * cs; // pixel width of texture

        Color32[] pixels = new Color32[pw * gh * cs];

        // 1. Paint tile layer
        for (int cy = 0; cy < gh; cy++)
        for (int cx = 0; cx < gw; cx++)
        {
            Color32 col = TileColors[(int)Config.GetTile(cx, cy)];
            FillCell(pixels, cx, cy, gw, cs, col);
        }

        // 2. Paint placed objects on top (solid, overwrite tile colour)
        foreach (var obj in Config.objects)
        {
            Color32 col = ObjectColors[(int)obj.type];
            for (int oy = obj.gridY; oy < obj.gridY + obj.height && oy < gh; oy++)
            for (int ox = obj.gridX; ox < obj.gridX + obj.width  && ox < gw; ox++)
                FillCell(pixels, ox, oy, gw, cs, col);

            // Thin border around multi-cell objects
            DrawObjectBorder(pixels, obj, gw, gh, cs, pw);
        }

        // 3. Hover highlight
        if (Config.InBounds(_hoverCell.x, _hoverCell.y))
        {
            int hw = _mode == EditorMode.Place ? objectDefaultWidths [(int)_selectedObject] : 1;
            int hh = _mode == EditorMode.Place ? objectDefaultHeights[(int)_selectedObject] : 1;

            for (int oy = _hoverCell.y; oy < _hoverCell.y + hh && oy < gh; oy++)
            for (int ox = _hoverCell.x; ox < _hoverCell.x + hw && ox < gw; ox++)
                BlendCell(pixels, ox, oy, gw, cs, HoverTileColor, HoverBlend);
        }

        _tex.SetPixels32(pixels);
        _tex.Apply();
    }

    // Fill a single cell with colour (preserve grid line border pixels)
    static void FillCell(Color32[] pixels, int cx, int cy, int gw, int cs, Color32 col)
    {
        int pw = gw * cs;
        for (int py = 0; py < cs; py++)
        for (int px = 0; px < cs; px++)
        {
            bool border = px == 0 || py == 0;
            pixels[(cy * cs + py) * pw + (cx * cs + px)] = border ? GridLineColor : col;
        }
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

    // Draw a 2-pixel bright border around a multi-cell object footprint
    static void DrawObjectBorder(Color32[] pixels, PlacedObjectData obj,
                                  int gw, int gh, int cs, int pw)
    {
        Color32 borderCol = new Color32(255, 255, 255, 255);
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
                bool onBorder = (leftEdge   && px <= 1) ||
                                (rightEdge  && px >= cs - 2) ||
                                (bottomEdge && py <= 1) ||
                                (topEdge    && py >= cs - 2);
                if (onBorder)
                    pixels[(oy * cs + py) * pw + (ox * cs + px)] = borderCol;
            }
        }
    }

    // ── Public actions (wired from InstructorConfigUI) ────────────────────────

    public void ClearMap()
    {
        for (int y = 0; y < Config.gridHeight; y++)
        for (int x = 0; x < Config.gridWidth;  x++)
            Config.SetTile(x, y, TileType.Empty);

        Config.objects.Clear();
        RefreshTexture();
        InstructorConfigManager.Instance.NotifyConfigChanged();
    }

    /// <summary>Rebuild texture after an external config load.</summary>
    public void ReloadFromConfig()
    {
        BuildTexture();
        RefreshTexture();
    }
}

// ── Editor mode enum ──────────────────────────────────────────────────────────
public enum EditorMode { Paint, Place }
