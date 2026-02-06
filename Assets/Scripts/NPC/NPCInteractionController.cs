using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Controls NPC behavior for detecting, navigating to, picking up, and delivering items.
/// Requires a NavMeshAgent component on the same GameObject.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NPCInteractionController : MonoBehaviour
{
    [Header("Detection Settings")]
    [Tooltip("Radius within which the NPC scans for interactable items.")]
    [SerializeField] private float detectionRadius = 10f;

    [Tooltip("Layer mask for interactable items. Set to 'Interactable' layer.")]
    [SerializeField] private LayerMask itemLayerMask;

    [Header("Interaction Settings")]
    [Tooltip("Distance at which the NPC can reach and pick up an item.")]
    [SerializeField] private float reachDistance = 0.75f;

    [Tooltip("The NPC's hand transform where picked up items will be parented.")]
    [SerializeField] private Transform handBone;

    [Header("Counter Settings")]
    [Tooltip("The spawn point on the counter where items will be placed.")]
    [SerializeField] private Transform counterSpawn;

    [Header("Behavior Settings")]
    [Tooltip("Automatically scan for items at regular intervals.")]
    [SerializeField] private bool autoScan = true;

    [Tooltip("Time between automatic scans in seconds.")]
    [SerializeField] private float scanInterval = 1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    private NavMeshAgent _agent;
    private IInteractable _currentTarget;
    private GameObject _currentTargetObject;
    private InteractableItem _heldItem;
    private float _scanTimer;
    private bool _hasStartedMoving;

    // States for the interaction flow
    private enum NPCState { Idle, MovingToItem, PickingUp, MovingToCounter, PlacingItem }
    private NPCState _currentState = NPCState.Idle;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.stoppingDistance = reachDistance;
    }

    private void Update()
    {
        switch (_currentState)
        {
            case NPCState.Idle:
                HandleIdleState();
                break;

            case NPCState.MovingToItem:
                HandleMovingToItemState();
                break;

            case NPCState.PickingUp:
                HandlePickupState();
                break;

            case NPCState.MovingToCounter:
                HandleMovingToCounterState();
                break;

            case NPCState.PlacingItem:
                HandlePlacingState();
                break;
        }
    }

    /// <summary>
    /// Idle state: scan for items periodically if autoScan is enabled.
    /// </summary>
    private void HandleIdleState()
    {
        if (!autoScan) return;

        _scanTimer += Time.deltaTime;
        if (_scanTimer >= scanInterval)
        {
            _scanTimer = 0f;
            ScanForItems();
        }
    }

    /// <summary>
    /// Moving to item state: check if NPC has reached the target item.
    /// </summary>
    private void HandleMovingToItemState()
    {
        if (_currentTargetObject == null || _currentTarget == null)
        {
            CancelCurrentAction();
            return;
        }

        // Wait for path to be calculated and agent to start moving
        if (_agent.pathPending) return;

        // Mark that we've started moving once we have a valid path
        if (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance)
        {
            _hasStartedMoving = true;
        }

        // Only check arrival after we've actually started moving
        if (_hasStartedMoving && _agent.remainingDistance <= _agent.stoppingDistance && !_agent.pathPending)
        {
            Debug.Log($"[NPC] Arrived at item: {_currentTargetObject.name}");
            _hasStartedMoving = false;
            _currentState = NPCState.PickingUp;
        }
    }

    /// <summary>
    /// Pickup state: execute the pickup and move to counter.
    /// </summary>
    private void HandlePickupState()
    {
        // Cache the held item BEFORE calling OnPickedUp
        if (_currentTargetObject != null)
        {
            _heldItem = _currentTargetObject.GetComponent<InteractableItem>();
        }

        if (_currentTarget != null && handBone != null && _heldItem != null)
        {
            Debug.Log($"[NPC] Picking up: {_heldItem.gameObject.name}");
            _currentTarget.OnPickedUp(handBone);
        }
        else
        {
            Debug.LogWarning($"[NPC] Pickup failed - Target: {_currentTarget != null}, HandBone: {handBone != null}, HeldItem: {_heldItem != null}");
        }

        // Clear pickup target
        _currentTarget = null;
        _currentTargetObject = null;

        // Move to counter if assigned and we have an item
        if (counterSpawn != null && _heldItem != null)
        {
            Debug.Log($"[NPC] Moving to counter at {counterSpawn.position}");
            _agent.SetDestination(counterSpawn.position);
            _currentState = NPCState.MovingToCounter;
        }
        else
        {
            if (counterSpawn == null) Debug.LogWarning("[NPC] No counter spawn assigned!");
            _currentState = NPCState.Idle;
        }
    }

    /// <summary>
    /// Moving to counter state: check if NPC has reached the counter.
    /// </summary>
    private void HandleMovingToCounterState()
    {
        if (_agent.pathPending) return;

        // Mark that we've started moving
        if (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance)
        {
            _hasStartedMoving = true;
        }

        if (_hasStartedMoving && _agent.remainingDistance <= _agent.stoppingDistance && !_agent.pathPending)
        {
            Debug.Log("[NPC] Arrived at counter");
            _hasStartedMoving = false;
            _currentState = NPCState.PlacingItem;
        }
    }

    /// <summary>
    /// Placing state: place the item on the counter and return to idle.
    /// </summary>
    private void HandlePlacingState()
    {
        if (_heldItem != null && counterSpawn != null)
        {
            Debug.Log($"[NPC] Placing {_heldItem.gameObject.name} at counter");
            _heldItem.PlaceAt(counterSpawn.position);
        }
        else
        {
            Debug.LogWarning($"[NPC] Place failed - HeldItem: {_heldItem != null}, CounterSpawn: {counterSpawn != null}");
        }

        _heldItem = null;
        _currentState = NPCState.Idle;
    }

    /// <summary>
    /// Scans for nearby interactable items and selects the nearest one.
    /// </summary>
    public void ScanForItems()
    {
        // Don't scan if already holding an item
        if (_heldItem != null) return;

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, detectionRadius, itemLayerMask);

        if (hitColliders.Length == 0)
        {
            return;
        }

        // Find the nearest item that implements IInteractable
        float nearestDistance = float.MaxValue;
        IInteractable nearestInteractable = null;
        GameObject nearestObject = null;

        foreach (Collider col in hitColliders)
        {
            IInteractable interactable = col.GetComponent<IInteractable>();
            if (interactable == null)
            {
                interactable = col.GetComponentInParent<IInteractable>();
            }

            if (interactable != null)
            {
                float distance = Vector3.Distance(transform.position, col.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestInteractable = interactable;
                    nearestObject = col.gameObject;
                }
            }
        }

        if (nearestInteractable != null)
        {
            SetTarget(nearestInteractable, nearestObject);
        }
    }

    /// <summary>
    /// Sets the current target item and begins navigation.
    /// </summary>
    public void SetTarget(IInteractable target, GameObject targetObject)
    {
        _currentTarget = target;
        _currentTargetObject = targetObject;

        Vector3 targetPosition = target.GetInteractionPoint();

        // Check if we can path to the target
        NavMeshPath path = new NavMeshPath();
        bool pathValid = _agent.CalculatePath(targetPosition, path);

        if (!pathValid || path.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogError($"[NPCInteractionController] Cannot path to {targetObject.name}! NavMesh status: {path.status}.");
            CancelCurrentAction();
            return;
        }

        _agent.SetDestination(targetPosition);
        _currentState = NPCState.MovingToItem;
    }

    /// <summary>
    /// Cancels the current action and returns to idle state.
    /// </summary>
    public void CancelCurrentAction()
    {
        _agent.ResetPath();
        _currentTarget = null;
        _currentTargetObject = null;
        _currentState = NPCState.Idle;
    }

    /// <summary>
    /// Returns true if the NPC currently has a target item.
    /// </summary>
    public bool HasTarget()
    {
        return _currentTarget != null;
    }

    /// <summary>
    /// Returns true if the NPC is currently holding an item.
    /// </summary>
    public bool IsHoldingItem()
    {
        return _heldItem != null;
    }

    /// <summary>
    /// Returns the current state of the NPC.
    /// </summary>
    public string GetCurrentState()
    {
        return _currentState.ToString();
    }

    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;

        // Draw detection radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Draw reach distance
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, reachDistance);

        // Draw line to current target
        if (_currentTargetObject != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _currentTargetObject.transform.position);
        }

        // Draw line to counter
        if (counterSpawn != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, counterSpawn.position);
            Gizmos.DrawWireCube(counterSpawn.position, Vector3.one * 0.3f);
        }
    }
}
