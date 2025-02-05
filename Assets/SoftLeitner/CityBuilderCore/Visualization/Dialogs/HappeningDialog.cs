using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// dialog that shows start/end texts for happenings if they have one
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_happening_dialog.html")]
    public class HappeningDialog : DialogBase
    {
        [Tooltip("will display the happening Start/EndTitle")]
        public TMPro.TMP_Text TitleText;
        [Tooltip("will display the happening Start/EndDescription")]
        public TMPro.TMP_Text DescriptionText;

        public void Activate(TimingHappeningState state)
        {
            if (!state.HasText)
                return;

            base.Activate();

            TitleText.text = state.Title;
            DescriptionText.text = state.Description;
        }
    }
}