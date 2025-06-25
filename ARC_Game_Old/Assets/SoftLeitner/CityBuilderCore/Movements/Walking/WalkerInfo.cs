using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// meta info for data that does not change between instances of a walker<br/>
    /// can be used to compare walkers<br/>
    /// the animation section can be used to directly set animation parameters on the main <see cref="Walker.Animator"/><br/>
    /// this is done in the town demo, the other demos instead use unity events liks <see cref="Walker.IsWalkingChanged"/> configured in the inspector and <see cref="UnityAnimatorEvents"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_info.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(WalkerInfo))]
    public class WalkerInfo : KeyedObject
    {
        [Tooltip("display name")]
        public string Name;
        [TextArea]
        [Tooltip("display descriptions")]
        public string[] Descriptions;
        [Tooltip("prefab of the walker")]
        public GameObject Prefab;
        [Header("Pathing")]
        [Tooltip("the kind of pathing the walker will use")]
        public PathType PathType;
        [Tooltip("optional parameter for pathfinding, depends on PathType\nfor example a road for road pathing to only walk on that specific road")]
        public Object PathTag;
        [Tooltip("whether the info sends itself as the tag")]
        public bool PathTagSelf;
        [Header("Timing")]
        [Tooltip("distance the walker moves per second")]
        public float Speed = 5;
        [Tooltip("the maximum time a walker looks for a path when using trywalk")]
        public float MaxWait = 10f;
        [Tooltip("how long the walker stands still before starting to walk")]
        public float Delay;
        [Header("Animation")]
        [Tooltip("animation parameter set when the walker is moving(optional)")]
        public string WalkParameter;
        [Tooltip("animation parameter set to X component of walking direction(optional)")]
        public string DirectionXParameter;
        [Tooltip("animation parameter set to X component of walking direction(optional)")]
        public string DirectionYParameter;
        [Tooltip("animation parameter set when the walker has items(optional)")]
        public string CarryParameter;

        private int _walkAnimationId;
        private int _directionXAnimationId;
        private int _directionYAnimationId;
        private int _carryAnimationId;

        private void OnEnable()
        {
            _walkAnimationId = Animator.StringToHash(WalkParameter);
            _directionXAnimationId = Animator.StringToHash(DirectionXParameter);
            _directionYAnimationId = Animator.StringToHash(DirectionYParameter);
            _carryAnimationId = Animator.StringToHash(CarryParameter);
        }

        public void SetAnimationWalk(Walker walker, bool value) => setAnimation(walker, _walkAnimationId, value);
        public void SetAnimationCarry(Walker walker, bool value) => setAnimation(walker, _carryAnimationId, value);
        public void SetAnimationDirection(Walker walker, Vector3 direction)
        {
            if (!walker || !walker.Animator)
                return;

            if (_directionXAnimationId > 0)
                setAnimation(walker, _directionXAnimationId, direction.x);

            if (_directionYAnimationId > 0)
                setAnimation(walker, _directionYAnimationId, Dependencies.Get<IMap>().IsXY ? direction.y : direction.z);
        }

        private void setAnimation(Walker walker, int id, bool value)
        {
            if (!walker || !walker.Animator)
                return;
            if (id == 0)
                return;
            walker.Animator.SetBool(id, value);
        }
        private void setAnimation(Walker walker, int id, float value)
        {
            if (!walker || !walker.Animator)
                return;
            if (id == 0)
                return;
            walker.Animator.SetFloat(id, value);
        }
    }
}