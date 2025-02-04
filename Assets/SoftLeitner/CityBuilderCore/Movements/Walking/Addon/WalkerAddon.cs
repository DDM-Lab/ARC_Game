using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// temporary objects that are added(<see cref="Walker.AddAddon{T}(T)"/>) and removed(<see cref="Walker.RemoveAddon(WalkerAddon)"/>) at runtime<br/>
    /// can be used for effects, statuses, animations, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_addon.html")]
    public abstract class WalkerAddon : KeyedBehaviour, ISaveData
    {
        [Tooltip(@"how the addon behaves when there is more than one at the same time
Stack	add multiple
Queue	keep additional in queue
Replace	removes previous instances
Single	ignore additional")]
        public BuildingAddon.AddonAccumulationMode Accumulation;
        [Tooltip("whether the addon should be saved and re added on load(usually true for fire or disease, false for effects or selections)")]
        public bool Save;

        protected bool _isTerminated;

        public Walker Walker { get; set; }

        public virtual void Awake() { }
        public virtual void Start() { }
        public virtual void Update() { }

        /// <summary>
        /// Removes the addon from the walker it is located on<br/>
        /// this will usually result in the termination of the addon
        /// </summary>
        public void Remove()
        {
            Walker.RemoveAddon(this);
        }

        /// <summary>
        /// called by the walker after the addon has been instantiated and <see cref="Walker"/> has been set
        /// </summary>
        public virtual void InitializeAddon()
        {
        }
        /// <summary>
        /// called by the walker when the addon gets removed<br/>
        /// this should usually terminate the addon(mark as terminated and Destroy)
        /// </summary>
        public virtual void TerminateAddon()
        {
            _isTerminated = true;

            Destroy(gameObject);
        }

        #region Saving
        public virtual string SaveData() => string.Empty;
        public virtual void LoadData(string json) { }
        #endregion
    }
}