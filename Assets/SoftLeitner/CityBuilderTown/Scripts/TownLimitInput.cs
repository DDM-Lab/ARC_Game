using CityBuilderCore;
using TMPro;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// lets players set limits for certain items<br/>
    /// for example wood has to be limited because there might be no logs left for construction otherwise
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_limit_input.html")]
    public class TownLimitInput : MonoBehaviour
    {
        [Tooltip("lets players set the maximum number of an item that should be produced")]
        public TMP_InputField Input;
        [Tooltip("the item that will have its quantity limited(for example wood so there are logs left for construction)")]
        public Item Item;

        private void Start()
        {
            SetText();
            Input.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(textChanged));
        }

        public void SetText()
        {
            Input.SetTextWithoutNotify(TownManager.Instance.GetItemLimit(Item).ToString());
        }

        private void textChanged(string text)
        {
            if (!int.TryParse(text, out int num))
                return;

            TownManager.Instance.SetItemLimit(Item, num);
        }
    }
}
