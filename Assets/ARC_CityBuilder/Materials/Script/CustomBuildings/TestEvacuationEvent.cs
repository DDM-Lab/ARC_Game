using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TestEvacuationEvent : MonoBehaviour
{
    public void OnClickEvacuateAllCommunities()
    {
        var communities = GameDatabase.Instance.GetAllCommunities();
        foreach (var building in communities)
        {
            var logic = building.GetComponent<CommunityLogic>();
            if (logic != null)
            {
                logic.EnterFloodedMode(); // manually trigger evacuation logic
                Debug.Log($"[DebugEvacuation] Triggered flooded mode for {building.name}");
            }
        }
    }
}
