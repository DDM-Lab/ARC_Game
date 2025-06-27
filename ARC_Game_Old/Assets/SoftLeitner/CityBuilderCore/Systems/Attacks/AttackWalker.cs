using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// walks to the closest attackable and attacks it periodically until it is dead, rinse and repeat
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/attacks">https://citybuilder.softleitner.com/manual/attacks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_attack_walker.html")]
    public class AttackWalker : Walker, IAttacker, IHealther
    {
        public enum AttackWalkerState
        {
            Approaching,
            Attacking
        }

        [Tooltip("health of the walker at the start")]
        public int MaxHealth;
        [Tooltip("damage the walker does to the attackable")]
        public int Damage;
        [Tooltip("wait time before attacks")]
        public float AttackRate;
        [Tooltip("provides target position for defenders")]
        public Transform Target;
        [Tooltip("target position for health bar")]
        public Transform HealthTarget;

        [Tooltip("invoked when the walker damages an attackable")]
        public UnityEvent Attacking;

        public override float Speed
        {
            get
            {
                var speed = base.Speed;

                foreach (var addon in _addons.OfType<AttackSlowAddon>())
                {
                    speed *= addon.Factor;
                }

                return speed;
            }
        }

        public Vector3 Position => Target.position;

        public float TotalHealth => MaxHealth;
        public float CurrentHealth => _health;
        public Vector3 HealthPosition => HealthTarget.position;

        private AttackWalkerState _state;
        private BuildingComponentReference<IAttackable> _target;
        private float _attackTime;
        private int _health;

        protected override void Start()
        {
            base.Start();

            Dependencies.Get<IAttackManager>().AddAttacker(this);
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();

            Dependencies.Get<IAttackManager>().RemoveAttacker(this);
        }

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            _state = AttackWalkerState.Approaching;
            _target = null;
            _attackTime = 0f;
            _health = MaxHealth;

            approach();
        }

        public void Recalculate()
        {
            if (_state != AttackWalkerState.Approaching)
                return;

            StopAllCoroutines();

            var attackerPath = Dependencies.Get<IAttackManager>().GetAttackerPath(_current, PathType, PathTag);
            if (attackerPath == null)
                return;

            _target = attackerPath.Component;

            CurrentWalking.Recalculated(attackerPath.Path);

            ContinueWalk(attack);
        }

        private void Update()
        {
            if (_state == AttackWalkerState.Attacking)
            {
                if (_target.HasInstance)
                {
                    _attackTime += Time.deltaTime;

                    if (_attackTime >= AttackRate)
                    {
                        Attacking?.Invoke();
                        _target.Instance.Attack(Damage);
                        _attackTime = 0f;
                    }
                }
                else
                {
                    approach();
                }
            }
        }

        private void approach()
        {
            _state = AttackWalkerState.Approaching;

            var attackerPath = Dependencies.Get<IAttackManager>().GetAttackerPath(_current, PathType, PathTag);
            if (attackerPath == null)
                return;

            _target = attackerPath.Component;
            Walk(attackerPath.Path, attack);
        }

        private void attack()
        {
            _state = AttackWalkerState.Attacking;
            _attackTime = 0f;
        }

        public void Hurt(int damage)
        {
            _health -= damage;
            if (_health <= 0)
                onFinished();
        }

        public override string GetDebugText() => $"{CurrentHealth}/{MaxHealth}";

        #region Saving
        [Serializable]
        public class AttackWalkerData
        {
            public WalkerData WalkerData;
            public int State;
            public BuildingComponentReferenceData Target;
            public float AttackTime;
            public int Health;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new AttackWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
                Target = _target?.GetData(),
                AttackTime = _attackTime,
                Health = _health
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<AttackWalkerData>(json);

            loadWalkerData(data.WalkerData);

            _state = (AttackWalkerState)data.State;
            _target = data.Target?.GetReference<IAttackable>();
            _attackTime = data.AttackTime;
            _health = data.Health;

            switch (_state)
            {
                case AttackWalkerState.Approaching:
                    ContinueWalk(attack);
                    break;
                case AttackWalkerState.Attacking:
                    break;
            }
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualAttackWalkerSpawner : ManualWalkerSpawner<AttackWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicAttackWalkerSpawner : CyclicWalkerSpawner<AttackWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledAttackWalkerSpawner : PooledWalkerSpawner<AttackWalker> { }
}