using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CityBuilderCore
{
    /// <summary>
    /// has one current tool and a selection of other tools that are shown on mouse over<br/>
    /// when one of the other tools gets activated it becomes the current tool<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_tools_group.html")]
    public class ToolsGroup : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("gameobject under which the current/last tool will be displayed, when another tool is activated it is moved here and the active one get moved back into other tools")]
        public GameObject Current;
        [Tooltip("gameobject under which the other tools will be displayed")]
        public GameObject Other;

        private BaseTool _currentTool;
        private List<BaseTool> _otherTools;

        private void Start()
        {
            _currentTool = Current.GetComponentInChildren<BaseTool>();
            _otherTools = Other.GetComponentsInChildren<BaseTool>().ToList();

            _currentTool.Activating.AddListener(toolActivating);
            foreach (var tool in _otherTools)
            {
                tool.Activating.AddListener(toolActivating);
            }

            Other.SetActive(false);
        }

        private void toolActivating(BaseTool tool)
        {
            if (tool == _currentTool)
                return;

            _currentTool.transform.SetParent(Other.transform);
            _otherTools.Add(_currentTool);
            _otherTools.Remove(tool);
            _currentTool = tool;
            _currentTool.transform.SetParent(Current.transform);

            Other.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Other.transform.HasActiveChildren())
                return;

            Other.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData != null)//not necessary before Unity 2021, in combination with ToolsGroupContent restores old behaviour so exit is not recognized until children are exited too
            {
                if (eventData.pointerCurrentRaycast.gameObject != null)
                {
                    if (eventData.pointerCurrentRaycast.gameObject.transform.IsChildOf(transform))
                    {
                        return;
                    }
                }
            }

            Other.SetActive(false);
        }
    }
}