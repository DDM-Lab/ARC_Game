using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CityBuilderCore
{
    public static class InputHelper
    {
        public static bool IsPointerOutsideScreen()
        {
            float mouseX = Input.mousePosition.x;
            float mouseY = Input.mousePosition.y;
            float screenX = Screen.width;
            float screenY = Screen.height;
            return mouseX <= 0 || mouseX >= screenX || mouseY <= 0 || mouseY >= screenY;
        }
        public static bool IsPointerOverUIObject()
        {
            if (EventSystem.current == null)
                return false;

            PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
            eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
            return results.Count > 0;
        }
        public static bool IsPointerIn() => !IsPointerOutsideScreen() && !IsPointerOverUIObject();
        public static bool IsPointerOut() => IsPointerOutsideScreen() || IsPointerOverUIObject();

        public static bool GetShift() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static bool GetAlt() => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }
}
