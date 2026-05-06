using UnityEngine;

public class DrawerItemController : MonoBehaviour
{
    [SerializeField] private Transform drawerParent;
    [SerializeField] private Rigidbody cardRigidbody;
    [SerializeField] private Rigidbody walletRigidbody;

    [SerializeField] private float moveMultiplier = 1.0f;
    [SerializeField] private float velocityDamping = 0.8f;

    private Vector3 lastDrawerPosition;
    private Vector3 drawerVelocity;
    private const float minimumMovement = 0.001f;
    private Transform originalCardParent;
    private Transform originalWalletParent;

    private void Start()
    {
        if (drawerParent == null)
            drawerParent = transform.parent;

        if (cardRigidbody == null)
        {
            GameObject cardObj = GameObject.Find("Card.002");
            if (cardObj != null)
            {
                cardRigidbody = cardObj.GetComponent<Rigidbody>();
                originalCardParent = cardObj.transform.parent;
            }
        }
        else
        {
            originalCardParent = cardRigidbody.transform.parent;
        }

        if (walletRigidbody == null)
        {
            GameObject walletObj = GameObject.Find("wallet");
            if (walletObj != null)
            {
                walletRigidbody = walletObj.GetComponent<Rigidbody>();
                originalWalletParent = walletObj.transform.parent;
            }
        }
        else
        {
            originalWalletParent = walletRigidbody.transform.parent;
        }

        lastDrawerPosition = drawerParent.position;

        Debug.Log("Found Card.002: " + (cardRigidbody != null));
        Debug.Log("Found wallet: " + (walletRigidbody != null));
    }

    private void FixedUpdate()
    {
        Vector3 currentPosition = drawerParent.position;
        Vector3 movement = currentPosition - lastDrawerPosition;

        if (movement.magnitude > minimumMovement)
        {
            drawerVelocity = movement / Time.fixedDeltaTime;

            if (cardRigidbody != null && IsItemStillInDrawer(cardRigidbody.transform) && IsItemInDrawerBounds(cardRigidbody.transform.position))
            {
                Vector3 targetVelocity = drawerVelocity * moveMultiplier;
                cardRigidbody.linearVelocity = Vector3.Lerp(cardRigidbody.linearVelocity, targetVelocity, velocityDamping);
            }

            if (walletRigidbody != null && IsItemStillInDrawer(walletRigidbody.transform) && IsItemInDrawerBounds(walletRigidbody.transform.position))
            {
                Vector3 targetVelocity = drawerVelocity * moveMultiplier;
                walletRigidbody.linearVelocity = Vector3.Lerp(walletRigidbody.linearVelocity, targetVelocity, velocityDamping);
            }
        }
        else
        {
            if (cardRigidbody != null && IsItemStillInDrawer(cardRigidbody.transform))
                cardRigidbody.linearVelocity *= velocityDamping;

            if (walletRigidbody != null && IsItemStillInDrawer(walletRigidbody.transform))
                walletRigidbody.linearVelocity *= velocityDamping;
        }

        lastDrawerPosition = currentPosition;
    }

    private bool IsItemStillInDrawer(Transform itemTransform)
    {
        if (itemTransform == null) return false;

        Transform currentParent = itemTransform.parent;
        Transform originalParent = (itemTransform == cardRigidbody?.transform) ? originalCardParent : originalWalletParent;

        return currentParent == originalParent;
    }

    private bool IsItemInDrawerBounds(Vector3 itemPosition)
    {
        Bounds bounds = GetComponent<BoxCollider>().bounds;
        bounds.Expand(new Vector3(0.6f, 0, 0.6f));

        return itemPosition.x >= bounds.min.x && itemPosition.x <= bounds.max.x &&
               itemPosition.z >= bounds.min.z && itemPosition.z <= bounds.max.z &&
               itemPosition.y >= bounds.min.y && itemPosition.y <= bounds.min.y + 1.0f;
    }
}