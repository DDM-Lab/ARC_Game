using CityBuilderCore;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderTown
{
    /// <summary>
    /// behaviour used when displaying save games<br/>
    /// shows things like name, image and duration and gives access to the button that is used to select saves
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_save_game.html")]
    public class TownSaveGame : MonoBehaviour
    {
        [Tooltip("button used to select the save, event gets hooked up by the dialog that owns the save")]
        public Button Button;
        [Tooltip("image that gets colored white or black when the save gets selected/deselected")]
        public Image Background;
        [Tooltip("text used to display the save name")]
        public TMPro.TMP_Text Text;
        [Tooltip("iamge that displays the small screenshot that gets taken when saving due to DefaultGameManager.SaveMetaData")]
        public Image Image;
        [Tooltip("text that displays the duration of the save game")]
        public TMPro.TMP_Text Duration;
        [Tooltip("text that displays when the save game was created")]
        public TMPro.TMP_Text SavedAt;

        public string SaveName { get; private set; }

        public void Initialize(Mission mission, Difficulty difficulty, string name)
        {
            SaveName = name;

            if (string.IsNullOrWhiteSpace(name))
                Text.text = "QUICK";
            else
                Text.text = name;

            var metaData = SaveHelper.GetExtra(SaveHelper.GetKey(mission, difficulty), name, "META");
            if (!string.IsNullOrWhiteSpace(metaData))
            {
                try
                {
                    var meta = JsonUtility.FromJson<DefaultGameManager.SaveDataMeta>(metaData);

                    Duration.text = TimeSpan.FromSeconds(meta.Playtime).ToString("g");
                    SavedAt.text = DateTime.FromFileTime(meta.SavedAt).ToString("G");

                    var texture = new Texture2D(0, 0);
                    texture.LoadImage(meta.Image);

                    Image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0, 0));
                }
                catch
                {
                    //dont care
                }
            }
        }

        public void Select()
        {
            Background.color = Color.white;
        }
        public void Deselect()
        {
            Background.color = Color.black;
        }
    }
}
