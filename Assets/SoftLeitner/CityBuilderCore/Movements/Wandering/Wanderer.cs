using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// moves to a random adjacent point on the map then waits a little and repeats<br/>
    /// can be used for decorations and animals, used for the huntable blobs in Three
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_wanderer.html")]
    public class Wanderer : MonoBehaviour, ISaveData
    {
        public enum WandererState
        {
            Waiting = 0,
            Wandering = 10
        }

        [Tooltip("when set the wanderer will only move to these tiles")]
        public TileBase[] Tiles;
        [Tooltip("name of the tilemap that is checked for the above tiles")]
        public string Tilemap;
        [Tooltip("gets rotated accordingly when the wanderer starts moving to a new point")]
        public Transform Pivot;
        [Tooltip("wait time between moves")]
        public float Interval;
        [Tooltip("how fast the wanderer moves to an adjacent point")]
        public float Speed;
        [Tooltip("may be used to check which kind of wanderer this is")]
        public string Key;

        public IntEvent StateChanged;

        public float TimePerStep => 1 / Speed;

        private Tilemap _tilemap;
        private WandererState _state = WandererState.Waiting;
        private float _time;
        private Vector3 _start;
        private Vector3 _target;
        private float _scale;
        private IGridHeights _gridHeights;

        private void Start()
        {
            _gridHeights = Dependencies.GetOptional<IGridHeights>();

            Pivot.position += Dependencies.Get<IMap>().GetVariance();

            if (Tiles != null && Tiles.Length > 0)
                _tilemap = this.FindObjects<Tilemap>().FirstOrDefault(t => t.name == Tilemap);
        }

        private void Update()
        {
            switch (_state)
            {
                case WandererState.Waiting:
                    _time += Time.deltaTime;
                    if (_time >= Interval)
                    {
                        wander();
                        StateChanged?.Invoke((int)_state);
                    }
                    break;
                case WandererState.Wandering:
                    _time += Time.deltaTime * _scale;
                    if (_time >= TimePerStep)
                    {
                        transform.position = _target;
                        _gridHeights?.ApplyHeight(Pivot);

                        _state = WandererState.Waiting;
                        StateChanged?.Invoke((int)_state);
                    }
                    else
                    {
                        transform.position = Vector3.Lerp(_start, _target, _time / TimePerStep);
                        _gridHeights?.ApplyHeight(Pivot);
                    }
                    break;
                default:
                    break;
            }
        }

        private void wander()
        {
            var candidates = new List<Vector3>();
            var positions = Dependencies.Get<IGridPositions>();

            foreach (var position in PositionHelper.GetAdjacent(positions.GetGridPoint(transform.position), Vector2Int.one, true))
            {
                if (_tilemap == null || Tiles.Contains(_tilemap.GetTile((Vector3Int)position)))
                    candidates.Add(positions.GetWorldPosition(position));
            }

            if (candidates.Count == 0)
            {
                _state = WandererState.Waiting;
                _time = 0f;
            }
            else
            {
                _state = WandererState.Wandering;
                _time = 0f;
                _start = transform.position;
                _target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                _scale = 1 / Vector3.Distance(_start, _target);

                Dependencies.Get<IGridRotations>().SetRotation(Pivot, _target - _start);
            }
        }

        #region Saving
        [Serializable]
        public class WandererData
        {
            public Vector3 Position;
            public Quaternion Rotation;

            public int State;
            public float Time;
            public Vector3 Start;
            public Vector3 Target;
            public float Scale;
        }

        public string SaveData()
        {
            return JsonUtility.ToJson(new WandererData()
            {
                Position = transform.position,
                Rotation = Pivot.localRotation,

                State = (int)_state,
                Time = _time,
                Start = _start,
                Target = _target,
                Scale = _scale
            });
        }

        public void LoadData(string json)
        {
            var data = JsonUtility.FromJson<WandererData>(json);

            transform.position = data.Position;
            _gridHeights?.ApplyHeight(Pivot);
            Pivot.localRotation = data.Rotation;

            _state = (WandererState)data.State;
            _time = data.Time;
            _start = data.Start;
            _target = data.Target;
            _scale = data.Scale;
        }
        #endregion
    }
}