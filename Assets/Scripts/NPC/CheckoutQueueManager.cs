using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton manager that handles the checkout queue for NPCs.
/// NPCs enqueue when their batch is full, and only the front NPC can place items.
/// </summary>
public class CheckoutQueueManager : MonoBehaviour
{
    public static CheckoutQueueManager Instance { get; private set; }

    [Header("Queue Settings")]
    [Tooltip("Position where NPCs wait in line (first position). Additional positions calculated automatically.")]
    [SerializeField] private Transform queueStartPosition;

    [Tooltip("Spacing between NPCs in the queue.")]
    [SerializeField] private float queueSpacing = 1.5f;

    [Tooltip("Direction the queue extends (local forward of queueStartPosition).")]
    [SerializeField] private bool queueExtendsBackward = true;

    [Header("References")]
    [Tooltip("All counter slots in the scene. Used to check if counter is clear.")]
    [SerializeField] private List<CounterSlot> counterSlots = new List<CounterSlot>();

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private Queue<NPCInteractionController> _checkoutQueue = new Queue<NPCInteractionController>();
    private NPCInteractionController _currentlyAtCounter = null;

    /// <summary>
    /// The NPC currently placing items / waiting for checkout at the counter.
    /// </summary>
    public NPCInteractionController CurrentlyAtCounter => _currentlyAtCounter;

    /// <summary>
    /// Number of NPCs waiting in the queue (not including the one at counter).
    /// </summary>
    public int QueueLength => _checkoutQueue.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CheckoutQueueManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Adds an NPC to the checkout queue. Called when NPC has collected their batch.
    /// </summary>
    public void EnqueueForCheckout(NPCInteractionController npc)
    {
        if (_checkoutQueue.Contains(npc) || _currentlyAtCounter == npc)
        {
            DebugLog($"[CheckoutQueueManager] NPC {npc.name} already in queue or at counter.");
            return;
        }

        _checkoutQueue.Enqueue(npc);
        DebugLog($"[CheckoutQueueManager] NPC {npc.name} added to queue. Queue length: {_checkoutQueue.Count}");

        // If no one is at counter and counter is clear, let this NPC go
        TryAdvanceQueue();
    }

    /// <summary>
    /// Removes an NPC from the queue (e.g., if they leave or are destroyed).
    /// </summary>
    public void RemoveFromQueue(NPCInteractionController npc)
    {
        if (_currentlyAtCounter == npc)
        {
            _currentlyAtCounter = null;
            DebugLog($"[CheckoutQueueManager] NPC {npc.name} removed from counter position.");
            TryAdvanceQueue();
            return;
        }

        // Rebuild queue without this NPC
        Queue<NPCInteractionController> newQueue = new Queue<NPCInteractionController>();
        while (_checkoutQueue.Count > 0)
        {
            NPCInteractionController queued = _checkoutQueue.Dequeue();
            if (queued != npc && queued != null)
            {
                newQueue.Enqueue(queued);
            }
        }
        _checkoutQueue = newQueue;
        DebugLog($"[CheckoutQueueManager] NPC {npc.name} removed from queue.");
    }

    /// <summary>
    /// Called when the current NPC at counter has been checked out.
    /// Clears the counter position and advances the queue.
    /// </summary>
    public void OnCheckoutComplete(NPCInteractionController npc)
    {
        if (_currentlyAtCounter == npc)
        {
            DebugLog($"[CheckoutQueueManager] NPC {npc.name} checkout complete. Advancing queue.");
            _currentlyAtCounter = null;
            TryAdvanceQueue();
        }
    }

    /// <summary>
    /// Checks if it's this NPC's turn to proceed to counter.
    /// </summary>
    public bool IsMyTurn(NPCInteractionController npc)
    {
        return _currentlyAtCounter == npc;
    }

    /// <summary>
    /// Gets the queue position for an NPC (0 = front of queue, -1 = at counter, -2 = not in queue).
    /// </summary>
    public int GetQueuePosition(NPCInteractionController npc)
    {
        if (_currentlyAtCounter == npc) return -1;

        int position = 0;
        foreach (var queued in _checkoutQueue)
        {
            if (queued == npc) return position;
            position++;
        }
        return -2; // Not in queue
    }

    /// <summary>
    /// Gets the world position where an NPC should stand based on their queue position.
    /// </summary>
    public Vector3 GetQueueWaitPosition(int queuePosition)
    {
        if (queueStartPosition == null)
        {
            Debug.LogWarning("[CheckoutQueueManager] No queue start position assigned!");
            return Vector3.zero;
        }

        Vector3 direction = queueExtendsBackward ? -queueStartPosition.forward : queueStartPosition.forward;
        return queueStartPosition.position + direction * (queueSpacing * queuePosition);
    }

    /// <summary>
    /// Checks if all counter slots are empty.
    /// </summary>
    public bool IsCounterClear()
    {
        foreach (var slot in counterSlots)
        {
            if (slot != null && slot.HasItems)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Checks if any NPC is currently at the counter (placed items or waiting for checkout).
    /// </summary>
    public bool IsCounterOccupied()
    {
        return _currentlyAtCounter != null;
    }

    /// <summary>
    /// Tries to advance the queue - lets the next NPC proceed to counter if conditions are met.
    /// </summary>
    private void TryAdvanceQueue()
    {
        // Can't advance if someone is already at counter
        if (_currentlyAtCounter != null) return;

        // Can't advance if counter isn't clear
        if (!IsCounterClear())
        {
            DebugLog("[CheckoutQueueManager] Counter not clear, waiting...");
            return;
        }

        // Nobody waiting
        if (_checkoutQueue.Count == 0) return;

        // Advance the next NPC
        _currentlyAtCounter = _checkoutQueue.Dequeue();
        DebugLog($"[CheckoutQueueManager] NPC {_currentlyAtCounter.name} is now at counter. Remaining in queue: {_checkoutQueue.Count}");

        // Notify the NPC that it's their turn
        _currentlyAtCounter.OnTurnToPlaceItems();

        // Update queue positions for remaining NPCs
        UpdateQueuePositions();
    }

    /// <summary>
    /// Updates all queued NPCs with their new positions.
    /// </summary>
    private void UpdateQueuePositions()
    {
        int position = 0;
        foreach (var npc in _checkoutQueue)
        {
            if (npc != null)
            {
                npc.UpdateQueuePosition(position);
            }
            position++;
        }
    }

    /// <summary>
    /// Called by external systems (like when player deletes counter items) to check if queue can advance.
    /// </summary>
    public void NotifyCounterStateChanged()
    {
        TryAdvanceQueue();
    }

    private void DebugLog(string message)
    {
        if (showDebugLogs) Debug.Log(message);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (queueStartPosition == null) return;

        // Draw queue positions
        Gizmos.color = Color.yellow;
        for (int i = 0; i < 5; i++)
        {
            Vector3 pos = GetQueueWaitPosition(i);
            Gizmos.DrawWireSphere(pos, 0.3f);
            UnityEditor.Handles.Label(pos + Vector3.up * 0.5f, $"Queue {i}");
        }
    }
#endif
}
