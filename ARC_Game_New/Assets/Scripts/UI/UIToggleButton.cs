using UnityEngine;
using UnityEngine.UI;

public class UIToggleButton : MonoBehaviour
{
    public Button toggleButton;
    public GameObject[] targets;

    private bool hidden = false;

    void Start()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);
    }

    void Toggle()
    {
        hidden = !hidden;
        foreach (GameObject target in targets)
            if (target != null)
                target.SetActive(!hidden);
    }
}
