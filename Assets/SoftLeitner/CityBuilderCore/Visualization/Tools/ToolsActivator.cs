using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper for activating a tool from a <see cref="UnityEngine.UI.Toggle"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_tools_activator.html")]
    [RequireComponent(typeof(BaseTool))]
    public class ToolsActivator : MonoBehaviour
    {
        private BaseTool _tool;

        private void Awake()
        {
            _tool = GetComponent<BaseTool>();
        }

        public void SetToolActive(bool active)
        {
            if (active)
                Dependencies.Get<IToolsManager>().ActivateTool(_tool);
            else
                Dependencies.Get<IToolsManager>().DeactivateTool(_tool);
        }
    }
}