using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the box-to-shelf item placement workflow with proximity-based shelf detection.
/// Attach to the player or camera object alongside ObjectPickup.
/// </summary>
public class ItemPlacementManager : MonoBehaviour
{
    public enum PlacementState { Idle, Ready, Disabled }

    [Header("References")]
    [SerializeField] private ObjectPickup objectPickup;

    [Header("Proximity Detection")]
    [Tooltip("Range to detect nearby shelf sections.")]
    [SerializeField] private float shelfDetectionRange = 3f;

    [Tooltip("Layer mask for shelf detection.")]
    [SerializeField] private LayerMask shelfLayerMask = ~0;

    [Header("Ghost Preview")]
    [Tooltip("Material to apply to ghost preview objects (should be semi-transparent).")]
    [SerializeField] private Material ghostMaterial;

    [Tooltip("Color for the ghost preview.")]
    [SerializeField] private Color ghostColor = new Color(0.5f, 1f, 0.5f, 0.5f);

    [Header("UI Prompts")]
    [SerializeField] private string equipBoxPrompt = "Equip box to restock";
    [SerializeField] private string emptyBoxPrompt = "Box is empty";
    [SerializeField] private string slotFullPrompt = "Slot full";
    [SerializeField] private string categoryMismatchPrompt = "Wrong item type";

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = false;

    // State
    public PlacementState State { get; private set; } = PlacementState.Idle;
    public ItemCategory CurrentlySelectedCategory { get; private set; }
    public string CurrentPrompt { get; private set; } = string.Empty;

    // Cached references
    private InventoryBox _activeBox;
    private ShelfSection _nearbyShelf;
    private ShelfSlot _targetSlot;
    private GameObject _ghostPreviewInstance;
    private Camera _playerCamera;

    // Random selection tracking
    private List<ItemCategory> _currentMissingItems = new List<ItemCategory>();
    private ItemCategory _randomlySelectedCategory;

    // Singleton for easy access
    public static ItemPlacementManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _playerCamera = Camera.main;

