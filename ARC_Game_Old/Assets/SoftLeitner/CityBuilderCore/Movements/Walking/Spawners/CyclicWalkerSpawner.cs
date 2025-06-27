using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// cyclically spawns walkers on its own cooldown<br/>
    /// for individual walker cooldowns use <see cref="PooledWalkerSpawner{T}"/>
    /// </summary>
    /// <typeparam name="T">concrete type of the walker</typeparam>
    [Serializable]
    public class CyclicWalkerSpawner<T> : WalkerSpawner<T>
    where T : Walker
    {
        [Tooltip("time between spawns[s]")]
        public float Downtime;

        private float _currentDowntime;

        protected override void initialize()
        {
            base.initialize();

            _currentDowntime = Downtime;
        }

        public void Update(float multiplier = 1f)
        {
            if (!HasWalker)
                return;

            if (_currentDowntime > 0f)
            {
                _currentDowntime -= Time.deltaTime * multiplier;
            }
            else
            {
                spawn();
                _currentDowntime = Downtime;
            }
        }

        #region Saving
        public CyclicWalkerSpawnerData SaveData()
        {
            return new CyclicWalkerSpawnerData()
            {
                CurrentDownTime = _currentDowntime,
                Walkers = _currentWalkers.Select(w => w.SaveData()).ToArray()
            };
        }
        public void LoadData(CyclicWalkerSpawnerData data)
        {
            clearWalkers();

            _currentDowntime = data.CurrentDownTime;

            foreach (var active in data.Walkers)
            {
                reloadActive().LoadData(active);
            }
        }
    }
    [Serializable]
    public class CyclicWalkerSpawnerData
    {
        public float CurrentDownTime;
        public string[] Walkers;
    }
    #endregion
}