namespace CityBuilderCore
{
    /// <summary>
    /// a building component that can be attacked
    /// </summary>
    public interface IAttackable : IBuildingTrait<IAttackable>
    {
        /// <summary>
        /// deals damage to the attackable, reducing its health and potentially destroying it
        /// </summary>
        /// <param name="damage">the amount of damage to do</param>
        void Attack(int damage);
    }
}