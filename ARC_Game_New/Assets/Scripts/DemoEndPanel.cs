using UnityEngine;

public class DemoEndPanel : MonoBehaviour
{
    public GameObject panel;
    public int        demoFinalDay = 3;

    void Start()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    void OnEnable()
    {
        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
    }

    void OnDisable()
    {
        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
    }

    void OnDayChanged(int newDay)
    {
        if (newDay == demoFinalDay + 1)
            panel?.SetActive(true);
    }
}
