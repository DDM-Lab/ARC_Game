using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// default implementation for <see cref="IAttackManager"/><br/>
    /// attacker targets are chosen purely on distance
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/attacks">https://citybuilder.softleitner.com/manual/attacks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_attack_manager.html")]
    public class DefaultAttackManager : MonoBehaviour, IAttackManager
    {
        [Tooltip("true > no pathing to blocked attackables, false > straight path regardless of obstacles")]
        public bool IgnoreBlocked;

        private List<IAttacker> _attackers = new List<IAttacker>();

        protected virtual void Awake()
        {
            Dependencies.Register<IAttackManager>(this);
        }

        public void AddAttacker(IAttacker attacker)
        {
            _attackers.Add(attacker);
        }
        public void RemoveAttacker(IAttacker attacker)
        {
            _attackers.Remove(attacker);
        }

        public IAttacker GetAttacker(Vector3 position, float maxDistance)
        {
            float minDistance = float.MaxValue;
            IAttacker minAttacker = null;

            foreach (var attacker in _attackers)
            {
                var distance = Vector3.Distance(attacker.Position, position);
                if (distance < minDistance && distance < maxDistance)
                {
                    minDistance = distance;
                    minAttacker = attacker;
                }
            }

            return minAttacker;
        }
        public IEnumerable<IAttacker> GetAttackers(Vector3 position, float maxDistance)
        {
            foreach (var attacker in _attackers)
            {
                var distance = Vector3.Distance(attacker.Position, position);
                if (distance < maxDistance)
                    yield return attacker;
            }
        }
        public BuildingComponentPath<IAttackable> GetAttackerPath(Vector2Int point, PathType pathType, object tag = null)
        {
            var attackables = Dependencies.Get<IBuildingManager>().GetBuildingTraits<IAttackable>().OrderBy(t => Vector3.Distance(t.Building.WorldCenter, transform.position)).ToList();
            if (attackables.Count == 0)
                return null;//game must have finished

            foreach (var attackable in attackables)
            {
                var path = PathHelper.FindPath(point, attackable.Building, pathType, tag);
                if (path != null)
                    return new BuildingComponentPath<IAttackable>(attackable.Reference, path);
            }

            if (IgnoreBlocked)
                return null;
            else
                return new BuildingComponentPath<IAttackable>(attackables[0].Reference, PathHelper.FindPath(point, attackables[0].Building, PathType.None));
        }
    }
}