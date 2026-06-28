using UnityEngine;
using TMPro;
using CognitiveVR.Core;

public class DigitalClock : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI clockText;

    [Header("Bindings")]
    [SerializeField] private SessionTimer _sessionTimer;

    // משתנה שישמור את הדקה האחרונה שעודכנה
    private int lastMinute = -1;

    void Start()
    {
        ResolveSessionTimer();
    }

    void Update()
    {
        if (_sessionTimer == null)
        {
            ResolveSessionTimer();
            if (_sessionTimer == null) return;
        }

        var (hours, minutes, _) = _sessionTimer.WallClockTime;

        // בדיקה האם הדקה הנוכחית שונה מהדקה האחרונה שהצגנו
        if (minutes != lastMinute)
        {
            // עדכון הטקסט רק כשצריך
            if (clockText != null)
            {
                clockText.text = $"{hours:D2}:{minutes:D2}";
            }

            // שמירת הדקה החדשה כדי שלא נעדכן שוב באותו פריים
            lastMinute = minutes;
        }
    }

    private void ResolveSessionTimer()
    {
        if (_sessionTimer != null) return;
#if UNITY_2023_1_OR_NEWER
        _sessionTimer = FindFirstObjectByType<SessionTimer>();
#else
        _sessionTimer = FindObjectOfType<SessionTimer>();
#endif
    }
}
