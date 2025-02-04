using System;
using System.Collections.Generic;
using System.Linq;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class for <see cref="PooledWalkerSpawner{T}"/> that holds the cooldown for each individual walker
    /// </summary>
    public class PooledWalkerSeat
    {
        private Walker _walker;
        public Walker Walker
        {
            get
            {
                return _walker;
            }
            set
            {
                if (_walker)
                    _walker.Finished -= walkerFinished;
                _walker = value;
                if (_walker)
                    _walker.Finished += walkerFinished;
            }
        }

        private float _currentDowntime;
        public float CurrentDowntime
        {
            get
            {
                return _currentDowntime;
            }
            set
            {
                _currentDowntime = value;
            }
        }

        private void walkerFinished(Walker obj)
        {
            Walker = null;
        }

        #region Saving
        [Serializable]
        public class PooledWalkerSeatData
        {
            public string WalkerId;
            public float CurrentDowntime;
        }

        public PooledWalkerSeatData SaveData() => new PooledWalkerSeatData()
        {
            WalkerId = (Walker ? Walker.Id : Guid.Empty).ToString(),
            CurrentDowntime = CurrentDowntime
        };
        public void LoadData(PooledWalkerSeatData data, IEnumerable<Walker> walkers)
        {
            Walker = walkers.FirstOrDefault(w => w.Id == new Guid(data.WalkerId));
            CurrentDowntime = data.CurrentDowntime;
        }
        #endregion
    }
}