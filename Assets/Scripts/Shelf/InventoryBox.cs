using System.Collections;
using UnityEngine;

/// <summary>
/// Portable container that holds a fixed number of items for shelf restocking.
/// Items are category-agnostic — the ItemPlacementManager decides what to spawn.
/// Attach to a pickupable object (requires Rigidbody).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class InventoryBox : MonoBehaviour
{
    [Header("Inventory")]
    [Tooltip("Total number of items this box can dispense before being destroyed.")]
    [SerializeField] private int totalItems = 8;

    [Header("Shrink Animation")]
    [Tooltip("Duration of the shrink animation when the box is emptied.")]
    [SerializeField] private float shrinkDuration = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool logOperations = false;

    private int _remainingItems;
    private bool _isDestroying = false;

    private void Awake()
    {
        _remainingItems = totalItems;
    }

    /// <summary>
    /// Returns true if the box still has items to dispense.
    /// </summary>
    public bool HasAnyStock()
    {
        return _remainingItems > 0 && !_isDestroying;
    }

    /// <summary>
    /// Gets the number of items remaining in the box.
    /// </summary>
    public int GetRemainingCount()
    {
        return _remainingItems;
    }

    /// <summary>
    /// Gets the total starting capacity of the box.
    /// </summary>
    public int GetTotalCapacity()
    {
        return totalItems;
    }

    /// <summary>
    /// Decrements the item count by one. If the box is now empty,
    /// triggers the shrink animation and destroys the box.
    /// </summary>
    public void Decrement()
    {
        if (_remainingItems <= 0 || _isDestroying) return;

        _remainingItems--;

        if (logOperations)
            Debug.Log($"[InventoryBox] Decremented. Remaining: {_remainingItems}/{totalItems}");

        if (_remainingItems <= 0)
        {
            StartCoroutine(ShrinkAndDestroy());
        }
    }

    /// <summary>
    /// Smoothly shrinks the box to zero scale, then destroys it.
    /// </summary>
    private IEnumerator ShrinkAndDestroy()
    {
        _isDestroying = true;

        if (logOperations)
            Debug.Log("[InventoryBox] Box is empty. Starting shrink animation...");

        Vector3 startScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / shrinkDuration);
            // SmoothStep gives a nice ease-in-out curve
            float smooth = Mathf.SmoothStep(1f, 0f, t);
            transform.localScale = startScale * smooth;
            yield return null;
        }

        transform.localScale = Vector3.zero;

        // Force drop if player is holding this box
        ObjectPickup pickup = FindFirstObjectByType<ObjectPickup>();
        if (pickup != null && pickup.GetHeldObject() == gameObject)
        {
            pickup.ForceDropObject();
        }

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (totalItems < 1)
            totalItems = 1;
    }
#endif
}
