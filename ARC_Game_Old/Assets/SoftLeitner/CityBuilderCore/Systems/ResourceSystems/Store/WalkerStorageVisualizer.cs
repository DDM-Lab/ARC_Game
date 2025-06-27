using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes the item a walker carries<br/>
    /// when the walker has an item the meshrenderer is activated<br/>
    /// the meshrenderer gets its material from the first item in walker storage
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_storage_visualizer.html")]
    [RequireComponent(typeof(Walker))]
    public class WalkerStorageVisualizer : MonoBehaviour
    {
        [Tooltip("will be activated and have its material set when the walker carries an item")]
        public MeshRenderer MeshRenderer;

        [Tooltip("fires whenever the walker storage changes, true when the walker has any item")]
        public BoolEvent HasItemsChanged;

        private Walker _walker;

        private void Awake()
        {
            _walker = GetComponent<Walker>();
        }

        private void OnEnable()
        {
            visualize();
        }

        private void Start()
        {
            _walker.ItemStorage.Changed += _ => visualize();

            visualize();
        }

        private void visualize()
        {
            var itemQuantity = _walker.ItemStorage.GetItemQuantities().FirstOrDefault();

            HasItemsChanged?.Invoke(itemQuantity != null);
            MeshRenderer.gameObject.SetActive(itemQuantity != null);

            if (itemQuantity != null)
            {
                MeshRenderer.sharedMaterial = itemQuantity.Item.Material;
            }
        }
    }
}