using UnityEngine;

/// <summary>
/// Manages the box-to-shelf item placement workflow.
/// Attach to the player or camera object alongside ObjectPickup.
/// </summary>
public class ItemPlacementManager : MonoBehaviour
{
    public enum PlacementState { Idle, Ready, Disabled }

    [Header("References")]
    [SerializeField] private ObjectPickup objectPickup;

    [Header("Ghost Preview")]
    [Tooltip("Material to apply to ghost preview objects (should be semi-transparent).")]
    [SerializeField] private Material ghostMaterial;

    [Tooltip("Color for the ghost preview.")]
    [SerializeField] private Color ghostColor = new Color(0.5f, 1f, 0.5f, 0.5f);

    [Header("UI Prompts")]
    [SerializeField] private string equipBoxPrompt = "Equip box to restock";
    [SerializeField] private string emptyBoxPrompt = "Box is empty";
    [SerializeField] private string slotFullPrompt = "Slot full";

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = false;

    // State
    public PlacementState State { get; private set; } = PlacementState.Idle;
    public ItemCategory CurrentlySelectedCategory { get; private set; }
    public string CurrentPrompt { get; private set; } = string.Empty;

    // Cached references
    private InventoryBox _activeBox;
    private ShelfSlot _targetSlot;
    private GameObject _ghostPreviewInstance;
    private Camera _playerCamera;

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
            ClearGhostPreview();
            _targetSlot = null;
            CurrentlySelectedCategory = null;
            return;
        }

        // Check if looking at a shelf slot
        _targetSlot = GetTargetedShelfSlot();

        if (_targetSlot == null)
        {
            SetState(PlacementState.Idle, string.Empty);
            ClearGhostPreview();
            CurrentlySelectedCategory = null;
            return;
        }

        // Phase I: Validation
        ItemCategory neededCategory = _targetSlot.AcceptedCategory;

        // Check if slot is full
        if (_targetSlot.IsOccupied)
        {
            SetState(PlacementState.Disabled, slotFullPrompt);
            ClearGhostPreview();
            CurrentlySelectedCategory = null;
            return;
        }

        // Check if box has stock (if category is specified)
        if (neededCategory != null && !_activeBox.HasStock(neededCategory))
        {
            SetState(PlacementState.Disabled, $"Out of {neededCategory.name} items");
            ClearGhostPreview();
            CurrentlySelectedCategory = null;
            return;
        }

        // If no category specified, check if box has any stock
        if (neededCategory == null && !_activeBox.HasAnyStock())
        {
            SetState(PlacementState.Disabled, emptyBoxPrompt);
            ClearGhostPreview();
            CurrentlySelectedCategory = null;
            return;
        }

        // Phase II: Ready for placement
        CurrentlySelectedCategory = neededCategory;

        int currentCount = _targetSlot.CurrentItemCount;
        int maxCount = _targetSlot.MaxItems;
        string categoryName = neededCategory != null ? neededCategory.name : "Item";
        string prompt = $"Press E to Place {categoryName} ({currentCount + 1}/{maxCount})";

        SetState(PlacementState.Ready, prompt);
        UpdateGhostPreview();
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

        // If slot accepts any category, use first available from box
        if (category == null)
        {
            var available = _activeBox.GetAvailableCategories();
            if (available.Count == 0) return false;
            category = available[0];
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

            // Clear ghost preview (will be recreated next frame if needed)
            ClearGhostPreview();

            if (logStateChanges)
                Debug.Log($"[ItemPlacementManager] Placed {category.name} on {_targetSlot.gameObject.name}");

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

    private InventoryBox GetHeldInventoryBox()
    {
        if (objectPickup == null) return null;

        GameObject heldObject = objectPickup.GetHeldObject();
        if (heldObject == null) return null;

        return heldObject.GetComponent<InventoryBox>();
    }

    private ShelfSlot GetTargetedShelfSlot()
    {
        if (_playerCamera == null) return null;

        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, 3f))
        {
            ShelfSlot slot = hit.collider.GetComponent<ShelfSlot>();
            if (slot == null)
                slot = hit.collider.GetComponentInParent<ShelfSlot>();
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

    private void UpdateGhostPreview()
    {
        if (_activeBox == null || _targetSlot == null) return;

        ItemCategory category = CurrentlySelectedCategory;

        // If no specific category, use first available
        if (category == null)
        {
            var available = _activeBox.GetAvailableCategories();
            if (available.Count == 0) return;
            category = available[0];
        }

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
        ClearGhostPreview();
    }
}
