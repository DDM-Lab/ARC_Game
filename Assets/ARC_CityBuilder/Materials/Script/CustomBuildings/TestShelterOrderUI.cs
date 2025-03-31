using UnityEngine;

public class TestShelterOrderUI : MonoBehaviour
{
    public void OnAdvanceDayClicked()
    {
        GameDatabase.Instance.AdvanceDay();
        Debug.Log("[ShelterOrderUI] Advance Day clicked — food orders generated.");
    }
}
