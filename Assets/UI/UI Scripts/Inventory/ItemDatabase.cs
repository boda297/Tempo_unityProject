using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Items/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public ItemData[] items;

    private Dictionary<int, ItemData> lookup;
    private static ItemDatabase instance;

    public static ItemDatabase Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<ItemDatabase>("ItemDatabase");
            return instance;
        }
    }

    public ItemData Get(int id)
    {
        if (lookup == null)
        {
            lookup = new Dictionary<int, ItemData>();
            foreach (var i in items)
                if (i != null) lookup[i.id] = i;
        }
        return lookup.TryGetValue(id, out var data) ? data : null;
    }
}