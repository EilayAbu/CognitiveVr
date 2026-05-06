using UnityEngine;
using Oculus.Interaction;

public class DebugInteractions : MonoBehaviour
{
    private Transform _cardTransform;
    private Transform _walletTransform;
    private float lastLogTime = 0f;
    private const float LogInterval = 1f;    // ��������� ������� �������� � ������� �����

    public Transform CardTransform => _cardTransform;    // �������� ������ ��� ������
    public Transform WalletTransform => _walletTransform;  // �������� ������ ��� ������

    void Start()
    {
        var walletObject = GameObject.Find("[BuildingBlock] WalletObject");
        var cardObject = GameObject.Find("[BuildingBlock] CardObject");

        if (walletObject != null)
        {
            _walletTransform = walletObject.transform;
        }

        if (cardObject != null)
        {
            _cardTransform = cardObject.transform;
        }
    }

    void Update()
    {
        if (Time.time - lastLogTime < LogInterval) return;
        lastLogTime = Time.time;

        if (_cardTransform != null && _walletTransform != null)
        {
            float distance = Vector3.Distance(_cardTransform.position, _walletTransform.position);
            Debug.Log($"Distance: {distance:F3} | Card: {_cardTransform.position:F2} | Wallet: {_walletTransform.position:F2}");

            var cardRb = _cardTransform.GetComponent<Rigidbody>();
            var walletRb = _walletTransform.GetComponent<Rigidbody>();

            Debug.Log($"Card Velocity: {cardRb.linearVelocity:F2} | Wallet Velocity: {walletRb.linearVelocity:F2}");
        }
    }
}