        if (objectPickup == null)
        {
            objectPickup = GetComponent<ObjectPickup>();
            if (objectPickup == null)
                objectPickup = FindFirstObjectByType<ObjectPickup>();
        }
    }

    private void Update()
    {
        UpdatePlacementState();
    }

    private void UpdatePlacementState()
    {
        // Check if player is holding an inventory box
        _activeBox = GetHeldInventoryBox();

        if (_activeBox == null)
        {
            SetState(PlacementState.Idle, equipBoxPrompt);
            ClearProximityState();
            return;
        }

        // Detect nearby shelf sections
        ShelfSection newNearbyShelf = DetectNearbyShelfSection();

        // If shelf changed, recalculate missing items and select random
        if (newNearbyShelf != _nearbyShelf)
        {
            _nearbyShelf = newNearbyShelf;
            OnShelfProximityChanged();
        }

        // If no nearby shelf while holding box
        if (_nearbyShelf == null)
        {
            SetState(PlacementState.Idle, string.Empty);
            ClearGhostPreview();
            _targetSlot = null;
            return;
        }

        // Check if looking at a specific slot on the nearby shelf
        _targetSlot = GetTargetedShelfSlot();

        if (_targetSlot == null)
        {
            // Near shelf but not aiming at a slot - show what item is selected
            if (_randomlySelectedCategory != null)
            {
                SetState(PlacementState.Idle, $"Aim at slot to place: {_randomlySelectedCategory.name}");
            }
            else
            {
                SetState(PlacementState.Disabled, "Shelf is fully stocked");
            }
            ClearGhostPreview();
            return;
        }

        // Phase I: Validate the targeted slot
        ItemCategory slotCategory = _targetSlot.AcceptedCategory;

        // Check if slot is full
        if (_targetSlot.IsOccupied)
        {
            SetState(PlacementState.Disabled, slotFullPrompt);
            ClearGhostPreview();
            return;
        }

        // Check if the randomly selected item matches this slot's category
        if (slotCategory != null && _randomlySelectedCategory != null && slotCategory != _randomlySelectedCategory)
        {
            SetState(PlacementState.Disabled, $"{categoryMismatchPrompt} - needs {slotCategory.name}");
            ClearGhostPreview();
            return;
        }

        // Check if box has stock for this slot's category
        ItemCategory categoryToPlace = slotCategory ?? _randomlySelectedCategory;
        if (categoryToPlace != null && !_activeBox.HasStock(categoryToPlace))
        {
            SetState(PlacementState.Disabled, $"Out of {categoryToPlace.name} items");
            ClearGhostPreview();
            return;
        }

        // If no category determined, check for any stock
        if (categoryToPlace == null && !_activeBox.HasAnyStock())
        {
            SetState(PlacementState.Disabled, emptyBoxPrompt);
            ClearGhostPreview();
            return;
        }

        // Phase II: Ready for placement
        CurrentlySelectedCategory = categoryToPlace;

        int currentCount = _targetSlot.CurrentItemCount;
        int maxCount = _targetSlot.MaxItems;
        string categoryName = categoryToPlace != null ? categoryToPlace.name : "Item";
        string prompt = $"Press E to Place {categoryName} ({currentCount + 1}/{maxCount})";

        SetState(PlacementState.Ready, prompt);
        UpdateGhostPreview();
    }

    /// <summary>
    /// Called when the player enters/exits proximity of a shelf section.
    /// Recalculates missing items and randomly selects one.
    /// </summary>
    private void OnShelfProximityChanged()
    {
        _currentMissingItems.Clear();
        _randomlySelectedCategory = null;
        CurrentlySelectedCategory = null;

        if (_nearbyShelf == null || _activeBox == null) return;

        // Get all missing items from the shelf
        List<ItemCategory> allMissing = _nearbyShelf.GetMissingItems();

        // Filter to only items the box has in stock
        foreach (ItemCategory category in allMissing)
        {
            if (_activeBox.HasStock(category))
            {
                _currentMissingItems.Add(category);
            }
        }

        // Randomly select one item to place
        if (_currentMissingItems.Count > 0)
        {
            int randomIndex = Random.Range(0, _currentMissingItems.Count);
            _randomlySelectedCategory = _currentMissingItems[randomIndex];

            if (logStateChanges)
                Debug.Log($"[ItemPlacementManager] Near shelf '{_nearbyShelf.name}'. Missing {_currentMissingItems.Count} items. Selected: {_randomlySelectedCategory.name}");
        }
        else
        {
            if (logStateChanges)
                Debug.Log($"[ItemPlacementManager] Near shelf '{_nearbyShelf.name}' but no placeable items (shelf full or box empty).");
        }
    }

    /// <summary>
    /// Attempts to place an item from the box onto the targeted shelf slot.
    /// Called by ObjectPickup when interact key is pressed.
    /// </summary>
    public bool TryPlaceFromBox()
    {
        if (State != PlacementState.Ready) return false;
        if (_activeBox == null || _targetSlot == null) return false;

        // Determine category to use
        ItemCategory category = CurrentlySelectedCategory;

        if (category == null)
        {
            Debug.LogWarning("[ItemPlacementManager] No category selected for placement");
            return false;
        }

        // Get prefab for this category
        GameObject prefab = _activeBox.GetItemPrefab(category);
        if (prefab == null)
        {
            Debug.LogWarning($"[ItemPlacementManager] No prefab found for category {category.name}");
            return false;
        }

        // Spawn the item
        GameObject itemInstance = Instantiate(prefab);

        // Try to place on shelf
        if (_targetSlot.TryPlaceItem(itemInstance))
        {
            // Decrement box inventory
            _activeBox.TryDecrement(category);

            // Clear ghost preview
            ClearGhostPreview();

            if (logStateChanges)
                Debug.Log($"[ItemPlacementManager] Placed {category.name} on {_targetSlot.gameObject.name}");

            // Re-evaluate missing items for this shelf
            OnShelfProximityChanged();

            return true;
        }
        else
        {
            // Placement failed - destroy spawned item
            Destroy(itemInstance);

            // Trigger screen shake
            if (MouseLook.Instance != null)
                MouseLook.Instance.Shake();

            return false;
        }
    }

    /// <summary>
    /// Returns true if the player is currently holding an InventoryBox and looking at a valid slot.
    /// </summary>
    public bool IsPlacementReady()
    {
        return State == PlacementState.Ready;
    }

    /// <summary>
    /// Returns true if the player is holding an inventory box.
    /// </summary>
    public bool IsHoldingBox()
    {
        return _activeBox != null;
    }

    /// <summary>
    /// Returns the currently randomly selected item category to place.
    /// </summary>
    public ItemCategory GetSelectedCategory()
    {
        return _randomlySelectedCategory;
    }

    private InventoryBox GetHeldInventoryBox()
    {
        if (objectPickup == null) return null;

        GameObject heldObject = objectPickup.GetHeldObject();
        if (heldObject == null) return null;

        return heldObject.GetComponent<InventoryBox>();
    }

    private ShelfSection DetectNearbyShelfSection()
    {
        if (_playerCamera == null) return null;

        // Use overlap sphere to find nearby shelf sections
        Collider[] colliders = Physics.OverlapSphere(
            _playerCamera.transform.position,
            shelfDetectionRange,
            shelfLayerMask
        );

        ShelfSection nearestShelf = null;
        float nearestDistance = float.MaxValue;

        foreach (Collider col in colliders)
        {
            ShelfSection section = col.GetComponent<ShelfSection>();
            if (section == null)
                section = col.GetComponentInParent<ShelfSection>();

            if (section != null)
            {
                float distance = Vector3.Distance(_playerCamera.transform.position, section.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestShelf = section;
                }
            }
        }

        return nearestShelf;
    }

    private ShelfSlot GetTargetedShelfSlot()
    {
        if (_playerCamera == null) return null;

        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, shelfDetectionRange))
        {
            ShelfSlot slot = hit.collider.GetComponent<ShelfSlot>();
            if (slot == null)
                slot = hit.collider.GetComponentInParent<ShelfSlot>();

            // Only return slots that belong to the nearby shelf
            if (slot != null && _nearbyShelf != null)
            {
                // Verify the slot is part of the nearby shelf
                if (_nearbyShelf.GetSlots().Contains(slot))
                    return slot;
            }

            return slot;
        }
        return null;
    }

    private void SetState(PlacementState newState, string prompt)
    {
        if (State != newState && logStateChanges)
        {
            Debug.Log($"[ItemPlacementManager] State: {State} → {newState}");
        }

        State = newState;
        CurrentPrompt = prompt;
    }

    private void ClearProximityState()
    {
        ClearGhostPreview();
        _nearbyShelf = null;
        _targetSlot = null;
        _currentMissingItems.Clear();
        _randomlySelectedCategory = null;
        CurrentlySelectedCategory = null;
    }

    private void UpdateGhostPreview()
    {
        if (_activeBox == null || _targetSlot == null) return;

        ItemCategory category = CurrentlySelectedCategory;
        if (category == null) return;

        GameObject prefab = _activeBox.GetItemPrefab(category);
        if (prefab == null) return;

        // Get next placement position from slot
        int nextIndex = _targetSlot.CurrentItemCount;
        if (nextIndex >= _targetSlot.ItemPlacements.Count) return;

        ItemPlacement placement = _targetSlot.ItemPlacements[nextIndex];
        Vector3 worldPos = _targetSlot.transform.TransformPoint(placement.positionOffset);
        Quaternion worldRot = _targetSlot.transform.rotation * Quaternion.Euler(placement.rotationOffset);

        // Create or update ghost preview
        if (_ghostPreviewInstance == null)
        {
            _ghostPreviewInstance = Instantiate(prefab);
            _ghostPreviewInstance.name = "GhostPreview";

            // Disable physics and colliders
            Rigidbody rb = _ghostPreviewInstance.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            Collider[] colliders = _ghostPreviewInstance.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
                col.enabled = false;

            // Apply ghost material
            ApplyGhostMaterial(_ghostPreviewInstance);
        }

        // Update position and rotation
        _ghostPreviewInstance.transform.position = worldPos;
        _ghostPreviewInstance.transform.rotation = worldRot;
    }

    private void ApplyGhostMaterial(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Material[] mats = new Material[renderer.materials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                if (ghostMaterial != null)
                {
                    mats[i] = new Material(ghostMaterial);
                }
                else
                {
                    // Create simple transparent material
                    mats[i] = new Material(Shader.Find("Sprites/Default"));
                }
                mats[i].color = ghostColor;
            }
            renderer.materials = mats;
        }
    }

    private void ClearGhostPreview()
    {
        if (_ghostPreviewInstance != null)
        {
            Destroy(_ghostPreviewInstance);
            _ghostPreviewInstance = null;
        }
    }

    private void OnDisable()
    {
        ClearProximityState();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw shelf detection range
        if (_playerCamera != null)
        {
            Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.3f);
            Gizmos.DrawWireSphere(_playerCamera.transform.position, shelfDetectionRange);
        }
    }
#endif
}
