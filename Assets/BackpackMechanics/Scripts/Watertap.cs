using UnityEngine;

public class WaterTapSwitch : MonoBehaviour
{
    private ParticleSystem _waterStream;
    private AudioSource _audioSource;
    private float _lastToggleTime;
    private const float ToggleDelay = 0.5f;
    private bool _isWaterOn;

    // Свойства только для чтения
    public ParticleSystem WaterStream => _waterStream;
    public AudioSource AudioSource => _audioSource;
    public bool IsWaterOn => _isWaterOn;

    void Start()
    {
        _waterStream = GetComponentInChildren<ParticleSystem>();
        _audioSource = GetComponent<AudioSource>();
        _waterStream.Stop();

        if (_audioSource != null)
        {
            _audioSource.Stop();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Проверяем, прошло ли достаточно времени с последнего переключения
        if (Time.time - _lastToggleTime < ToggleDelay)
            return;

        _lastToggleTime = Time.time;
        ToggleWater();
        Debug.Log($"Trigger Enter from: {other.gameObject.name}");
    }

    private void ToggleWater()
    {
        _isWaterOn = !_isWaterOn;
        Debug.Log($"Water state changed to: {_isWaterOn}");

        if (_isWaterOn)
        {
            _waterStream.Play();
            if (_audioSource != null)
            {
                _audioSource.Play();
            }
        }
        else
        {
            _waterStream.Stop();
            if (_audioSource != null)
                _audioSource.Stop();
        }
    }
}