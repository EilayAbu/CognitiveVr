using CognitiveVR.Tasks;
using UnityEngine;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Hebrew weather app screen. Reports CheckWeather progress to TaskApi
    /// the first time it is opened and the forecast becomes visible. The Hebrew
    /// forecast text is hard-coded per spec: "גשם צפוי בהמשך היום".
    /// </summary>
    [DisallowMultipleComponent]
    public class WeatherAppScreen : MonoBehaviour
    {
        [Header("Bindings (auto-resolved)")]
        [SerializeField] private PhoneScreenController _phone;

        [Header("State (read only)")]
        [SerializeField] private bool _firstOpenLogged;

        public bool HasBeenOpened => _firstOpenLogged;

        public void Bind(PhoneScreenController phone)
        {
            _phone = phone;
        }

        public void NotifyOpened()
        {
            if (_firstOpenLogged) return;
            _firstOpenLogged = true;

            try
            {
                TaskApi.ReportStepCompleted(TaskType.CheckWeather, "open_phone", "Opened weather app on phone");
                TaskApi.ReportStepCompleted(TaskType.CheckWeather, "read_forecast", "Forecast visible: גשם צפוי בהמשך היום");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[{nameof(WeatherAppScreen)}] Failed to report weather app open: {ex.Message}", this);
            }
        }
    }
}
