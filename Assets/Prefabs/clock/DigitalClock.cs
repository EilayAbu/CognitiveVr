using System;
using UnityEngine;
using TMPro;
using CognitiveVR.Core;

public class DigitalClock : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI clockText;

    [Header("Bindings")]
    [SerializeField] private SessionTimer _sessionTimer;

    // משתנה שישמור את השנייה האחרונה שעודכנה
    private int lastSecond = -1;

    void Start()
    {
        ResolveSessionTimer();
    }

    void Update()
    {
        var (hours, minutes, seconds) = CurrentTime();

        // בדיקה האם השנייה הנוכחית שונה מהשנייה האחרונה שהצגנו
        if (seconds != lastSecond)
        {
            // עדכון הטקסט רק כשצריך
            if (clockText != null)
            {
                // הסתרה באמצעות שקיפות כדי שהספרות לא יזוזו
                string colon = seconds % 2 == 0 ? ":" : "<alpha=#00>:<alpha=#FF>";
                clockText.text = $"{hours:D2}{colon}{minutes:D2}{colon}{seconds:D2}";
            }

            // שמירת השנייה החדשה כדי שלא נעדכן שוב באותה שנייה
            lastSecond = seconds;
        }
    }

    // Main scene: the session wall clock. Any scene without one (the tutorial)
    // shows the real local time instead of staying blank.
    private (int hours, int minutes, int seconds) CurrentTime()
    {
        if (_sessionTimer == null)
            ResolveSessionTimer();

        if (_sessionTimer != null)
            return _sessionTimer.WallClockTime;

        var now = DateTime.Now;
        return (now.Hour, now.Minute, now.Second);
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
