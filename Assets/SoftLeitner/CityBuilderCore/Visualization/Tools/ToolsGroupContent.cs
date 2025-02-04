using UnityEngine;
using UnityEngine.EventSystems;

namespace CityBuilderCore
{
    /// <summary>
    /// only exists because OnPointerExit behaviour has changed in Unity 2021<br/>
    /// can be removed in earlier versions
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_tools_group_content.html")]
    public class ToolsGroupContent : MonoBehaviour, IPointerExitHandler
    {
        [Tooltip("OnPointerExit is forwarded to this group")]
        public ToolsGroup Group;

        public void OnPointerExit(PointerEventData eventData)
        {
            Group.OnPointerExit(eventData);
        }
    }
}
