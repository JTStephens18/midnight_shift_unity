using UnityEngine;

public class ObjectPickup : MonoBehaviour
{
    [Header("Pickup Settings")]
    [SerializeField] private float pickupRange = 3f;
    [SerializeField] private float holdDistance = 1.2f;
    [SerializeField] private float pickupSmoothSpeed = 10f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Throw Settings")]
    [SerializeField] private KeyCode throwKey = KeyCode.F;
    [SerializeField] private float throwForce = 15f;

    [Header("Place Settings")]
    [SerializeField] private KeyCode placeKey = KeyCode.G;

    [Header("Distance Control")]
    [SerializeField] private float scrollSpeed = 0.5f;
    [SerializeField] private float minHoldDistance = 0.5f;
    [SerializeField] private float maxHoldDistance = 4f;

    [Header("References")]
    [SerializeField] private Transform holdPoint;
    [SerializeField] private LayerMask pickupLayerMask = ~0; // Default to all layers

    [Header("Inventory Box Fixed Position")]
    [Tooltip("Local position offset for inventory box when held (fixed in front of camera).")]
    [SerializeField] private Vector3 boxHoldOffset = new Vector3(0.3f, -0.3f, 0.6f);
    [Tooltip("Local rotation for inventory box when held (euler angles).")]
    [SerializeField] private Vector3 boxHoldRotation = new Vector3(10f, -15f, 0f);

    [Header("Debug")]
    [SerializeField] private bool showDebugRay = true;

    private Camera _playerCamera;
    private GameObject _heldObject;
    private Rigidbody _heldRigidbody;
    private Collider _heldCollider;
    private float _currentHoldDistance;
    private IPlaceable _currentPlaceable;
    private bool _isHoldingInventoryBox = false;

    void Start()
    {
        _playerCamera = GetComponent<Camera>();
        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
        }

        // Create a hold point if one wasn't assigned
        if (holdPoint == null)
        {
            GameObject holdPointObj = new GameObject("HoldPoint");
            holdPoint = holdPointObj.transform;
            holdPoint.SetParent(_playerCamera.transform);
            holdPoint.localPosition = new Vector3(0f, 0f, holdDistance);
        }

