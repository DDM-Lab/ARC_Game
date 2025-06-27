using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace CityBuilderCore.Editor
{
    /// <summary>
    /// stores various resource used by the setup windows when generating a new game
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core._editor_1_1_setup_generator.html")]
    public class SetupGenerator : ScriptableObject
    {
        public const string FOLDER_CITY = "City";
        public const string FOLDER_ROADS = "Roads";
        public const string FOLDER_BUILDINGS = "Buildings";
        public const string FOLDER_WALKERS = "Walkers";
        public const string FOLDER_ITEMS = "Items";
        public const string FOLDER_SCENES = "Scenes";
        public const string FOLDER_PALETTE = "Palette";
        public const string FOLDER_MODELS = "Models";
        public const string FOLDER_SPRITES = "Sprites";
        public const string FOLDER_MATERIALS = "Materials";
        public const string FOLDER_UI = "UI";
        public const string FOLDER_TERRAIN = "Terrain";

        public static string FOLDER_CITY_ROADS => Path.Combine(FOLDER_CITY, FOLDER_ROADS);
        public static string FOLDER_CITY_BUILDINGS => Path.Combine(FOLDER_CITY, FOLDER_BUILDINGS);
        public static string FOLDER_CITY_WALKERS => Path.Combine(FOLDER_CITY, FOLDER_WALKERS);
        public static string FOLDER_CITY_ITEMS => Path.Combine(FOLDER_CITY, FOLDER_ITEMS);

        private static SetupGenerator _instance;
        public static SetupGenerator Instance
        {
            get
            {
                if (_instance == null)
                {
                    var script = Resources.FindObjectsOfTypeAll<MonoScript>().Single(s => s.GetClass() == typeof(SetupGenerator));
                    var directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(script));

                    InternalEditorUtility.LoadSerializedFileAndForget(Path.Combine(directory, "SetupGenerator.asset"));
                }

                return _instance;
            }
        }

        public SceneAsset Scene;

        [Header("Palette")]
        public Grid GroundPalette;
        public Sprite RectBlank;
        public Sprite HexBlank;
        public Sprite IsoBlank;
        public TileBase InfoTile;
        public TileBase ValidTile;
        public TileBase InvalidTile;
        public TileBase GroundTile;
        public TileBase GroundBlockedTile;
        public TileBase RoadTile;
        public ObjectTile RoadObjectTile;
        public Mesh RoadHexMeshXY;
        [Header("Building")]
        public Building Building;
        public BuildingInfo BuildingInfo;
        public Mesh BuildingMesh;
        public Sprite BuildingSprite;
        public Sprite BuildingSpriteIso;
        [Header("Walker")]
        public GameObject Walker;
        public WalkerInfo WalkerInfo;
        public Mesh WalkerMesh;
        public Sprite WalkerSprite;
        public Sprite WalkerSpriteIso;
        [Header("UI")]
        public RenderTexture MinimapTexture;
        public TMPro.TMP_FontAsset Font;
        [Header("Materials")]
        public Material GroundMaterial;
        public Material BlockedMaterial;
        public Material RoadMaterial;
        [Header("Terrain")]
        public TerrainLayer GroundLayer;
        public TerrainLayer RoadLayer;

        private SetupModel _model;

        public SetupGenerator()
        {
            _instance = this;
        }

        public void Execute(SetupModel model)
        {
            _model = model;

            setupFolders();

            EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(copyAsset(Scene, FOLDER_SCENES, "Main.unity")));

            TileBase roadTile = null;

            setupUI();
            setupCamera();

            if (_model.MapDisplay == SetupModel.MapDisplayMode.Terrain)
            {
                setupTerrain();
            }
            else
            {
                DestroyImmediate(GameObject.Find("MapTerrain"));

                roadTile = setupGrid();
                setupMap();
            }

            var item = setupItem();
            var road = setupRoad(item, roadTile);
            var walker = setupWalker();
            var building = setupBuilding(walker, item);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(GameObject.Find("Logic").scene);
        }

        private void setupFolders()
        {
            if (!string.IsNullOrWhiteSpace(_model.Directory))
                createFolder("Assets", _model.Directory);
            createFolder(_model.AssetDirectory, FOLDER_SCENES);

            createFolder(_model.AssetDirectory, FOLDER_CITY);
            createFolder(_model.AssetDirectory, FOLDER_CITY, FOLDER_ROADS);
            createFolder(_model.AssetDirectory, FOLDER_CITY, FOLDER_BUILDINGS);
            createFolder(_model.AssetDirectory, FOLDER_CITY, FOLDER_WALKERS);
            createFolder(_model.AssetDirectory, FOLDER_CITY, FOLDER_ITEMS);

            createFolder(_model.AssetDirectory, FOLDER_PALETTE);
            if (_model.CityDisplay == SetupModel.CityDisplayMode.Mesh)
                createFolder(_model.AssetDirectory, FOLDER_MODELS);
            createFolder(_model.AssetDirectory, FOLDER_SPRITES);
            createFolder(_model.AssetDirectory, FOLDER_MATERIALS);
            createFolder(_model.AssetDirectory, FOLDER_UI);
            if (_model.MapDisplay == SetupModel.MapDisplayMode.Terrain)
                createFolder(_model.AssetDirectory, FOLDER_TERRAIN);
        }

        private void setupCamera()
        {
            var two = GameObject.Find("2DCamera");
            var tre = GameObject.Find("3DCamera");

            var twoPos = two.transform.GetChild(0);

            var trePiv = tre.transform.GetChild(0);
            var trePos = trePiv.transform.GetChild(0);

            var twoCam = twoPos.GetComponent<Camera>();
            var treCam = trePos.GetComponent<Camera>();

            var twoCon = twoCam.GetComponent<CameraController>();
            var treCon = treCam.GetComponent<CameraController>();

            if (_model.MapAxis == SetupModel.MapAxisMode.XY)
            {
                two.transform.rotation = Quaternion.identity;
                tre.transform.rotation = Quaternion.Euler(270, 0, 0);
            }
            else
            {
                two.transform.rotation = Quaternion.Euler(90, 0, 0);
                tre.transform.rotation = Quaternion.identity;
            }

            var x = _model.MapSize.x / 2f * _model.Scale;
            var y = _model.MapSize.y / 2f * _model.Scale;

            if (_model.MapLayout == SetupModel.MapLayoutMode.Isometric)
            {
                x = 0;
                y /= 2f;
            }

            twoCon.MaxZoom = y;
            treCon.MaxZoom = y;

            twoCon.ZoomSpeed = twoCon.MaxZoom;
            treCon.ZoomSpeed = treCon.MaxZoom;

            twoPos.localPosition = new Vector3(x, y);
            twoCam.orthographicSize = y;

            trePiv.localPosition = new Vector3(x, 0, y);
            trePos.localPosition = new Vector3(0, 0, -y);

            if (_model.CityDisplay == SetupModel.CityDisplayMode.Mesh)
            {
                DestroyImmediate(two.gameObject);
                tre.SetActive(true);
                tre.name = "Camera";
            }
            else
            {
                two.SetActive(true);
                two.name = "Camera";
                DestroyImmediate(tre.gameObject);

                if (_model.MapDisplay == SetupModel.MapDisplayMode.Sprite)
                    DestroyImmediate(two.GetComponentInChildren<Light>().gameObject);
            }

            var miniTex = copyAsset(MinimapTexture, FOLDER_UI, "MinimapTexture.renderTexture");
            var miniMap = this.FindObject<Minimap>();
            var miniCam = GameObject.Find("MinimapCamera").GetComponent<Camera>();
            miniCam.targetTexture = miniTex;
            miniMap.Image.texture = miniCam.targetTexture;
            miniMap.Camera = miniCam;
            miniMap.Camera.orthographic = _model.MapDisplay == SetupModel.MapDisplayMode.Sprite;

            if (_model.MapLayout == SetupModel.MapLayoutMode.Isometric && _model.CityDisplay == SetupModel.CityDisplayMode.Sprite)
            {
                if (_model.MapAxis == SetupModel.MapAxisMode.XY)
                    twoCon.SortAxis = new Vector3(0, 1, 0);
                else
                    twoCon.SortAxis = new Vector3(0, 0, 1);
            }
        }

        private TileBase setupGrid()
        {
            Sprite sprite = null;

            switch (_model.MapLayout)
            {
                case SetupModel.MapLayoutMode.Rectangle:
                    sprite = copyAsset(RectBlank, FOLDER_PALETTE);
                    if (_model.Scale != 1)
                        changePixelsPerUnit(sprite, 4 / _model.Scale);
                    break;
                case SetupModel.MapLayoutMode.Hexagon:
                case SetupModel.MapLayoutMode.HexagonFlatTop:
                    sprite = copyAsset(HexBlank, FOLDER_PALETTE);
                    if (_model.Scale != 1)
                        changePixelsPerUnit(sprite, 128 / _model.Scale);
                    break;
                case SetupModel.MapLayoutMode.Isometric:
                    sprite = copyAsset(IsoBlank, FOLDER_PALETTE);
                    if (_model.Scale != 1)
                        changePixelsPerUnit(sprite, 128 / _model.Scale);
                    break;
            }

            var infoTile = copyAsset(InfoTile, FOLDER_PALETTE);
            var validTile = copyAsset(ValidTile, FOLDER_PALETTE);
            var invalidTile = copyAsset(InvalidTile, FOLDER_PALETTE);
            var groundTile = copyAsset(GroundTile, FOLDER_PALETTE);
            var groundBlockedTile = copyAsset(GroundBlockedTile, FOLDER_PALETTE);

            TileBase roadTile;
            if (_model.MapDisplay == SetupModel.MapDisplayMode.Sprite)
            {
                roadTile = copyAsset(RoadTile, FOLDER_PALETTE);
            }
            else
            {
                var roadObjectTile = copyAsset(RoadObjectTile, FOLDER_PALETTE);
                roadObjectTile.Prefab = copyAsset(RoadObjectTile.Prefab, FOLDER_PALETTE);
                var scale = _model.CellSize;
                if (_model.IsHexagonal)
                {
                    roadObjectTile.Prefab.GetComponent<MeshFilter>().mesh = RoadHexMeshXY;
                }
                else
                {
                    scale = new Vector3(scale.x, scale.y, scale.z / 100f);
                }
                roadObjectTile.Prefab.transform.localScale = scale;
                roadTile = roadObjectTile;

                roadObjectTile.Prefab.GetComponent<MeshRenderer>().material = copyAsset(RoadMaterial, FOLDER_MATERIALS);
            }

            var tiles = new[] { infoTile, validTile, invalidTile, groundTile, groundBlockedTile, roadTile };

            foreach (var tile in tiles.OfType<Tile>())
            {
                tile.sprite = sprite;
                EditorUtility.SetDirty(tile);
            }

            var highlightManager = this.FindObject<DefaultHighlightManager>();
            highlightManager.InfoTile = infoTile;
            highlightManager.ValidTile = validTile;
            highlightManager.InvalidTile = invalidTile;

            var grid = this.FindObject<Grid>();

            var ground = GameObject.Find("Ground");
            var groundTiles = ground.GetComponent<Tilemap>();

            grid.cellLayout = _model.CellLayout;
            grid.cellSwizzle = _model.CellSwizzle;
            grid.cellSize = _model.CellSize;

            DefaultMap map;

            switch (_model.MapLayout)
            {
                case SetupModel.MapLayoutMode.Hexagon:
                case SetupModel.MapLayoutMode.HexagonFlatTop:
                    map = grid.gameObject.AddComponent<HexagonMap>();
                    break;
                case SetupModel.MapLayoutMode.Isometric:
                    map = grid.gameObject.AddComponent<IsometricMap>();
                    break;
                default:
                    map = grid.gameObject.AddComponent<DefaultMap>();
                    break;
            }

            map.Size = _model.MapSize;
            map.Ground = groundTiles;
            map.WalkingBlockingTiles = new TileBase[] { groundBlockedTile };
            map.BuildingBlockingTiles = new BlockingTile[] { new BlockingTile() { Level = new StructureLevelMask(), Tile = groundBlockedTile } };

            for (int x = 0; x < _model.MapSize.x; x++)
            {
                for (int y = 0; y < _model.MapSize.y; y++)
                {
                    Vector3Int position;
                    if (_model.MapLayout == SetupModel.MapLayoutMode.HexagonFlatTop)
                        position = new Vector3Int(y, x, 0);
                    else
                        position = new Vector3Int(x, y, 0);

                    groundTiles.SetTile(position, y < _model.MapSize.y / 4 ? groundBlockedTile : groundTile);
                }
            }

            foreach (var tilemap in this.FindObjects<Tilemap>())
            {
                tilemap.orientation = _model.TilemapOrientation;
                tilemap.tileAnchor = _model.TilemapAnchor;
            }

            var palette = copyAsset(GroundPalette, FOLDER_PALETTE, "Ground.prefab");

            var paletteMap = palette.GetComponentInChildren<Tilemap>();

            paletteMap.SetTile(new Vector3Int(0, 0, 0), groundTile);
            paletteMap.SetTile(new Vector3Int(1, 0, 0), groundBlockedTile);

            return roadTile;
        }

        private void setupMap()
        {
            var ground = GameObject.Find("Ground");
            var map = GameObject.Find("Map");

            var walkable = map.transform.Find("Walkable");
            var blocked = map.transform.Find("Blocked");

            var groundTiles = ground.GetComponent<TilemapRenderer>();

            groundTiles.enabled = _model.MapDisplay == SetupModel.MapDisplayMode.Sprite;

            if (_model.MapDisplay == SetupModel.MapDisplayMode.Sprite)
            {
                DestroyImmediate(map);
            }
            else
            {
                var groundMaterial = copyAsset(GroundMaterial, FOLDER_MATERIALS);
                var blockedMaterial = copyAsset(BlockedMaterial, FOLDER_MATERIALS);

                walkable.GetComponent<MeshRenderer>().material = groundMaterial;
                blocked.GetComponent<MeshRenderer>().material = blockedMaterial;

                map.transform.rotation = Quaternion.Euler(_model.MapAxis == SetupModel.MapAxisMode.XY ? 0 : 90, 0, 0);

                if (_model.IsHexagonal)
                {
                    if (_model.MapLayout == SetupModel.MapLayoutMode.Hexagon)
                        map.transform.localScale = new Vector3(1.0f, 0.75f, 1.0f);
                    else
                        map.transform.localScale = new Vector3(0.75f, 1.0f, 1.0f);
                }

                walkable.localPosition = new Vector3(_model.MapSize.x * _model.Scale / 2f, _model.MapSize.y * _model.Scale / 2f, 0);
                walkable.localScale = new Vector3(_model.MapSize.x * _model.Scale / 10f, 1, _model.MapSize.y * _model.Scale / 10f);

                blocked.localPosition = new Vector3(walkable.localPosition.x, walkable.localPosition.y / 4f, -_model.MapSize.y * _model.Scale / 32f);
                blocked.localScale = new Vector3(_model.MapSize.x * _model.Scale, _model.MapSize.y * _model.Scale / 4f, _model.MapSize.y * _model.Scale / 16f);
            }
        }

        private void setupTerrain()
        {
            DestroyImmediate(GameObject.Find("Grid"));
            DestroyImmediate(GameObject.Find("Map"));

            var mapTerrainObject = GameObject.Find("MapTerrain");
            var terrainObject = GameObject.Find("Terrain");
            var terrain = terrainObject.GetComponent<Terrain>();
            var terrainCollider = terrainObject.GetComponent<TerrainCollider>();
            var terrainMap = mapTerrainObject.GetComponent<TerrainMap>();
            var terrainHeight = mapTerrainObject.GetComponent<DefaultMapHeight>();

            var height = _model.MapSize.x / 2 * _model.Scale;

            mapTerrainObject.name = "Map";

            terrainHeight.MapHeight = -height / 2f;
            terrainHeight.RoadHeight = -height / 2f;

            terrainObject.transform.position = new Vector3(0, -height / 2f, 0);

            terrainMap.Size = _model.MapSize;
            terrainMap.MinHeight = height / 2 - 1;
            terrainMap.MaxHeight = height / 2 + 1;

            terrain.terrainData = copyAsset(terrain.terrainData, FOLDER_TERRAIN);
            terrain.terrainData.size = new Vector3(_model.MapSize.x * _model.Scale, height, _model.MapSize.y * _model.Scale);

            var heights = new float[terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution];
            for (int x = 0; x < terrain.terrainData.heightmapResolution; x++)
            {
                for (int y = 0; y < terrain.terrainData.heightmapResolution; y++)
                {
                    heights[x, y] = 0.5f;
                }
            }
            terrain.terrainData.SetHeights(0, 0, heights);

            terrainCollider.terrainData = terrain.terrainData;

            var grid = mapTerrainObject.GetComponent<Grid>();

            grid.cellLayout = _model.CellLayout;
            grid.cellSwizzle = _model.CellSwizzle;
            grid.cellSize = _model.CellSize;

            var highlights = this.FindObject<SpriteHighlightManager>();
            highlights.Prefab.sprite = copyAsset(highlights.Prefab.sprite, FOLDER_SPRITES, "blank.png");
            highlights.Prefab.transform.localScale *= _model.Scale;

            var ground = copyAsset(GroundLayer, FOLDER_TERRAIN);
            var road = copyAsset(RoadLayer, FOLDER_TERRAIN);

            ground.diffuseTexture = copyAsset(ground.diffuseTexture, FOLDER_TERRAIN);
            road.diffuseTexture = copyAsset(road.diffuseTexture, FOLDER_TERRAIN);

            terrain.terrainData.terrainLayers = new TerrainLayer[]
            {
                ground,
                road,
            };
        }

        private Road setupRoad(Item item, TileBase roadTile)
        {
            var road = CreateInstance<Road>();
            road.Key = "ROD";
            road.Name = "Road";
            road.Cost = new[] { new ItemQuantity(item, 1) };
            road.Stages = new[] { new RoadStage() { Tile = roadTile, Index = 1 } };

            AssetDatabase.CreateAsset(road, _model.GetAssetPath("Road.asset", FOLDER_CITY_ROADS));

            var roads = CreateInstance<RoadSet>();
            roads.Objects = new[] { road };

            AssetDatabase.CreateAsset(roads, _model.GetAssetPath("Roads.asset", FOLDER_CITY_ROADS));
            this.FindObject<ObjectRepository>().Roads = roads;

            this.FindObject<RoadBuilder>().Road = road;

            return road;
        }

        private Item setupItem()
        {
            var item = CreateInstance<Item>();
            item.Key = "ITM";
            item.Name = "Item";
            item.UnitSize = 1;

            AssetDatabase.CreateAsset(item, _model.GetAssetPath("Item.asset", FOLDER_CITY_ITEMS));

            var items = CreateInstance<ItemSet>();
            items.Objects = new[] { item };

            AssetDatabase.CreateAsset(items, _model.GetAssetPath("Items.asset", FOLDER_CITY_ITEMS));
            this.FindObject<ObjectRepository>().Items = items;

            this.FindObject<DefaultItemManager>().StartItems = new[] { new ItemQuantity(item, 100) };
            this.FindObject<InventoryVisualizer>().Item = item;

            return item;
        }

        private Building setupBuilding(GameObject walker, Item item)
        {
            AssetDatabase.DeleteAsset(_model.GetAssetPath("Building.prefab", FOLDER_CITY_BUILDINGS));//delete previously created asset to work around invalid prefab error

            var buildingBase = copyAsset(Building, FOLDER_CITY_BUILDINGS, "BuildingBase.prefab");
            var buildingInfo = copyAsset(BuildingInfo, FOLDER_CITY_BUILDINGS, "BuildingInfo.asset");

            buildingBase.Info = null;
            buildingInfo.Cost = new[] { new ItemQuantity(item, 10) };

            var pivotXY = buildingBase.transform.Find("PivotXY");
            var pivotXZ = buildingBase.transform.Find("PivotXZ");

            var buildingSprite = copyAsset(_model.MapLayout == SetupModel.MapLayoutMode.Isometric ? BuildingSpriteIso : BuildingSprite, FOLDER_SPRITES, "building.png");
            if (_model.Scale != 1)
                changePixelsPerUnit(buildingSprite, 16 / _model.Scale);

            if (_model.CityDisplay == SetupModel.CityDisplayMode.Mesh)
            {
                var buildingMesh = copyAsset(BuildingMesh, FOLDER_MODELS, "building.fbx");
                foreach (var sprite in buildingBase.GetComponentsInChildren<SpriteRenderer>())
                {
                    DestroyImmediate(sprite.gameObject, true);
                }
                foreach (var mesh in buildingBase.GetComponentsInChildren<MeshFilter>())
                {
                    mesh.mesh = buildingMesh;
                }
            }
            else
            {
                foreach (var sprite in buildingBase.GetComponentsInChildren<SpriteRenderer>())
                {
                    sprite.sprite = buildingSprite;
                }
                foreach (var mesh in buildingBase.GetComponentsInChildren<MeshFilter>())
                {
                    DestroyImmediate(mesh.gameObject, true);
                }
            }

            if (_model.MapAxis == SetupModel.MapAxisMode.XY)
            {
                DestroyImmediate(pivotXZ.gameObject, true);
                pivotXY.name = "Pivot";
                buildingBase.Pivot = pivotXY;
            }
            else
            {
                DestroyImmediate(pivotXY.gameObject, true);
                pivotXZ.name = "Pivot";
                buildingBase.Pivot = pivotXZ;
            }

            buildingBase.Pivot.localPosition *= _model.Scale;
            if (_model.MapLayout == SetupModel.MapLayoutMode.Isometric)
                buildingBase.Pivot.localPosition = new Vector3(0, buildingBase.Pivot.localPosition.y * 0.5f, buildingBase.Pivot.localPosition.z * 0.5f);
            else if (_model.IsHexagonal)
                buildingBase.Pivot.localPosition = Vector3.zero;

            if (_model.CityDisplay == SetupModel.CityDisplayMode.Mesh)
                buildingBase.Pivot.localScale *= _model.Scale;

            var building = ((GameObject)PrefabUtility.InstantiatePrefab(buildingBase.gameObject)).GetComponent<Building>();
            building.Info = buildingInfo;
            var walkerComponent = building.gameObject.AddComponent<WalkerComponent>();
            walkerComponent.CyclicRoamingWalkers = new CyclicRoamingWalkerSpawner() { Prefab = walker.GetComponent<RoamingWalker>() };

            var buildingPrefab = PrefabUtility.SaveAsPrefabAsset(building.gameObject, _model.GetAssetPath("Building.prefab", FOLDER_CITY_BUILDINGS)).GetComponent<Building>();
            buildingInfo.Prefab = buildingPrefab;

            EditorUtility.SetDirty(buildingInfo);

            DestroyImmediate(building.gameObject);

            var buildings = CreateInstance<BuildingInfoSet>();
            buildings.Objects = new[] { buildingInfo };

            AssetDatabase.CreateAsset(buildings, _model.GetAssetPath("Buildings.asset", FOLDER_CITY_BUILDINGS));
            this.FindObject<ObjectRepository>().Buildings = buildings;

            this.FindObject<BuildingBuilder>().BuildingInfo = buildingInfo;
            this.FindObject<BuildingBuilder>().GetComponentInChildren<Image>().sprite = buildingSprite;
            this.FindObject<DemolishTool>().GetComponentInChildren<Image>().sprite = buildingSprite;

            return buildingPrefab;
        }

        private GameObject setupWalker()
        {
            AssetDatabase.DeleteAsset(_model.GetAssetPath("Walker.prefab", FOLDER_CITY_WALKERS));//delete previously created asset to work around invalid prefab error

            var walkerBase = copyAsset(Walker, FOLDER_CITY_WALKERS, "WalkerBase.prefab");
            var walkerInfo = copyAsset(WalkerInfo, FOLDER_CITY_WALKERS, "WalkerInfo.asset");

            var pivotXY = walkerBase.transform.Find("PivotXY");
            var pivotXZ = walkerBase.transform.Find("PivotXZ");

            var walkerSprite = copyAsset(_model.MapLayout == SetupModel.MapLayoutMode.Isometric ? WalkerSpriteIso : WalkerSprite, FOLDER_SPRITES, "walker.png");
            if (_model.Scale != 1)
                changePixelsPerUnit(walkerSprite, 16 / _model.Scale);

            if (_model.CityDisplay == SetupModel.CityDisplayMode.Mesh)
            {
                var walkerMesh = copyAsset(WalkerMesh, FOLDER_MODELS, "walker.fbx");
                foreach (var sprite in walkerBase.GetComponentsInChildren<SpriteRenderer>())
                {
                    DestroyImmediate(sprite.gameObject, true);
                }
                foreach (var mesh in walkerBase.GetComponentsInChildren<MeshFilter>())
                {
                    mesh.mesh = walkerMesh;
                }
            }
            else
            {
                foreach (var sprite in walkerBase.GetComponentsInChildren<SpriteRenderer>())
                {
                    sprite.sprite = walkerSprite;
                }
                foreach (var mesh in walkerBase.GetComponentsInChildren<MeshFilter>())
                {
                    DestroyImmediate(mesh.gameObject, true);
                }
            }

            if (_model.MapAxis == SetupModel.MapAxisMode.XY)
            {
                DestroyImmediate(pivotXZ.gameObject, true);
                pivotXY.name = "Pivot";
            }
            else
            {
                DestroyImmediate(pivotXY.gameObject, true);
                pivotXZ.name = "Pivot";
            }

            var walker = ((GameObject)PrefabUtility.InstantiatePrefab(walkerBase.gameObject)).AddComponent<RoamingWalker>();
            walker.Info = walkerInfo;
            walker.Pivot = walker.transform.Find("Pivot");

            walker.Pivot.localPosition *= _model.Scale;
            if (_model.MapLayout == SetupModel.MapLayoutMode.Isometric)
                walker.Pivot.localPosition = new Vector3(0, walker.Pivot.localPosition.y * 0.5f, walker.Pivot.localPosition.z * 0.5f);
            else if (_model.IsHexagonal)
                walker.Pivot.localPosition = Vector3.zero;
            if (_model.CityDisplay == SetupModel.CityDisplayMode.Mesh)
                walker.Pivot.localScale *= _model.Scale;

            walker.Info = walkerInfo;
            walker.Info.Speed *= _model.Scale;

            var walkerPrefab = PrefabUtility.SaveAsPrefabAsset(walker.gameObject, _model.GetAssetPath("Walker.prefab", FOLDER_CITY_WALKERS));
            walkerInfo.Prefab = walkerPrefab;

            EditorUtility.SetDirty(walkerInfo);

            DestroyImmediate(walker.gameObject);

            var walkers = CreateInstance<WalkerInfoSet>();
            walkers.Objects = new[] { walkerInfo };

            AssetDatabase.CreateAsset(walkers, _model.GetAssetPath("Walkers.asset", FOLDER_CITY_WALKERS));
            this.FindObject<ObjectRepository>().Walkers = walkers;

            this.FindObject<RoadBuilder>().GetComponentInChildren<Image>().sprite = walkerSprite;

            return walkerPrefab;
        }

        private void setupUI()
        {
            var font = copyAsset(Font, FOLDER_UI, "DefaultFont.asset");

            foreach (var text in this.FindObjects<TMPro.TMP_Text>())
            {
                text.font = font;
            }
        }

        private void createFolder(string parent, string name)
        {
            if (AssetDatabase.IsValidFolder(Path.Combine(parent, name)))
                return;
            AssetDatabase.CreateFolder(parent, name);
        }

        private void createFolder(string parent, string subfolder, string name)
        {
            if (AssetDatabase.IsValidFolder(Path.Combine(parent, subfolder, name)))
                return;
            AssetDatabase.CreateFolder(Path.Combine(parent, subfolder), name);
        }

        private T copyAsset<T>(T asset, string folder, string name = null) where T : Object
        {
            string originalPath = AssetDatabase.GetAssetPath(asset);
            string copyPath = _model.GetAssetPath(name ?? Path.GetFileName(originalPath), folder);
            AssetDatabase.CopyAsset(originalPath, copyPath);
            return AssetDatabase.LoadAssetAtPath<T>(copyPath);
        }

        private void changePixelsPerUnit(Sprite sprite, float value)
        {
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
            TextureImporterSettings texSettings = new TextureImporterSettings();
            ti.ReadTextureSettings(texSettings);
            texSettings.spritePixelsPerUnit = value;
            ti.SetTextureSettings(texSettings);
            ti.SaveAndReimport();
        }
    }
}
