using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InventoryView : MonoBehaviour
{
    public static InventoryView Instance;

    [SerializeField] private Transform hotbarRoot;   // "Hotbar"
    [SerializeField] private Transform gridRoot;     // "SlotGrid"
    [SerializeField] private Button dropButton;
    [SerializeField] private Button trashButton;
    [SerializeField] private Image dragGhost;   // an Image at the bottom of the Canvas

    private InventorySlotUI[] slotUIs;
    private PlayerInventory inventory;

    private void Awake()
    {
        Instance = this;

        var list = new List<InventorySlotUI>();
        list.AddRange(hotbarRoot.GetComponentsInChildren<InventorySlotUI>(true));
        list.AddRange(gridRoot.GetComponentsInChildren<InventorySlotUI>(true));
        slotUIs = list.ToArray();

        if (slotUIs.Length != PlayerInventory.TotalSlots)
            Debug.LogWarning($"[InventoryView] Found {slotUIs.Length} slots, expected {PlayerInventory.TotalSlots}.");
    }

    public void Bind(PlayerInventory inv)
    {
        Unbind(inventory);
        inventory = inv;
        inventory.OnChanged += Refresh;

        for (int i = 0; i < slotUIs.Length && i < PlayerInventory.TotalSlots; i++)
            slotUIs[i].Init(inv, i);

        dropButton.onClick.AddListener(inventory.DropSelected);
        trashButton.onClick.AddListener(inventory.TrashSelected);
        Refresh();
    }

    public void Unbind(PlayerInventory inv)
    {
        if (inv == null || inv != inventory) return;
        inventory.OnChanged -= Refresh;
        dropButton.onClick.RemoveListener(inventory.DropSelected);
        trashButton.onClick.RemoveListener(inventory.TrashSelected);
        inventory = null;
    }

    private void Refresh()
    {
        for (int i = 0; i < slotUIs.Length && i < PlayerInventory.TotalSlots; i++)
            slotUIs[i].Refresh(inventory.Slots[i], inventory.SelectedIndex == i);
    }


    public void ShowGhost(Sprite sprite)
    {
        dragGhost.sprite = sprite;
        dragGhost.gameObject.SetActive(true);
    }

    public void MoveGhost(Vector2 screenPos) => dragGhost.transform.position = screenPos;
    public void HideGhost() => dragGhost.gameObject.SetActive(false);
}