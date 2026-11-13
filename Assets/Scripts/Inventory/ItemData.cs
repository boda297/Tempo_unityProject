using UnityEngine;

[CreateAssetMenu(menuName = "Items/Item Data")]
public class ItemData : ScriptableObject
{
    public int id = 1;               
    public string displayName;
    public Sprite icon;
    public int maxStack = 1;
    public GameObject worldPrefab;   // the prefab spawned when dropped
}