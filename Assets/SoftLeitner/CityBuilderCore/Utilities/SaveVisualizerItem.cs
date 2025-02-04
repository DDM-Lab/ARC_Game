using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityBuilderCore
{
    public class SaveVisualizerItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("Texts")]
        [Tooltip("text used to display the save name")]
        public TMPro.TMP_Text MissionText;
        [Tooltip("text used to display the save name")]
        public TMPro.TMP_Text SaveText;
        [Header("Meta Save Data")]
        [Tooltip("image that displays the small screenshot that gets taken when saving due to DefaultGameManager.SaveMetaData")]
        public Image Image;
        [Tooltip("text that displays the duration of the save game")]
        public TMPro.TMP_Text Duration;
        [Tooltip("text that displays when the save game was created")]
        public TMPro.TMP_Text SavedAt;
        [Header("Selection and Hover")]
        public Image Background;
        public Color DefaultColor;
        public Color HoverColor;
        public Color SelectedColor;
        [Header("Other")]
        [Tooltip("optional dialog shown before Loading")]
        public MessageBoxDialog ConfirmationDialog;

        public string SaveName => _name;

        private SaveVisualizer _parent;
        private Mission _mission;
        private Difficulty _difficulty;
        private string _name;

        private bool _isHovered;
        private bool _isSelected;

        public void Initialize(SaveVisualizer parent, Mission mission, Difficulty difficulty, string name)
        {
            _parent = parent;
            _mission = mission;
            _difficulty = difficulty;
            _name = name;

            if (MissionText)
                MissionText.text = mission.Name;
            if (SaveText)
                SaveText.text = string.IsNullOrWhiteSpace(name) ? "QUICK" : name;

            if (Image || Duration || SavedAt)
            {
                var metaData = SaveHelper.GetExtra(SaveHelper.GetKey(mission, difficulty), name, "META");
                if (!string.IsNullOrWhiteSpace(metaData))
                {
                    try
                    {
                        var meta = JsonUtility.FromJson<DefaultGameManager.SaveDataMeta>(metaData);

                        Duration.text = TimeSpan.FromSeconds(meta.Playtime).ToString("h'h 'm'm 's's'");
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
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount == 1)
            {
                _parent.Select(this);
            }
            else if (eventData.clickCount == 2)
            {
                if (_isSelected)
                    _parent.Use();
                else
                    _parent.Select(this);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            setBackgroundColor();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            setBackgroundColor();
        }

        public void SetSelected(bool value)
        {
            _isSelected = value;
            setBackgroundColor();
        }

        public void Delete()
        {
            _mission.Delete(_name, _difficulty);
        }

        public void Load()
        {
            if (ConfirmationDialog)
                ConfirmationDialog.Check("Loading", "You are about to load a savegame.", load);
            else
                load();
        }
        private void load()
        {
            SceneManager.LoadSceneAsync(_mission.SceneName).completed += o =>
            {
                Dependencies.Get<IMissionManager>().SetMissionParameters(new MissionParameters()
                {
                    Mission = _mission,
                    Difficulty = _difficulty,
                    ContinueName = _name,
                    IsContinue = true
                });
            };
        }

        private void setBackgroundColor()
        {
            if (!Background)
                return;

            if (_isSelected)
                Background.color = SelectedColor;
            else if (_isHovered)
                Background.color = HoverColor;
            else
                Background.color = DefaultColor;
        }
    }
}
