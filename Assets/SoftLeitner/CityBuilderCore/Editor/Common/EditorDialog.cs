using System;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    public class EditorDialog : EditorWindow
    {
        private string _labelA;
        private string _labelB;
        private string _labelC;

        private string _textA;
        private string _textB;
        private string _textC;

        private bool _initializedPosition = false;
        private Action _confirmed;

        private bool _shouldClose = false;
        private Vector2 _maxScreenPos;

        void OnGUI()
        {
            // Check if Esc/Return have been pressed
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    // Escape pressed
                    case KeyCode.Escape:
                        _shouldClose = true;
                        e.Use();
                        break;

                    // Enter pressed
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        _confirmed?.Invoke();
                        _shouldClose = true;
                        e.Use();
                        break;
                }
            }

            if (_shouldClose)
            {
                Close();
            }

            var rect = EditorGUILayout.BeginVertical();

            EditorGUILayout.Space(8);
            if (!string.IsNullOrWhiteSpace(_labelA))
                _textA = EditorGUILayout.TextField(_labelA, _textA);
            if (!string.IsNullOrWhiteSpace(_labelB))
                _textB = EditorGUILayout.TextField(_labelB, _textB);
            if (!string.IsNullOrWhiteSpace(_labelC))
                _textC = EditorGUILayout.TextField(_labelC, _textC);
            EditorGUILayout.Space(12);

            var r = EditorGUILayout.GetControlRect();
            r.width /= 2;
            if (GUI.Button(r, "Ok"))
            {
                _confirmed?.Invoke();
                _shouldClose = true;
            }

            r.x += r.width;
            if (GUI.Button(r, "Cancel"))
            {
                _shouldClose = true;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.EndVertical();

            // Force change size of the window
            if (rect.width != 0 && minSize != rect.size)
            {
                minSize = maxSize = rect.size;
            }

            // Set dialog position next to mouse position
            if (!_initializedPosition && e.type == EventType.Layout)
            {
                _initializedPosition = true;

                // Move window to a new position. Make sure we're inside visible window
                var mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                mousePos.x += 32;
                if (mousePos.x + position.width > _maxScreenPos.x) mousePos.x -= position.width + 64; // Display on left side of mouse
                if (mousePos.y + position.height > _maxScreenPos.y) mousePos.y = _maxScreenPos.y - position.height;

                position = new Rect(mousePos.x, mousePos.y, position.width, position.height);
                
                // Focus current window
                Focus();
            }
        }

        public static Tuple<string, string, string> Show(string title, string labelA, string labelB, string labelC, string textA, string textB, string textC)
        {
            // Make sure our popup is always inside parent window, and never offscreen
            // So get caller's window size
            var maxPos = GUIUtility.GUIToScreenPoint(new Vector2(Screen.width, Screen.height));

            Tuple<string, string, string> result = null;
            var dialog = CreateInstance<EditorDialog>();
            dialog._maxScreenPos = maxPos;
            dialog.titleContent = new GUIContent(title);
            dialog._labelA = labelA;
            dialog._labelB = labelB;
            dialog._labelC = labelC;
            dialog._textA = textA;
            dialog._textB = textB;
            dialog._textC = textC;
            dialog._confirmed += () => result = Tuple.Create(dialog._textA, dialog._textB, dialog._textC);

            dialog.ShowModal();

            return result;
        }
    }
}