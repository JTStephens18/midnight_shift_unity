using System.Collections.Generic;
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

    [Header("Item Preferences")]
    [Tooltip("Categories of items this NPC will pick up. Leave empty to pick up any item.")]
    [SerializeField] private List<ItemCategory> wantedCategories = new List<ItemCategory>();

    [Header("Behavior Settings")]
    [Tooltip("Automatically scan for items at regular intervals.")]
    [SerializeField] private bool autoScan = true;

    [Tooltip("Time between automatic scans in seconds.")]
    [SerializeField] private float scanInterval = 1f;

    [Tooltip("Time to pause at item before picking it up.")]
    [SerializeField] private float pickupPauseTime = 0.5f;

    [Header("Batch Collection")]
    [Tooltip("If true, collect multiple items before going to counter. If false, deliver each item individually.")]
    [SerializeField] private bool batchCollection = false;

    [Tooltip("Number of items to collect before going to counter (when batch collection is enabled).")]
    [SerializeField] private int batchSize = 4;

    [Tooltip("Enable/disable item collection. Set to false to stop the NPC from collecting items.")]
    [SerializeField] private bool isCollecting = true;

    [Header("Exit Settings")]
    [Tooltip("The exit point where the NPC goes after checkout.")]
    [SerializeField] private Transform exitPoint;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    private NavMeshAgent _agent;
    private IInteractable _currentTarget;
    private GameObject _currentTargetObject;
    private List<InteractableItem> _heldItems = new List<InteractableItem>();
    private Dictionary<GameObject, float> _unreachableItems = new Dictionary<GameObject, float>(); // Items we couldn't path to
    private float _scanTimer;
    private float _pauseTimer;
    private bool _hasStartedMoving;
    private bool _hasCheckedOut = false; // Set true when player triggers checkout
    private const float UNREACHABLE_RETRY_TIME = 10f; // Seconds before retrying unreachable items

    // States for the interaction flow
    private enum NPCState { Idle, MovingToItem, WaitingAtItem, PickingUp, MovingToCounter, PlacingItem, MovingToExit }
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

            case NPCState.WaitingAtItem:
                HandleWaitingAtItemState();
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

            case NPCState.MovingToExit:
                HandleMovingToExitState();
                break;
        }
    }

    /// <summary>
    /// Idle state: scan for items periodically if autoScan is enabled.
    /// If checkout is triggered, navigate to exit.
    /// </summary>
    private void HandleIdleState()
    {
        // If checkout was triggered, head to exit
        if (_hasCheckedOut)
        {
            if (exitPoint != null)
            {
                Debug.Log($"[NPC] Checkout complete! Heading to exit at {exitPoint.position}");
                _agent.SetDestination(exitPoint.position);
                _currentState = NPCState.MovingToExit;
            }
            else
            {
                Debug.LogWarning("[NPC] Checkout triggered but no exit point assigned! Despawning immediately.");
                Destroy(gameObject);
            }
            return;
        }

        if (!autoScan || !isCollecting) return;

        _scanTimer += Time.deltaTime;
        if (_scanTimer >= scanInterval)
        {
            _scanTimer = 0f;
            ScanForItems();
        }
    }

    /// <summary>
    /// Moving to exit state: check if NPC has reached the exit and despawn.
    /// </summary>
    private void HandleMovingToExitState()
    {
        if (_agent.pathPending) return;

        // Mark that we've started moving
        if (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance)
        {
            _hasStartedMoving = true;
        }

        if (_hasStartedMoving && _agent.remainingDistance <= _agent.stoppingDistance && !_agent.pathPending)
        {
            Debug.Log("[NPC] Reached exit. Goodbye!");
            _hasStartedMoving = false;
            Destroy(gameObject);
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
            _pauseTimer = 0f;
            _currentState = NPCState.WaitingAtItem;
        }
    }

    /// <summary>
    /// Waiting at item state: pause before picking up.
    /// </summary>
    private void HandleWaitingAtItemState()
    {
        _pauseTimer += Time.deltaTime;

        if (_pauseTimer >= pickupPauseTime)
        {
            _currentState = NPCState.PickingUp;
        }
    }

    /// <summary>
    /// Pickup state: execute the pickup and decide whether to continue collecting or go to counter.
    /// </summary>
    private void HandlePickupState()
    {
        InteractableItem pickedItem = null;

        // Cache the held item - check both on object and in parents
        if (_currentTargetObject != null)
        {
            pickedItem = _currentTargetObject.GetComponent<InteractableItem>();
            if (pickedItem == null)
            {
                pickedItem = _currentTargetObject.GetComponentInParent<InteractableItem>();
            }
        }

        if (_currentTarget != null && handBone != null && pickedItem != null)
        {
            Debug.Log($"[NPC] Picking up: {pickedItem.gameObject.name}");
            _currentTarget.OnPickedUp(handBone);
            _heldItems.Add(pickedItem);

            // Disable all renderers on the held item to hide it
            Renderer[] renderers = pickedItem.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                r.enabled = false;
                Debug.Log($"[NPC] Disabled renderer: {r.gameObject.name}");
            }
        }
        else
        {
            Debug.LogWarning($"[NPC] Pickup failed - Target: {_currentTarget != null}, HandBone: {handBone != null}, PickedItem: {pickedItem != null}");
        }

        // Clear pickup target
        _currentTarget = null;
        _currentTargetObject = null;

        // Decide next action based on batch collection setting
        bool shouldGoToCounter = false;

        if (!batchCollection)
        {
            // Individual mode: go to counter after each item
            shouldGoToCounter = _heldItems.Count > 0;
        }
        else
        {
            // Batch mode: go to counter when batch is full OR if collecting is disabled
            shouldGoToCounter = _heldItems.Count >= batchSize || (!isCollecting && _heldItems.Count > 0);
        }

        if (shouldGoToCounter && counterSpawn != null)
        {
            Debug.Log($"[NPC] Moving to counter with {_heldItems.Count} item(s) at {counterSpawn.position}");
            _agent.SetDestination(counterSpawn.position);
            _currentState = NPCState.MovingToCounter;
        }
        else if (batchCollection && _heldItems.Count < batchSize && isCollecting)
        {
            // Continue collecting more items
            Debug.Log($"[NPC] Collected {_heldItems.Count}/{batchSize} items, looking for more...");
            _currentState = NPCState.Idle;
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
    /// Placing state: place all held items on the counter and return to idle.
    /// </summary>
    private void HandlePlacingState()
    {
        if (_heldItems.Count > 0 && counterSpawn != null)
        {
            // Place all items with slight offset to avoid stacking
            float offsetStep = 0.3f;
            for (int i = 0; i < _heldItems.Count; i++)
            {
                InteractableItem item = _heldItems[i];
                if (item != null)
                {
                    Debug.Log($"[NPC] Placing {item.gameObject.name} at counter");

                    // Re-enable all renderers to show the item again
                    Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
                    foreach (Renderer r in renderers)
                    {
                        r.enabled = true;
                    }

                    // Offset each item slightly so they don't stack exactly
                    Vector3 placePosition = counterSpawn.position + new Vector3(i * offsetStep, 0, 0);
                    item.PlaceAt(placePosition);
                }
            }

            Debug.Log($"[NPC] Placed {_heldItems.Count} item(s) at counter");
        }
        else
        {
            Debug.LogWarning($"[NPC] Place failed - HeldItems: {_heldItems.Count}, CounterSpawn: {counterSpawn != null}");
        }

        _heldItems.Clear();
        _currentState = NPCState.Idle;
    }

    /// <summary>
    /// Scans for nearby interactable items and selects the nearest one.
    /// </summary>
    public void ScanForItems()
    {
        // Don't scan if not collecting or already holding max items in batch mode
        if (!isCollecting) return;
        if (batchCollection && _heldItems.Count >= batchSize) return;
        if (!batchCollection && _heldItems.Count > 0) return;

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
                // Get the InteractableItem to check its category
                InteractableItem item = col.GetComponent<InteractableItem>();
                if (item == null)
                {
                    item = col.GetComponentInParent<InteractableItem>();
                }

                // Skip items that have already been delivered
                if (item != null && item.IsDelivered)
                {
                    continue;
                }

                // Skip items we couldn't path to recently (clear expired entries)
                if (_unreachableItems.ContainsKey(col.gameObject))
                {
                    if (Time.time - _unreachableItems[col.gameObject] < UNREACHABLE_RETRY_TIME)
                    {
                        continue; // Still in timeout
                    }
                    else
                    {
                        _unreachableItems.Remove(col.gameObject); // Timeout expired, try again
                    }
                }

                // Check category filter (empty list = accept all)
                if (item != null && wantedCategories.Count > 0)
                {
                    if (item.ItemCategory == null || !wantedCategories.Contains(item.ItemCategory))
                    {
                        continue; // Skip items not in our wanted list
                    }
                }

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

        // Try to find a valid NavMesh position near the item
        // This allows items on shelves to be reachable by walking near them
        NavMeshHit navHit;
        Vector3 destinationPosition = targetPosition;

        // First check if the exact position is on NavMesh
        if (!NavMesh.SamplePosition(targetPosition, out navHit, 0.5f, NavMesh.AllAreas))
        {
            // Item isn't on NavMesh, find nearest walkable point (within reachDistance)
            if (NavMesh.SamplePosition(targetPosition, out navHit, reachDistance + 1f, NavMesh.AllAreas))
            {
                destinationPosition = navHit.position;
                Debug.Log($"[NPC] Item {targetObject.name} not on NavMesh, navigating to nearby point.");
            }
            else
            {
                // No NavMesh point found nearby at all
                _unreachableItems[targetObject] = Time.time;
                Debug.LogWarning($"[NPC] Cannot find NavMesh near {targetObject.name}. Will retry in {UNREACHABLE_RETRY_TIME}s.");
                CancelCurrentAction();
                return;
            }
        }

        // Check if we can path to the destination
        NavMeshPath path = new NavMeshPath();
        bool pathValid = _agent.CalculatePath(destinationPosition, path);

        if (!pathValid || path.status != NavMeshPathStatus.PathComplete)
        {
            // Mark as unreachable so we don't spam this error
            _unreachableItems[targetObject] = Time.time;
            Debug.LogWarning($"[NPC] Cannot path to {targetObject.name} (NavMesh status: {path.status}). Will retry in {UNREACHABLE_RETRY_TIME}s.");
            CancelCurrentAction();
            return;
        }

        _agent.SetDestination(destinationPosition);
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
        return _heldItems.Count > 0;
    }

    /// <summary>
    /// Returns the number of items currently held.
    /// </summary>
    public int GetHeldItemCount()
    {
        return _heldItems.Count;
    }

    /// <summary>
    /// Sets whether the NPC should collect items.
    /// </summary>
    public void SetCollecting(bool collecting)
    {
        isCollecting = collecting;
        Debug.Log($"[NPC] Collecting set to: {collecting}");

        // If we're stopping collection and have items, go deliver them
        if (!collecting && _heldItems.Count > 0 && _currentState == NPCState.Idle)
        {
            if (counterSpawn != null)
            {
                Debug.Log($"[NPC] Stopping collection, delivering {_heldItems.Count} item(s) to counter");
                _agent.SetDestination(counterSpawn.position);
                _currentState = NPCState.MovingToCounter;
            }
        }
    }

    /// <summary>
    /// Returns whether the NPC is currently collecting items.
    /// </summary>
    public bool IsCollecting()
    {
        return isCollecting;
    }

    /// <summary>
    /// Triggers checkout - NPC will stop collecting and head to exit after placing items.
    /// Call this when the player completes the transaction.
    /// </summary>
    public void TriggerCheckout()
    {
        _hasCheckedOut = true;
        isCollecting = false;
        Debug.Log("[NPC] Checkout triggered! Will head to exit when ready.");

        // If idle with no items, immediately head to exit
        if (_currentState == NPCState.Idle && _heldItems.Count == 0)
        {
            if (exitPoint != null)
            {
                Debug.Log($"[NPC] Heading to exit immediately at {exitPoint.position}");
                _agent.SetDestination(exitPoint.position);
                _currentState = NPCState.MovingToExit;
            }
        }
        // If holding items, deliver them first (handled by existing logic)
        else if (_heldItems.Count > 0 && _currentState == NPCState.Idle)
        {
            if (counterSpawn != null)
            {
                Debug.Log($"[NPC] Delivering remaining {_heldItems.Count} item(s) before checkout");
                _agent.SetDestination(counterSpawn.position);
                _currentState = NPCState.MovingToCounter;
            }
        }
    }

    /// <summary>
    /// Returns whether checkout has been triggered.
    /// </summary>
    public bool HasCheckedOut()
    {
        return _hasCheckedOut;
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

        // Draw line to exit
        if (exitPoint != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, exitPoint.position);
            Gizmos.DrawWireSphere(exitPoint.position, 0.5f);
        }
    }
}
