using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class InventoryView : MonoBehaviour
{
    public static InventoryView Instance;

    [SerializeField] private Transform hotbarRoot;
    [SerializeField] private Transform gridRoot;
    [SerializeField] private Button dropButton;
    [SerializeField] private Button trashButton;
    [SerializeField] private Image dragGhost;

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

        if (dragGhost != null) dragGhost.gameObject.SetActive(false);
    }

    private void Start()
    {
        // The host's player spawns in the menu scene, before this view exists. Bind it now.
        var nm = NetworkManager.Singleton;
        if (inventory == null && nm != null && nm.IsListening &&
            nm.LocalClient != null && nm.LocalClient.PlayerObject != null &&
            nm.LocalClient.PlayerObject.TryGetComponent(out PlayerInventory inv))
        {
            Bind(inv);
        }
    }

    private void OnDestroy()
    {
        Unbind(inventory);
        if (Instance == this) Instance = null;
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
        if (dropButton != null) dropButton.onClick.RemoveListener(inventory.DropSelected);
        if (trashButton != null) trashButton.onClick.RemoveListener(inventory.TrashSelected);
        inventory = null;
    }

    private void Refresh()
    {
        if (inventory == null) return;
        for (int i = 0; i < slotUIs.Length && i < PlayerInventory.TotalSlots; i++)
            slotUIs[i].Refresh(inventory.Slots[i], inventory.SelectedIndex == i);
    }

    public void ShowGhost(Sprite sprite)
    {
        if (dragGhost == null || sprite == null) return;
        dragGhost.sprite = sprite;
        dragGhost.gameObject.SetActive(true);
    }

    public void MoveGhost(Vector2 screenPos)
    {
        if (dragGhost != null) dragGhost.transform.position = screenPos;
    }

    public void HideGhost()
    {
        if (dragGhost != null) dragGhost.gameObject.SetActive(false);
    }
}