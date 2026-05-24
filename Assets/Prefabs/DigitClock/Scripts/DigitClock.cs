//Developed by Alexey Master

using System;
using UnityEngine;

namespace ClockControl.AM
{
    public class DigitClock : MonoBehaviour
    {
        [SerializeField] int addDays = 0;
        [SerializeField] int addMinutes = 0;
        Material clockMaterial;
        int firstDigit = 0;
        int secondDigit = 0;
        int thirdDigit = 0;
        int fourthDigit = 0;
        int weekDay = 0;
        int month = 0;
        int day = 0;
        int hour = 0;
        int minute = 0;
        float weekDayControl = 0.0f;
        int firstDigitMonth = 0;
        int secondDigitMonth = 0;
        int firstDigitDay = 0;
        int secondDigitDay = 0;

        private void Start()
        {
            clockMaterial = GetComponent<Renderer>().material;
        }

        void Update()
        {
            DateTime now = DateTime.Now.AddDays(addDays).AddMinutes(addMinutes);
            hour = now.Hour;
            minute = now.Minute;
            weekDay = Convert.ToInt32(now.DayOfWeek);
            month = Convert.ToInt32(now.Month);
            day = Convert.ToInt32(now.Day);

            if (month < 10) { firstDigitMonth = 0; secondDigitMonth = month; } else { firstDigitMonth = 1; secondDigitMonth = month - 10; }
            if (day < 10) { firstDigitDay = 0; secondDigitDay = day; } else { firstDigitDay = day / 10; secondDigitDay = day % 10; }
            if (hour < 10) { firstDigit = 0; secondDigit = hour; } else { firstDigit = 1; secondDigit = hour - 10; }
            if (minute < 10) { thirdDigit = 0; fourthDigit = minute; } else { thirdDigit = minute / 10; fourthDigit = minute % 10; }
            if (weekDay != 0) { weekDayControl = 2.1f - weekDay * 0.15f; } else { weekDayControl = 1.05f; }

            clockMaterial.SetInt("_DigitControl01", firstDigit);
            clockMaterial.SetInt("_DigitControl02", secondDigit);
            clockMaterial.SetInt("_DigitControl03", thirdDigit);
            clockMaterial.SetInt("_DigitControl04", fourthDigit);
            clockMaterial.SetInt("_MonthControl01", firstDigitMonth);
            clockMaterial.SetInt("_MonthControl02", secondDigitMonth);
            clockMaterial.SetInt("_DayControl01", firstDigitDay);
            clockMaterial.SetInt("_DayControl02", secondDigitDay);
            clockMaterial.SetFloat("_MaskControlY", -1 * weekDayControl);
        }       
    }
}
