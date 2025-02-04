using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(TimingHappening), true)]
    public class TimingHappeningEditor : DebugEditor
    {
        private bool _showDialog;

        protected override void drawDebugGUI()
        {
            EditorGUILayout.BeginHorizontal();
            _showDialog = GUILayout.Toggle(_showDialog, "ShowDialog");
            if (GUILayout.Button("Start"))
            {
                var happening = (TimingHappening)target;

                happening.Start();
                happening.Activate();

                if (_showDialog)
                {
                    var dialog = this.FindObject<HappeningDialog>(true);
                    if (dialog)
                        dialog.Activate(new TimingHappeningState(happening, true));
                }
            }
            if (GUILayout.Button("End"))
            {
                var happening = (TimingHappening)target;

                happening.Deactivate();
                happening.End();

                if (_showDialog)
                {
                    var dialog = this.FindObject<HappeningDialog>(true);
                    if (dialog)
                        dialog.Activate(new TimingHappeningState(happening, false));
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
