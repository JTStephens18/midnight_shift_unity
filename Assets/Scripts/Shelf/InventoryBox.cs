using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a single inventory entry with category and quantity.
/// </summary>
[Serializable]
public class InventoryEntry
{
    [Tooltip("The category of items in this stack.")]
    public ItemCategory category;

    [Tooltip("The prefab to spawn when placing this item type.")]
    public GameObject itemPrefab;

    [Tooltip("Current quantity in stock.")]
    [Min(0)]
    public int quantity;
}

/// <summary>
/// Portable container that holds item stock for shelf restocking.
/// Attach to a pickupable object (requires Rigidbody).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class InventoryBox : MonoBehaviour
{
    [Header("Inventory")]
    [Tooltip("Items available in this box for placement.")]
    [SerializeField] private List<InventoryEntry> inventory = new List<InventoryEntry>();

    [Header("Debug")]
    [SerializeField] private bool logOperations = false;

    /// <summary>
    /// Checks if this box has stock for the given category.
    /// </summary>
    public bool HasStock(ItemCategory category)
    {
        if (category == null) return false;

        foreach (InventoryEntry entry in inventory)
        {
            if (entry.category == category && entry.quantity > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the quantity of items for a given category.
    /// </summary>
    public int GetQuantity(ItemCategory category)
    {
        if (category == null) return 0;

        foreach (InventoryEntry entry in inventory)
        {
            if (entry.category == category)
                return entry.quantity;
        }
        return 0;
    }

    /// <summary>
    /// Gets the prefab for spawning items of the given category.
    /// </summary>
    public GameObject GetItemPrefab(ItemCategory category)
    {
        if (category == null) return null;

        foreach (InventoryEntry entry in inventory)
        {
            if (entry.category == category)
                return entry.itemPrefab;
        }
        return null;
    }

    /// <summary>
    /// Attempts to decrement the stock for a category. Returns true if successful.
    /// </summary>
    public bool TryDecrement(ItemCategory category)
    {
        if (category == null) return false;

        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].category == category && inventory[i].quantity > 0)
            {
                inventory[i].quantity--;

                if (logOperations)
                    Debug.Log($"[InventoryBox] Decremented {category.name}. Remaining: {inventory[i].quantity}");

                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Adds stock for a category. Creates new entry if category doesn't exist.
    /// </summary>
    public void AddStock(ItemCategory category, int amount, GameObject prefab = null)
    {
        if (category == null || amount <= 0) return;

        foreach (InventoryEntry entry in inventory)
        {
            if (entry.category == category)
            {
                entry.quantity += amount;
                if (prefab != null) entry.itemPrefab = prefab;

                if (logOperations)
                    Debug.Log($"[InventoryBox] Added {amount} {category.name}. Total: {entry.quantity}");
                return;
            }
        }

        // Category not found - create new entry
        inventory.Add(new InventoryEntry
        {
            category = category,
            itemPrefab = prefab,
            quantity = amount
        });

        if (logOperations)
            Debug.Log($"[InventoryBox] Created new entry for {category.name} with {amount} items.");
    }

    /// <summary>
    /// Returns all categories that currently have stock.
    /// </summary>
    public List<ItemCategory> GetAvailableCategories()
    {
        List<ItemCategory> available = new List<ItemCategory>();
        foreach (InventoryEntry entry in inventory)
        {
            if (entry.category != null && entry.quantity > 0)
                available.Add(entry.category);
        }
        return available;
    }

    /// <summary>
    /// Returns true if the box has any items at all.
    /// </summary>
    public bool HasAnyStock()
    {
        foreach (InventoryEntry entry in inventory)
        {
            if (entry.quantity > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the total number of items across all categories.
    /// </summary>
    public int GetTotalItemCount()
    {
        int total = 0;
        foreach (InventoryEntry entry in inventory)
        {
            total += entry.quantity;
        }
        return total;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure no negative quantities
        foreach (InventoryEntry entry in inventory)
        {
            if (entry.quantity < 0)
                entry.quantity = 0;
        }
    }
#endif
}
