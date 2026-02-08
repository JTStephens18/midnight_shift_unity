using UnityEngine;

/// <summary>
/// Cash register that triggers NPC checkout when the player interacts with it.
/// Checkout is only allowed when counter is empty (all items have been bagged).
/// </summary>
public class CashRegister : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("Maximum distance from which the player can interact with the register.")]
    [SerializeField] private float interactionRange = 3f;

    [Tooltip("Key to press to trigger checkout.")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Tooltip("Layer mask for the register. Leave as default to detect this object.")]
    [SerializeField] private LayerMask registerLayer = ~0;

    private Camera _playerCamera;

    void Start()
    {
        _playerCamera = Camera.main;
    }

    void Update()
    {
        if (Input.GetKeyDown(interactKey))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        if (_playerCamera == null) return;

        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange, registerLayer))
        {
            // Check if we hit this cash register
            if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
            {
                TriggerCheckout();
            }
        }
    }

    private void TriggerCheckout()
    {
        // Check if queue manager exists
        if (CheckoutQueueManager.Instance == null)
        {
            Debug.LogWarning("[CashRegister] No CheckoutQueueManager found! Please add one to the scene.");
            return;
        }

        // Block checkout if counter has items
        if (!CheckoutQueueManager.Instance.IsCounterClear())
        {
            Debug.Log("[CashRegister] Cannot checkout - counter still has items! Remove all items first.");
            // TODO: Could trigger a UI message or sound here
            return;
        }

        // Get the NPC currently at counter
        NPCInteractionController npcAtCounter = CheckoutQueueManager.Instance.CurrentlyAtCounter;

        if (npcAtCounter != null)
        {
            if (!npcAtCounter.HasCheckedOut())
            {
                Debug.Log($"[CashRegister] Processing checkout for NPC: {npcAtCounter.name}");
                npcAtCounter.TriggerCheckout();
            }
            else
            {
                Debug.Log("[CashRegister] NPC already checked out");
            }
        }
        else
        {
            Debug.Log("[CashRegister] No NPC at counter to checkout");
        }
    }

    /// <summary>
    /// Check if checkout is currently possible.
    /// </summary>
    public bool CanCheckout()
    {
        if (CheckoutQueueManager.Instance == null) return false;
        return CheckoutQueueManager.Instance.IsCounterClear() &&
               CheckoutQueueManager.Instance.CurrentlyAtCounter != null;
    }
}
