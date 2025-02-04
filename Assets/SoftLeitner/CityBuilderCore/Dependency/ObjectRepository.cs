using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// repository used to define and find the objects available to a scene
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_object_repository.html")]
    public class ObjectRepository : MonoBehaviour
    {
        [Header("Sets")]
        public BuildingAddonSet Addons;
        public BuildingInfoSet Buildings;
        public WalkerAddonSet WalkerAddons;
        public WalkerInfoSet Walkers;
        public ItemSet Items;
        public PopulationSet Populations;
        public RoadSet Roads;
        public ScoreSet Scores;
        public EmploymentGroupSet EmploymentGroups;
        public MissionSet Missions;
        public DifficultySet Difficulties;

        [Header("Singles (convenience option when theres only one)")]
        public BuildingAddon Addon;
        public BuildingInfo Building;
        public WalkerAddon WalkerAddon;
        public WalkerInfo Walker;
        public Item Item;
        public Population Population;
        public Road Road;
        public Score Score;
        public EmploymentGroup EmploymentGroup;
        public Mission Mission;
        public Difficulty Difficulty;

        private void Awake()
        {
            Dependencies.Register(getKeyedSet(Addons, Addon));
            Dependencies.Register(getKeyedSet(Buildings, Building));
            Dependencies.Register(getKeyedSet(WalkerAddons, WalkerAddon));
            Dependencies.Register(getKeyedSet(Items, Item));
            Dependencies.Register(getKeyedSet(Populations, Population));
            Dependencies.Register(getKeyedSet(EmploymentGroups, EmploymentGroup));
            Dependencies.Register(getKeyedSet(Missions, Mission));
            Dependencies.Register(getKeyedSet(Difficulties, Difficulty));

            Dependencies.Register(getObjectSet(Addons, Addon));
            Dependencies.Register(getObjectSet(Buildings, Building));
            Dependencies.Register(getObjectSet(WalkerAddons, WalkerAddon));
            Dependencies.Register(getObjectSet(Walkers, Walker));
            Dependencies.Register(getObjectSet(Items, Item));
            Dependencies.Register(getObjectSet(Populations, Population));
            Dependencies.Register(getObjectSet(Roads, Road));
            Dependencies.Register(getObjectSet(Scores, Score));
            Dependencies.Register(getObjectSet(EmploymentGroups, EmploymentGroup));
            Dependencies.Register(getObjectSet(Missions, Mission));
            Dependencies.Register(getObjectSet(Difficulties, Difficulty));
        }

        private IKeyedSet<T> getKeyedSet<T>(IKeyedSet<T> set, T single) where T : IKeyed
        {
            if (set != null)
                return set;
            else if (single != null)
                return new RuntimeKeyedSet<T>(single);
            else
                return new RuntimeKeyedSet<T>();
        }

        private IObjectSet<T> getObjectSet<T>(IObjectSet<T> set, T single)
        {
            if (set != null)
                return set;
            else if (single != null)
                return new RuntimeObjectSet<T>(single);
            else
                return new RuntimeObjectSet<T>();
        }
    }
}