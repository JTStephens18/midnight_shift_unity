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

    [Header("NPC Detection")]
    [Tooltip("Radius to check for NPCs at the counter.")]
    [SerializeField] private float npcDetectionRadius = 5f;

    [Tooltip("Layer mask to detect NPCs.")]
    [SerializeField] private LayerMask npcLayerMask;

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
        // Find nearby NPCs
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, npcDetectionRadius, npcLayerMask);
        NPCInteractionController bestCandidate = null;
        float closestDist = float.MaxValue;

        foreach (var col in hitColliders)
        {
            NPCInteractionController npc = col.GetComponent<NPCInteractionController>();
            if (npc != null && !npc.HasCheckedOut())
            {
                float dist = Vector3.Distance(transform.position, npc.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    bestCandidate = npc;
                }
            }
        }

        if (bestCandidate != null)
        {
            Debug.Log($"[CashRegister] Processing checkout for NPC: {bestCandidate.name}");
            bestCandidate.TriggerCheckout();
        }
        else
        {
            Debug.Log("[CashRegister] No eligible NPC found near counter to checkout.");
        }
    }
}
