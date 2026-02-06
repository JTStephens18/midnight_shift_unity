using UnityEngine;

/// <summary>
/// Cash register that triggers NPC checkout when the player interacts with it.
/// Attach to the CashRegister object and ensure it has a collider.
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

    [Header("References")]
    [Tooltip("Optional: Specific NPC to checkout. If empty, finds all NPCs in scene.")]
    [SerializeField] private NPCInteractionController targetNPC;

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
        if (targetNPC != null)
        {
            // Checkout specific NPC
            if (!targetNPC.HasCheckedOut())
            {
                Debug.Log("[CashRegister] Processing checkout for NPC");
                targetNPC.TriggerCheckout();
            }
            else
            {
                Debug.Log("[CashRegister] NPC already checked out");
            }
        }
        else
        {
            // Find and checkout all NPCs in scene
            NPCInteractionController[] allNPCs = FindObjectsOfType<NPCInteractionController>();
            int checkedOut = 0;

            foreach (var npc in allNPCs)
            {
                if (!npc.HasCheckedOut())
                {
                    npc.TriggerCheckout();
                    checkedOut++;
                }
            }

            if (checkedOut > 0)
            {
                Debug.Log($"[CashRegister] Processed checkout for {checkedOut} NPC(s)");
            }
            else
            {
                Debug.Log("[CashRegister] No NPCs to checkout");
            }
        }
    }
}
