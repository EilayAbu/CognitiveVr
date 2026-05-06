using UnityEngine;

public class DoorSound : MonoBehaviour
{
    private AudioSource _audioSource;
    private bool _isAlarmOn;

    public AudioSource AudioSource => _audioSource;
    public bool IsAlarmOn => _isAlarmOn;

    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource != null)
        {
            _audioSource.Stop();
            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_isAlarmOn)
        {
            _isAlarmOn = true;
            Debug.Log("START alarm");
            _audioSource.Play();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (_isAlarmOn)
        {
            _isAlarmOn = false;
            Debug.Log("STOP alarm");
            _audioSource.Stop();
        }
    }
}