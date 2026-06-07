using UnityEngine;
using TMPro;
using System;

public class DigitalClock : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI clockText;

    [Header("Scene Start Time")]
    public int startHour = 8;
    public int startMinute = 53;

    // משתנה שישמור את הדקה האחרונה שעודכנה
    private int lastMinute = -1;

    // ההפרש בין שעת הסצינה הרצויה לשעת המערכת בעת טעינת הסצינה
    private TimeSpan timeOffset;

    void Start()
    {
        DateTime now = DateTime.Now;
        DateTime sceneStart = now.Date.AddHours(startHour).AddMinutes(startMinute);
        timeOffset = sceneStart - now;
    }

    void Update()
    {
        DateTime currentTime = DateTime.Now + timeOffset;

        // בדיקה האם הדקה הנוכחית שונה מהדקה האחרונה שהצגנו
        if (currentTime.Minute != lastMinute)
        {
            // עדכון הטקסט רק כשצריך
            if (clockText != null)
            {
                clockText.text = currentTime.ToString("HH:mm");
            }

            // שמירת הדקה החדשה כדי שלא נעדכן שוב באותו פריים
            lastMinute = currentTime.Minute;
        }
    }
}