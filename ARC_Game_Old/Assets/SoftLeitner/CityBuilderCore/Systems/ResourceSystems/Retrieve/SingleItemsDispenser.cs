using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// items dispenser that dispenses once and then self destructs
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_single_items_dispenser.html")]
    public class SingleItemsDispenser : MonoBehaviour, IItemsDispenser
    {
        [Tooltip("dispenser key, used in retrievers")]
        public string Key;
        [Tooltip("items returned on dispense")]
        public ItemQuantity Items;

        [Tooltip("fired when the dispenser is used")]
        public UnityEvent Dispensed;

        public Vector3 Position => transform.position;
        string IItemsDispenser.Key => Key;

        private void Start()
        {
            Dependencies.Get<IItemsDispenserManager>().Add(this);
        }

        private void OnDestroy()
        {
            Dependencies.Get<IItemsDispenserManager>().Remove(this);
        }

        public ItemQuantity Dispense()
        {
            Dispensed?.Invoke();
            Destroy(gameObject);

            return Items;
        }
    }
}