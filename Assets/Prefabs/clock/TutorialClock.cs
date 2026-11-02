using UnityEngine;
using TMPro;

/// <summary>
/// Tutorial-scene clock. Starts at a fixed time (default 08:50:00) when the
/// scene loads and rolls forward in real time. Independent of SessionTimer.
/// Same look as DigitalClock (blinking colon, digits don't shift).
/// </summary>
public class TutorialClock : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI clockText;

    [Header("Start Time")]
    [Range(0, 23)] public int startHour = 8;
    [Range(0, 59)] public int startMinute = 50;
    [Range(0, 59)] public int startSecond = 0;

    private float _elapsed;
    private int _lastSecond = -1;

    void Update()
    {
        _elapsed += Time.deltaTime;

        int total = startHour * 3600 + startMinute * 60 + startSecond + Mathf.FloorToInt(_elapsed);
        int hours = (total / 3600) % 24;
        int minutes = (total % 3600) / 60;
        int seconds = total % 60;

        // update only when the second changes
        if (seconds == _lastSecond) return;
        _lastSecond = seconds;

        if (clockText != null)
        {
            // hide the colon with alpha so the digits don't move
            string colon = seconds % 2 == 0 ? ":" : "<alpha=#00>:<alpha=#FF>";
            clockText.text = $"{hours:D2}{colon}{minutes:D2}{colon}{seconds:D2}";
        }
    }
}
