using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walker spawner that does not spawn on its own
    /// </summary>
    /// <typeparam name="T">concrete type of the walker</typeparam>
    [Serializable]
    public class ManualWalkerSpawner<T> : WalkerSpawner<T>
    where T : Walker
    {
        public void Spawn(Action<T> onSpawned = null, Vector2Int? start = null)
        {
            spawn(onSpawned, start);
        }
        public void Spawn<Q, P>(MonoBehaviour owner, Func<Q> preparer, Func<Q, P> planner, Action<T, P> spawner = null, Action<T> onSpawned = null, Vector2Int? start = null)
        {
            spawnPrepared(owner, () => preparer(), q => planner((Q)q), (w, p) => spawner(w, (P)p), onSpawned, start);
        }

        #region Saving
        public ManualWalkerSpawnerData SaveData()
        {
            return new ManualWalkerSpawnerData()
            {
                Walkers = _currentWalkers.Select(w => w.SaveData()).ToArray()
            };
        }
        public void LoadData(ManualWalkerSpawnerData data)
        {
            clearWalkers();

            foreach (var active in data.Walkers)
            {
                reloadActive().LoadData(active);
            }
        }
    }
    [Serializable]
    public class ManualWalkerSpawnerData
    {
        public string[] Walkers;
    }
    #endregion
}