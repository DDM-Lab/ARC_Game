using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIToggleButton : MonoBehaviour
{
    public static UIToggleButton Instance { get; private set; }

    public Button toggleButton;
    public GameObject[] targets;

    private bool hidden = false;
    private readonly HashSet<GameObject> activeBeforeHide = new HashSet<GameObject>();

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
        hidden = !hidden;
        foreach (GameObject target in targets)
            if (target != null)
                target.SetActive(!hidden);
    }

    public void SetHidden(bool hide)
    {
        if (hide)
        {
            hidden = true;
            activeBeforeHide.Clear();
            foreach (GameObject target in targets)
            {
                if (target != null && target.activeSelf)
                {
                    activeBeforeHide.Add(target);
                    target.SetActive(false);
                }
            }
            toggleButton?.gameObject.SetActive(false);
        }
        else
        {
            hidden = false;
            foreach (GameObject target in targets)
                if (target != null && activeBeforeHide.Contains(target))
                    target.SetActive(true);
            activeBeforeHide.Clear();
            toggleButton?.gameObject.SetActive(true);
        }
    }
}
