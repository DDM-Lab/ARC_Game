using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// spawns instances of a prefab up to a maximum number<br/>
    /// spawn position is randomly selected either from all tiles or just one specific tile on a tilemap<br/>
    /// tile positions are selected at the start for performance reasons so changing the tilemap at runtime wont have an effect on the spawner
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_tilemap_spawner.html")]
    [RequireComponent(typeof(Tilemap))]
    public class TilemapSpawner : ExtraDataBehaviour
    {
        [Tooltip("specific tile instances are spawned on, leave empty to spawn on all tiles of the map")]
        public TileBase Tile;
        [Tooltip("maximum number of instances")]
        public int Maximum;
        [Tooltip("number of seconds between spawns")]
        public float Interval;
        [Tooltip("prefab that gets instantiated")]
        public GameObject Prefab;

        private List<Vector2Int> _points = new List<Vector2Int>();
        private List<GameObject> _instances = new List<GameObject>();
        private float _time;

        private void Awake()
        {
            var tilemap = GetComponent<Tilemap>();
            foreach (var position in tilemap.cellBounds.allPositionsWithin)
            {
                if (Tile)
                {
                    if (tilemap.GetTile(position) != Tile)
                        continue;
                }
                else
                {
                    if (!tilemap.HasTile(position))
                        continue;
                }

                _points.Add((Vector2Int)position);
            }
        }

        private void Update()
        {
            _time += Time.deltaTime;
            if (_time >= Interval)
            {
                _time = 0f;
                checkSpawning();
            }
        }

        private void checkSpawning()
        {
            cleanup();

            if (_instances.Count < Maximum)
                spawn();
        }

        private void cleanup()
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (!_instances[i])
                    _instances.RemoveAt(i);
            }
        }

        private void spawn()
        {
            var point = _points.Random();
            var instance = Instantiate(Prefab, Dependencies.Get<IGridPositions>().GetWorldPosition(point), Quaternion.identity, transform);

            var walker = instance.GetComponent<Walker>();
            if (walker)
                walker.Initialize(null, point);

            _instances.Add(instance);
        }

        #region Saving
        [Serializable]
        public class StructureSpawnerData
        {
            public string Key;
            public float Time;
            public StructureSpawnerInstanceData[] Instances;
        }
        [Serializable]
        public class StructureSpawnerInstanceData
        {
            public Vector3 Position;
            public string Data;
        }

        public override string SaveData()
        {
            cleanup();

            return JsonUtility.ToJson(new StructureSpawnerData()
            {
                Key = Key,
                Time = _time,
                Instances = _instances.Select(i => new StructureSpawnerInstanceData()
                {
                    Position = i.transform.position,
                    Data = i.GetComponent<ISaveData>()?.SaveData()
                }).ToArray()
            });
        }

        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<StructureSpawnerData>(json);

            _instances.ForEach(i => Destroy(i));
            _instances.Clear();

            _time = data.Time;

            foreach (var instanceData in data.Instances)
            {
                var instance = Instantiate(Prefab, instanceData.Position, Quaternion.identity, transform);
                if (!string.IsNullOrWhiteSpace(instanceData.Data))
                    instance.GetComponent<ISaveData>().LoadData(instanceData.Data);

                var walker = instance.GetComponent<Walker>();
                if (walker)
                    Dependencies.Get<IWalkerManager>().RegisterWalker(walker);

                _instances.Add(instance);
            }
        }
        #endregion
    }
}