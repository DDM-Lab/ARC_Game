using UnityEngine;
using UnityEngine.UI;

public class EndTurnButton : MonoBehaviour
{
    private Button endTurnButton;

    private void Start()
    {
        endTurnButton = GetComponent<Button>();
        endTurnButton.onClick.AddListener(OnEndTurnClicked);
    }

    private void OnEndTurnClicked()
    {
        GameManager.Instance.EndTurn(); // Call the new public method
    }
}
