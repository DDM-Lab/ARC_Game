using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// roams around and reduces the risks of <see cref="IRiskRecipient"/> while it is in range<br/>
    /// when a fire breaks out in its vicinity(collider enter) it runs there and tries to extinguish it
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_fire_walker.html")]
    public class FireWalker : RiskWalker
    {
        public enum FireWalkerState
        {
            Prevention = 0,
            ToFire = 1,
            Extinguishing = 2,
            FromFire = 3
        }

        [Tooltip("the kind of pathing the walker will use when extinguishing fires")]
        public PathType ExtinguishPathType;
        [Tooltip("optional parameter for pathfinding, depends on PathType\nfor example a road for road pathing to only walk on that specific road when extinguishing fires")]
        public Object ExtinguisPathTag;
        [Tooltip("how mush is extinguished per second")]
        public float ExtinguishAmount;
        [Tooltip("distance the walker moves per second when moving to and from fires")]
        public float ExtinguishSpeed;
        [Tooltip("fired when the walker state changes, the value is whether the walker is running to or from fire, useful for animation")]
        public BoolEvent IsRunningChanged;
        [Tooltip("fired when the walker state changes, the value is whether the walker is currently extinguishing, useful for animation")]
        public BoolEvent IsExtinguishingChanged;

        public override float Speed => _fireState == FireWalkerState.Prevention ? base.Speed : ExtinguishSpeed;

        private FireWalkerState _fireState;
        private Vector2Int _currentRoaming;
        private BuildingReference _candidate;
        private BuildingReference _target;

        protected override void Update()
        {
            base.Update();

            if (_fireState == FireWalkerState.Extinguishing)
            {
                var fire = _target.HasInstance ? _target.Instance.GetAddon<FireAddon>(null) : null;
                if (fire == null)
                    headBack();
                else
                    fire.Extinguish(ExtinguishAmount * Time.deltaTime);
            }
        }

        private void extinguish()
        {
            _fireState = FireWalkerState.Extinguishing;
            onFireStateChanged();
        }

        private void headBack()
        {
            _fireState = FireWalkerState.FromFire;
            onFireStateChanged();

            var path = PathHelper.FindPath(_current, Home.Instance, ExtinguishPathType, ExtinguisPathTag);
            if (path == null)
                onFinished();
            else
                Walk(path);
        }

        private void resume()
        {
            _fireState = FireWalkerState.Prevention;
            onFireStateChanged();
            ContinueRoam(Memory, Range, onRoamFinished);
        }

        private void onFireStateChanged()
        {
            IsRunningChanged?.Invoke(_fireState == FireWalkerState.ToFire || _fireState == FireWalkerState.FromFire);
            IsExtinguishingChanged?.Invoke(_fireState == FireWalkerState.Extinguishing);
        }

        private void checkCandidate()
        {
            if (_candidate == null)
                return;

            if (_state != RoamingWalkerState.Inactive)
            {
                var path = PathHelper.FindPath(_current, _candidate.Instance, ExtinguishPathType, ExtinguisPathTag);
                if (path != null)
                {
                    StopAllCoroutines();
                    _currentRoaming = _current;
                    _target = _candidate;
                    _fireState = FireWalkerState.ToFire;
                    onFireStateChanged();
                    this.DelayToEnd(() => Walk(path, extinguish));
                }
            }

            _candidate = null;
        }

        private void OnTriggerEnter2D(Collider2D collider) => enter(collider);
        private void OnTriggerEnter(Collider collider) => enter(collider);
        private void enter(Component collider)
        {
            if (_fireState == FireWalkerState.Prevention && _state != RoamingWalkerState.Inactive && collider.gameObject.name != "Fire")
            {
                var fire = collider.GetComponent<FireAddon>();
                if (fire && fire.Building != null)
                {
                    _candidate = fire.Building.BuildingReference;

                    if (_state == RoamingWalkerState.Roaming && CurrentRoaming.Moved < 0.02)
                        checkCandidate();
                }
            }
        }

        protected override void onMoved(Vector2Int point)
        {
            base.onMoved(point);

            checkCandidate();
        }

        protected override void onFinished()
        {
            _fireState = FireWalkerState.Prevention;
            base.onFinished();
        }

        #region Saving
        [System.Serializable]
        public class FireWalkerData
        {
            public WalkerData WalkerData;
            public int RoamingState;
            public int FireState;
            public Vector2Int CurrentRoaming;
            public string TargetId;
        }

        public override string SaveData()
        {
            string targetId = string.Empty;
            if (_target != null && _target.HasInstance)
                targetId = _target.Instance.Id.ToString();

            return JsonUtility.ToJson(new FireWalkerData()
            {
                WalkerData = savewalkerData(),
                RoamingState = (int)_state,
                FireState = (int)_fireState,
                CurrentRoaming = _currentRoaming,
                TargetId = targetId,
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<FireWalkerData>(json);

            loadWalkerData(data.WalkerData);

            _state = (RoamingWalkerState)data.RoamingState;
            _fireState = (FireWalkerState)data.FireState;
            _currentRoaming = data.CurrentRoaming;

            if (!string.IsNullOrWhiteSpace(data.TargetId))
                _target = Dependencies.Get<IBuildingManager>().GetBuildingReference(new System.Guid(data.TargetId));

            switch (_fireState)
            {
                case FireWalkerState.Prevention:
                    switch (_state)
                    {
                        case RoamingWalkerState.Roaming:
                            ContinueRoam(Memory, Range, onRoamFinished);
                            break;
                        case RoamingWalkerState.Waiting:
                            onRoamFinished();
                            break;
                        case RoamingWalkerState.Returning:
                            ContinueWalk();
                            break;
                    }
                    break;
                case FireWalkerState.ToFire:
                    ContinueWalk(extinguish);
                    break;
                case FireWalkerState.Extinguishing:
                    break;
                case FireWalkerState.FromFire:
                    ContinueWalk();
                    break;
                default:
                    break;
            }

            onFireStateChanged();
        }
        #endregion
    }
}