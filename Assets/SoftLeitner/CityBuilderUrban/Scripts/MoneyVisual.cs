using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// gets spawned in when money is earned or spent<br/>
    /// the prefab also contains an animator which calls <see cref="Done"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_money_visual.html")]
    public class MoneyVisual : MonoBehaviour
    {
        public TMPro.TMP_Text Text;

        public void Set(int quantity)
        {
            if (quantity > 0)
                Text.color = Color.green;
            else
                Text.color = Color.red;

            Text.text = quantity.ToString();
        }
        public void Done() => Destroy(gameObject);
    }
}
