using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public TextMeshProUGUI roundText;

    public void UpdateRoundText(int roundNumber, int dayNumber)
    {
        roundText.text = "Round: " + roundNumber + " Day: " + dayNumber + " Weather: " + GlobalManager.Instance.currentWeather;
    }
}
