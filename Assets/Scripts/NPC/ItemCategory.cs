using UnityEngine;

/// <summary>
/// ScriptableObject representing an item category (e.g., Food, Drink, Cleaning).
/// Used by NPCs to filter which items they're interested in picking up.
/// To create a new category: Right-click in Project window → Create → NPC → Item Category
/// </summary>
[CreateAssetMenu(fileName = "NewItemCategory", menuName = "NPC/Item Category")]
public class ItemCategory : ScriptableObject
{
    [Tooltip("Optional description for editor reference.")]
    public string description;
}
