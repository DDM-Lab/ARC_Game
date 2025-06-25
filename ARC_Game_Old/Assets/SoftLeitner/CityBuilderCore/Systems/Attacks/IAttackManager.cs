using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// manages attack system
    /// <para>
    /// attackers attack attackableComponents and get fended off by defenderComponents<br/>
    /// all entities with health get healthbars
    /// </para>
    /// </summary>
    public interface IAttackManager
    {
        /// <summary>
        /// adds an attacker to the manager that can now be found by defenders<br/>
        /// typically called by the attacker in Start
        /// </summary>
        /// <param name="attacker">the attacker to add</param>
        void AddAttacker(IAttacker attacker);
        /// <summary>
        /// removes a previously added attacker from the manager, it can no longer be found by defenders<br/>
        /// typically called by the attacker in OnDestroy when it has been killed
        /// </summary>
        /// <param name="attacker"></param>
        void RemoveAttacker(IAttacker attacker);

        /// <summary>
        /// retrieves the closest attacker to a given position
        /// </summary>
        /// <param name="position">position of the defender that is looking for attackers</param>
        /// <param name="maxDistance">maximum distance to look for</param>
        /// <returns>the attacker if one was found inside the distance</returns>
        IAttacker GetAttacker(Vector3 position, float maxDistance);
        /// <summary>
        /// retrieves the all attackers within a radius
        /// </summary>
        /// <param name="position">position of the defender that is looking for attackers</param>
        /// <param name="radius">maximum distance to look for</param>
        /// <returns>all attackers found inside the radius</returns>
        IEnumerable<IAttacker> GetAttackers(Vector3 position, float radius);
        /// <summary>
        /// attempts to find a path to an <see cref="IAttackable"/>
        /// </summary>
        /// <param name="point">current point to start from</param>
        /// <param name="pathType">preferred path type</param>
        /// <param name="tag">additional pathing option</param>
        /// <returns>the attackable and a path to it if on was found</returns>
        BuildingComponentPath<IAttackable> GetAttackerPath(Vector2Int point, PathType pathType, object tag = null);
    }
}