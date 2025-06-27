using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// interface for any entity that attacks <see cref="IAttackable"/>s and is fended of by <see cref="DefenderComponent"/>s
    /// </summary>
    public interface IAttacker
    {
        /// <summary>
        /// position for defenders to attack
        /// </summary>
        Vector3 Position { get; }
        /// <summary>
        /// damages and potentially kills the attacker
        /// </summary>
        /// <param name="damage">the amount of damage to do</param>
        void Hurt(int damage);
    }
}