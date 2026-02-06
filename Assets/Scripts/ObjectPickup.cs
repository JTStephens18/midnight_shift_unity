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

    [Header("Debug")]
    [SerializeField] private bool showDebugRay = true;

    private Camera _playerCamera;
    private GameObject _heldObject;
    private Rigidbody _heldRigidbody;
    private Collider _heldCollider;
    private float _currentHoldDistance;

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
        if (Input.GetKeyDown(interactKey))
        {
            if (_heldObject == null)
            {
                TryPickup();
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

        // Disable gravity and rotation while holding
        _heldRigidbody.useGravity = false;
        _heldRigidbody.freezeRotation = true;

        // Optional: Disable collision with player to prevent pushing
        // You may want to handle this differently based on your setup
        _heldRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void DropObject()
    {
        if (_heldRigidbody != null)
        {
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
    }

    private void ThrowObject()
    {
        if (_heldRigidbody != null)
        {
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
    }

    private void PlaceObject()
    {
        if (_heldRigidbody != null)
        {
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
}
