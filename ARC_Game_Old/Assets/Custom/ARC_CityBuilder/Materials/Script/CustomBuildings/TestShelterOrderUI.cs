using UnityEngine;

public class TestShelterOrderUI : MonoBehaviour
{
    public void OnAdvanceDayClicked()
    {
        BuildingSystem.Instance.AdvanceDay();
        Debug.Log("[ShelterOrderUI] Advance Day clicked — food orders generated.");
    }
}
