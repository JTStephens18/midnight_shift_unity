using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines placement position and rotation for a single item in a counter slot.
/// Same as ItemPlacement in ShelfSlot but kept separate for clarity.
/// </summary>
[System.Serializable]
public class CounterItemPlacement
{
    [Tooltip("Local position offset for this item.")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("Local rotation for this item (euler angles).")]
    public Vector3 rotationOffset = Vector3.zero;

    [HideInInspector]
    public GameObject placedItem;
}

/// <summary>
/// Represents a slot on the counter where NPCs place items for checkout.
/// Player can press E on individual items to delete them (bagging simulation).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CounterSlot : MonoBehaviour, IPlaceable
{
    [Header("Visualization")]
    [Tooltip("Size of the slot bounding box for visualization and collider.")]
    [SerializeField] private Vector3 slotSize = new Vector3(0.4f, 0.3f, 0.4f);

    [Header("Highlight")]
    [Tooltip("Color of the highlight outline when looking at this slot.")]
    [SerializeField] private Color highlightColor = new Color(0.5f, 0.8f, 1f, 0.8f);

    [Tooltip("Width of the highlight outline.")]
    [SerializeField] private float highlightWidth = 0.02f;

    [Header("Item Placements")]
    [Tooltip("Define positions for each item that can be placed in this slot. Array size = max items.")]
    [SerializeField] private List<CounterItemPlacement> itemPlacements = new List<CounterItemPlacement>() { new CounterItemPlacement() };

    private BoxCollider _collider;
    private LineRenderer _highlightRenderer;
    private bool _isHighlighted = false;
    private int _currentItemCount = 0;

    public bool IsOccupied => _currentItemCount >= itemPlacements.Count;
    public bool HasItems => _currentItemCount > 0;
    public int CurrentItemCount => _currentItemCount;
    public int MaxItems => itemPlacements.Count;
    public List<CounterItemPlacement> ItemPlacements => itemPlacements;
    public Vector3 Position => transform.position;

    private void Awake()
    {
        // Setup collider to match slot size
        _collider = GetComponent<BoxCollider>();
        if (_collider != null)
        {
            _collider.size = slotSize;
            _collider.isTrigger = true;
        }

        // Create highlight outline renderer
        CreateHighlightRenderer();
    }

    private void CreateHighlightRenderer()
    {
        GameObject highlightObj = new GameObject("CounterSlotHighlight");
        highlightObj.transform.SetParent(transform);
        highlightObj.transform.localPosition = Vector3.zero;
        highlightObj.transform.localRotation = Quaternion.identity;
        highlightObj.transform.localScale = Vector3.one;

        _highlightRenderer = highlightObj.AddComponent<LineRenderer>();
        _highlightRenderer.useWorldSpace = true;
        _highlightRenderer.loop = true;
        _highlightRenderer.startWidth = highlightWidth;
        _highlightRenderer.endWidth = highlightWidth;
        _highlightRenderer.positionCount = 16;

        // Setup material
        _highlightRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _highlightRenderer.startColor = highlightColor;
        _highlightRenderer.endColor = highlightColor;

        // Start hidden
        _highlightRenderer.enabled = false;
    }

    /// <summary>
    /// Shows the highlight outline. Call when player looks at this slot.
    /// </summary>
    public void ShowHighlight()
    {
        if (_highlightRenderer != null && !_isHighlighted)
        {
            BoxCollider col = GetComponent<BoxCollider>();
            if (col != null)
            {
                Vector3 size = col.size;
                Vector3 center = col.center;
                Vector3 half = size * 0.5f;

                Vector3[] localCorners = new Vector3[]
                {
                    center + new Vector3(-half.x, -half.y, -half.z),
                    center + new Vector3(half.x, -half.y, -half.z),
                    center + new Vector3(half.x, -half.y, half.z),
                    center + new Vector3(-half.x, -half.y, half.z),
                    center + new Vector3(-half.x, half.y, -half.z),
                    center + new Vector3(half.x, half.y, -half.z),
                    center + new Vector3(half.x, half.y, half.z),
                    center + new Vector3(-half.x, half.y, half.z)
                };

                for (int i = 0; i < localCorners.Length; i++)
                {
                    localCorners[i] = transform.TransformPoint(localCorners[i]);
                }

                _highlightRenderer.positionCount = 16;
                _highlightRenderer.SetPosition(0, localCorners[0]);
                _highlightRenderer.SetPosition(1, localCorners[1]);
                _highlightRenderer.SetPosition(2, localCorners[2]);
                _highlightRenderer.SetPosition(3, localCorners[3]);
                _highlightRenderer.SetPosition(4, localCorners[0]);
                _highlightRenderer.SetPosition(5, localCorners[4]);
                _highlightRenderer.SetPosition(6, localCorners[5]);
                _highlightRenderer.SetPosition(7, localCorners[1]);
                _highlightRenderer.SetPosition(8, localCorners[5]);
                _highlightRenderer.SetPosition(9, localCorners[6]);
                _highlightRenderer.SetPosition(10, localCorners[2]);
                _highlightRenderer.SetPosition(11, localCorners[6]);
                _highlightRenderer.SetPosition(12, localCorners[7]);
                _highlightRenderer.SetPosition(13, localCorners[3]);
                _highlightRenderer.SetPosition(14, localCorners[7]);
                _highlightRenderer.SetPosition(15, localCorners[4]);
            }

            _highlightRenderer.enabled = true;
            _isHighlighted = true;
        }
    }

    /// <summary>
    /// Hides the highlight outline.
    /// </summary>
    public void HideHighlight()
    {
        if (_highlightRenderer != null && _isHighlighted)
        {
            _highlightRenderer.enabled = false;
            _isHighlighted = false;
        }
    }

    private void OnValidate()
    {
        SyncCollider();
    }

    private void Reset()
    {
        SyncCollider();
    }

    [ContextMenu("Sync Collider to Slot Size")]
    private void SyncCollider()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col != null)
        {
            col.size = slotSize;
            col.isTrigger = true;
        }
    }

    #region IPlaceable Implementation

    public bool CanPlaceItem(GameObject item)
    {
        return !IsOccupied;
    }

    public bool TryPlaceItem(GameObject item)
    {
        if (!CanPlaceItem(item)) return false;
        PlaceItem(item);
        return true;
    }

    public string GetPlacementPrompt()
    {
        if (IsOccupied) return "Counter Full";
        return $"Counter ({_currentItemCount}/{itemPlacements.Count})";
    }

    #endregion

    /// <summary>
    /// Places an item in the next available position in this slot.
    /// Called by NPC when placing items on counter.
    /// </summary>
    public void PlaceItem(GameObject item)
    {
        if (_currentItemCount >= itemPlacements.Count) return;

        CounterItemPlacement placement = itemPlacements[_currentItemCount];
        placement.placedItem = item;
        _currentItemCount++;

        // Parent and position
        item.transform.SetParent(transform);
        item.transform.localPosition = placement.positionOffset;

        // Calculate rotation including category offset
        Quaternion placementRot = Quaternion.Euler(placement.rotationOffset);
        Quaternion categoryRot = Quaternion.identity;

        InteractableItem interactable = item.GetComponent<InteractableItem>();
        if (interactable != null && interactable.ItemCategory != null)
        {
            categoryRot = Quaternion.Euler(interactable.ItemCategory.shelfRotationOffset);
        }

        item.transform.localRotation = placementRot * categoryRot;

        // Make item static (kinematic)
        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        Debug.Log($"[CounterSlot] Placed '{item.name}' in counter slot '{gameObject.name}' position {_currentItemCount}/{itemPlacements.Count}");
    }

    /// <summary>
    /// Removes a specific item from this slot by reference.
    /// Used when player interacts with (deletes) an item.
    /// </summary>
    public bool RemoveItem(GameObject item)
    {
        for (int i = 0; i < itemPlacements.Count; i++)
        {
            if (itemPlacements[i].placedItem == item)
            {
                itemPlacements[i].placedItem = null;

                // Shift remaining items down to fill gap
                for (int j = i; j < _currentItemCount - 1; j++)
                {
                    itemPlacements[j].placedItem = itemPlacements[j + 1].placedItem;
                    // Update position of shifted item
                    if (itemPlacements[j].placedItem != null)
                    {
                        itemPlacements[j].placedItem.transform.localPosition = itemPlacements[j].positionOffset;

                        // Calculate rotation including category offset
                        Quaternion placementRot = Quaternion.Euler(itemPlacements[j].rotationOffset);
                        Quaternion categoryRot = Quaternion.identity;

                        InteractableItem interactable = itemPlacements[j].placedItem.GetComponent<InteractableItem>();
                        if (interactable != null && interactable.ItemCategory != null)
                        {
                            categoryRot = Quaternion.Euler(interactable.ItemCategory.shelfRotationOffset);
                        }

                        itemPlacements[j].placedItem.transform.localRotation = placementRot * categoryRot;
                    }
                }
                itemPlacements[_currentItemCount - 1].placedItem = null;
                _currentItemCount--;

                Debug.Log($"[CounterSlot] Removed '{item.name}' from counter slot '{gameObject.name}'");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a specific item is placed in this slot.
    /// </summary>
    public bool ContainsItem(GameObject item)
    {
        foreach (var placement in itemPlacements)
        {
            if (placement.placedItem == item)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the CounterSlot that contains a specific item.
    /// Static helper for ObjectPickup to find the slot.
    /// </summary>
    public static CounterSlot GetSlotContaining(GameObject item)
    {
        CounterSlot[] allSlots = FindObjectsByType<CounterSlot>(FindObjectsSortMode.None);
        foreach (var slot in allSlots)
        {
            if (slot.ContainsItem(item))
                return slot;
        }
        return null;
    }

    /// <summary>
    /// Clears all items from the slot.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < itemPlacements.Count; i++)
        {
            itemPlacements[i].placedItem = null;
        }
        _currentItemCount = 0;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 size = col != null ? col.size : slotSize;
        Vector3 center = col != null ? col.center : Vector3.zero;

        Gizmos.color = IsOccupied ? new Color(1f, 0.5f, 0.3f, 0.5f) : new Color(0.3f, 0.7f, 1f, 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.DrawWireCube(center, size);
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 size = col != null ? col.size : slotSize;
        Vector3 center = col != null ? col.center : Vector3.zero;

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

        Gizmos.color = IsOccupied ? new Color(1f, 0.5f, 0.3f, 0.3f) : new Color(0.3f, 0.7f, 1f, 0.3f);
        Gizmos.DrawCube(center, size);

        Gizmos.color = IsOccupied ? new Color(1f, 0.5f, 0f) : new Color(0.3f, 0.7f, 1f);
        Gizmos.DrawWireCube(center, size);

        // Draw item placement positions
        Gizmos.color = Color.yellow;
        foreach (var placement in itemPlacements)
        {
            Gizmos.DrawWireSphere(center + placement.positionOffset, 0.05f);
        }
    }
#endif
}
