using UnityEngine;
using UnityEngine.UI;

public class UIToggleButton : MonoBehaviour
{
    public static UIToggleButton Instance { get; private set; }

    public Button toggleButton;
    public GameObject[] targets;

    private bool hidden = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);
    }

    public void Toggle()
    {
        SetHidden(!hidden);
    }

    public void SetHidden(bool hide)
    {
        hidden = hide;
        foreach (GameObject target in targets)
            if (target != null)
                target.SetActive(!hidden);
        toggleButton?.gameObject.SetActive(!hidden);
    }
}