        _currentHoldDistance = holdDistance;
    }

    void Update()
    {
        // Always detect placeable targets for highlight (some slots show even without held item)
        DetectPlaceable();

        if (Input.GetKeyDown(interactKey))
        {
            if (_heldObject == null)
            {
                TryPickup();
            }
            // Check if we're in box-to-shelf placement mode
            else if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsPlacementReady())
            {
                // Place from inventory box onto shelf
                ItemPlacementManager.Instance.TryPlaceFromBox();
            }
            else if (_currentPlaceable != null && _currentPlaceable.CanPlaceItem(_heldObject))
            {
                // Place on shelf instead of dropping
                PlaceOnShelf();
            }
            else if (_currentPlaceable != null && !_currentPlaceable.CanPlaceItem(_heldObject))
            {
                // Trying to place on a slot that rejects the item - shake feedback
                if (MouseLook.Instance != null)
                {
                    MouseLook.Instance.Shake();
                }
                Debug.Log("[ObjectPickup] Cannot place item here - wrong category or slot full");
            }
            else
            {
                DropObject();
            }
        }

        // Throw held object
        if (Input.GetKeyDown(throwKey) && _heldObject != null)
        {
            ThrowObject();
        }

        // Place held object gently
        if (Input.GetKeyDown(placeKey) && _heldObject != null)
        {
            PlaceObject();
        }

        // Scroll wheel to adjust hold distance
        if (_heldObject != null)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                _currentHoldDistance += scroll * scrollSpeed;
                _currentHoldDistance = Mathf.Clamp(_currentHoldDistance, minHoldDistance, maxHoldDistance);
                holdPoint.localPosition = new Vector3(0f, 0f, _currentHoldDistance);
            }
        }

        // Show debug ray in Scene view
        if (showDebugRay && _playerCamera != null)
        {
            Debug.DrawRay(_playerCamera.transform.position, _playerCamera.transform.forward * pickupRange, Color.yellow);
        }
    }

    void FixedUpdate()
    {
        // Skip physics-based holding for inventory box (it's fixed to camera)
        if (_isHoldingInventoryBox) return;

        // Smoothly move held object to hold point
        if (_heldObject != null && _heldRigidbody != null)
        {
            Vector3 targetPosition = holdPoint.position;
            Vector3 direction = targetPosition - _heldObject.transform.position;

            // Use velocity-based movement for smoother physics interaction
            _heldRigidbody.linearVelocity = direction * pickupSmoothSpeed;

            // Optionally, you can also smoothly rotate the object to match camera rotation
            // _heldRigidbody.MoveRotation(Quaternion.Lerp(_heldRigidbody.rotation, holdPoint.rotation, Time.fixedDeltaTime * pickupSmoothSpeed));
        }
    }

    private void TryPickup()
    {
        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickupLayerMask))
        {
            // Check if the object has a Rigidbody (is pickupable)
            Rigidbody rb = hit.collider.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = hit.collider.GetComponentInParent<Rigidbody>();
            }

            if (rb != null && !rb.isKinematic)
            {
                PickupObject(hit.collider.gameObject, rb, hit.collider);
            }
        }
    }

    private void PickupObject(GameObject obj, Rigidbody rb, Collider col)
    {
        _heldObject = obj;
        _heldRigidbody = rb;
        _heldCollider = col;

        // Check if this is an inventory box
        _isHoldingInventoryBox = obj.GetComponent<InventoryBox>() != null;

        if (_isHoldingInventoryBox)
        {
            // Fixed position: Clear velocity first, THEN make kinematic
            // (Can't set velocity on kinematic bodies)
            _heldRigidbody.linearVelocity = Vector3.zero;
            _heldRigidbody.angularVelocity = Vector3.zero;
            _heldRigidbody.useGravity = false;
            _heldRigidbody.isKinematic = true;

            // Disable collider to prevent blocking view
            if (_heldCollider != null)
                _heldCollider.enabled = false;

            // Parent to camera and set fixed position
            _heldObject.transform.SetParent(_playerCamera.transform);
            _heldObject.transform.localPosition = boxHoldOffset;
            _heldObject.transform.localRotation = Quaternion.Euler(boxHoldRotation);
        }
        else
        {
            // Normal physics-based holding
            _heldRigidbody.useGravity = false;
            _heldRigidbody.freezeRotation = true;
            _heldRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void DropObject()
    {
        if (_heldRigidbody != null)
        {
            if (_isHoldingInventoryBox)
            {
                // Unparent and re-enable physics for inventory box
                _heldObject.transform.SetParent(null);

                if (_heldCollider != null)
                    _heldCollider.enabled = true;

                _heldRigidbody.isKinematic = false;
            }

            // Re-enable gravity and rotation
            _heldRigidbody.useGravity = true;
            _heldRigidbody.freezeRotation = false;
            _heldRigidbody.interpolation = RigidbodyInterpolation.None;

            // Give it a slight forward velocity when dropping
            _heldRigidbody.linearVelocity = _playerCamera.transform.forward * 2f;
        }

        _heldObject = null;
        _heldRigidbody = null;
        _heldCollider = null;
        _isHoldingInventoryBox = false;
    }

    private void ThrowObject()
    {
        if (_heldRigidbody != null)
        {
            if (_isHoldingInventoryBox)
            {
                // Unparent and re-enable physics for inventory box
                _heldObject.transform.SetParent(null);

                if (_heldCollider != null)
                    _heldCollider.enabled = true;

                _heldRigidbody.isKinematic = false;
            }

            // Re-enable gravity and rotation
            _heldRigidbody.useGravity = true;
            _heldRigidbody.freezeRotation = false;
            _heldRigidbody.interpolation = RigidbodyInterpolation.None;

            // Apply throw force in camera direction
            _heldRigidbody.linearVelocity = _playerCamera.transform.forward * throwForce;
        }

        _heldObject = null;
        _heldRigidbody = null;
        _heldCollider = null;
        _isHoldingInventoryBox = false;
    }

    private void PlaceObject()
    {
        if (_heldRigidbody != null)
        {
            if (_isHoldingInventoryBox)
            {
                // Unparent and re-enable physics for inventory box
                _heldObject.transform.SetParent(null);

                if (_heldCollider != null)
                    _heldCollider.enabled = true;

                _heldRigidbody.isKinematic = false;
            }

            // Re-enable gravity and rotation
            _heldRigidbody.useGravity = true;
            _heldRigidbody.freezeRotation = false;
            _heldRigidbody.interpolation = RigidbodyInterpolation.None;

            // Set velocity to zero for a gentle placement
            _heldRigidbody.linearVelocity = Vector3.zero;
            _heldRigidbody.angularVelocity = Vector3.zero;
        }

        _heldObject = null;
        _heldRigidbody = null;
        _heldCollider = null;
        _isHoldingInventoryBox = false;
    }

    private void DetectPlaceable()
    {
        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        IPlaceable newPlaceable = null;

        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickupLayerMask))
        {
            newPlaceable = hit.collider.GetComponent<IPlaceable>();
            if (newPlaceable == null)
                newPlaceable = hit.collider.GetComponentInParent<IPlaceable>();
        }

        // Handle highlight changes
        if (newPlaceable != _currentPlaceable)
        {
            // Hide highlight on previous slot
            if (_currentPlaceable is ShelfSlot previousSlot)
            {
                previousSlot.HideHighlight();
            }

            // Show highlight on new slot (respecting RequireHeldItem setting)
            if (newPlaceable is ShelfSlot newSlot)
            {
                bool shouldShowHighlight = !newSlot.RequireHeldItem || _heldObject != null;
                if (shouldShowHighlight)
                {
                    newSlot.ShowHighlight();
                }
            }
        }

        _currentPlaceable = newPlaceable;
    }

    private void PlaceOnShelf()
    {
        if (_currentPlaceable == null || _heldObject == null) return;

        // Prepare the object for placement
        if (_heldRigidbody != null)
        {
            _heldRigidbody.linearVelocity = Vector3.zero;
            _heldRigidbody.angularVelocity = Vector3.zero;
        }

        // Place the item
        if (_currentPlaceable.TryPlaceItem(_heldObject))
        {
            // Clear held references
            _heldObject = null;
            _heldRigidbody = null;
            _heldCollider = null;
            _currentPlaceable = null;
        }
    }

    // Public method to check if currently looking at a placeable
    public bool IsLookingAtPlaceable()
    {
        return _currentPlaceable != null;
    }

    // Public method to get the current placeable's prompt
    public string GetPlaceablePrompt()
    {
        return _currentPlaceable?.GetPlacementPrompt() ?? string.Empty;
    }

    // Public method to check if currently holding an object
    public bool IsHoldingObject()
    {
        return _heldObject != null;
    }

    // Public method to get the held object
    public GameObject GetHeldObject()
    {
        return _heldObject;
    }

    // Force drop (can be called externally if needed)
    public void ForceDropObject()
    {
        if (_heldObject != null)
        {
            DropObject();
        }
    }

    // Check if currently holding an InventoryBox
    public bool IsHoldingInventoryBox()
    {
        return _heldObject != null && _heldObject.GetComponent<InventoryBox>() != null;
    }
}
