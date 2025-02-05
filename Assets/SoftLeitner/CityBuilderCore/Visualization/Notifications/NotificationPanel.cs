using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityBuilderCore
{
    public class NotificationPanel : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("displays the notification text")]
        public TMPro.TMP_Text Text;
        [Tooltip("faded in and out when the pointer enters and exits")]
        public Graphic Background;
        [Tooltip("how long the notification is displayed before it gets destroyed")]
        public float DisplayDuration;
        [Tooltip("how long the notification fades out at the end")]
        public float FadeDuration;

        public NotificationRequest Request { get; set; }

        private float _time;
        private bool _isPointerInside;

        private void Start()
        {
            if (Request != null)
                Text.text = Request.Text;

            if (Background)
                Background.CrossFadeAlpha(0, 0, true);
        }

        private void Update()
        {
            _time += Time.unscaledDeltaTime;

            if (_isPointerInside)
                Background.CrossFadeAlpha(1f, 0.1f, true);
            else
                Background.CrossFadeAlpha(0f, 0.2f, true);

            if (_time > DisplayDuration)
            {
                Destroy(gameObject);
                return;
            }

            if (_time > DisplayDuration - FadeDuration)
                Text.alpha = (DisplayDuration - _time) / FadeDuration;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Request?.Leader != null)
                Dependencies.Get<IMainCamera>().Follow(Request.Leader);
            else if (Request?.Position != null)
                Dependencies.Get<IMainCamera>().Jump(Request.Position.Value);
        }

        public void OnPointerEnter(PointerEventData eventData) => _isPointerInside = true;
        public void OnPointerExit(PointerEventData eventData) => _isPointerInside = false;
    }
}
