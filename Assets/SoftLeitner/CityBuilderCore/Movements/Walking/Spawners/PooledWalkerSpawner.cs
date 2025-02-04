using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// cyclic spawner with individual cooldowns for walkers and a spawner cooldown between walkers
    /// </summary>
    /// <typeparam name="T">concrete type of the walker</typeparam>
    [Serializable]
    public partial class PooledWalkerSpawner<T> : WalkerSpawner<T>
    where T : Walker
    {
        [Tooltip("time between spawns for a walker")]
        public float WalkerDowntime;
        [Tooltip("minimum time between spawns of different walkers")]
        public float SpawnerDowntime;

        private List<PooledWalkerSeat> _seats;
        private float _currentDowntime;

        protected override void initialize()
        {
            base.initialize();

            _currentDowntime = SpawnerDowntime;
            _seats = new List<PooledWalkerSeat>(Count);
            for (int i = 0; i < Count; i++)
            {
                _seats.Add(new PooledWalkerSeat());
            }
        }

        public void Update(float multiplier = 1f)
        {
            foreach (var seat in _seats)
            {
                if (seat.Walker == null && seat.CurrentDowntime > 0f)
                {
                    seat.CurrentDowntime -= Time.deltaTime * multiplier;
                }
            }

            if (_currentDowntime > 0f)
            {
                _currentDowntime -= Time.deltaTime * multiplier;
            }
            else
            {
                var seat = _seats.FirstOrDefault(s => s.Walker == null && s.CurrentDowntime <= 0f);
                if (seat != null)
                {
                    spawn(walker =>
                    {
                        seat.CurrentDowntime = WalkerDowntime;
                        seat.Walker = walker;
                    });
                }

                _currentDowntime = SpawnerDowntime;
            }
        }

        #region Saving
        public PooledWalkerSpawnerData SaveData()
        {
            return new PooledWalkerSpawnerData()
            {
                CurrentDowntime = _currentDowntime,
                Walkers = _currentWalkers.Select(w => w.SaveData()).ToArray(),
                Seats = _seats.Select(s => s.SaveData()).ToArray()
            };
        }
        public void LoadData(PooledWalkerSpawnerData data)
        {
            clearWalkers();

            _currentDowntime = data.CurrentDowntime;

            foreach (var active in data.Walkers)
            {
                reloadActive().LoadData(active);
            }

            for (int i = 0; i < data.Seats.Length; i++)
            {
                _seats[i].LoadData(data.Seats[i], _currentWalkers);
            }
        }
    }
    [Serializable]
    public class PooledWalkerSpawnerData
    {
        public float CurrentDowntime;
        public string[] Walkers;
        public PooledWalkerSeat.PooledWalkerSeatData[] Seats;
    }
    #endregion
}