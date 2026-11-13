using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image highlight;

    private PlayerInventory inventory;
    private int index;
    private bool dragging;

    public void Init(PlayerInventory inv, int slotIndex)
    {
        inventory = inv;
        index = slotIndex;
    }

    public void Refresh(PlayerInventory.ItemStack stack, bool selected)
    {
        bool has = !stack.IsEmpty;
        var data = has ? ItemDatabase.Instance.Get(stack.itemId) : null;

        if (has)
        {
            if (data == null)
                Debug.LogWarning($"Slot {index}: no ItemData for id {stack.itemId}", this);
            else if (data.icon == null)
                Debug.LogWarning($"Slot {index}: ItemData '{data.name}' has no Icon sprite", this);
        }

        icon.enabled = data != null && data.icon != null;
        icon.sprite = data != null ? data.icon : null;
        icon.color = Color.white;
        icon.preserveAspect = true;

        bool showCount = has && stack.count > 1;
        countText.gameObject.SetActive(showCount);          // the Count child is inactive in the prefab
        countText.text = showCount ? stack.count.ToString() : "";

        if (highlight != null) highlight.enabled = selected;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || inventory == null) return;
        inventory.Select(index);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        dragging = false;
        if (e.button != PointerEventData.InputButton.Left || inventory == null) return;
        if (inventory.Slots[index].IsEmpty || icon.sprite == null) return;   // nothing to drag

        dragging = true;
        icon.color = new Color(1f, 1f, 1f, 0.35f);
        if (InventoryView.Instance != null)
        {
            InventoryView.Instance.ShowGhost(icon.sprite);
            InventoryView.Instance.MoveGhost(e.position);
        }
    }

    public void OnDrag(PointerEventData e)
    {
        if (dragging && InventoryView.Instance != null)
            InventoryView.Instance.MoveGhost(e.position);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        EndDragVisuals();
    }

    public void OnDrop(PointerEventData e)
    {
        if (inventory == null || e.pointerDrag == null) return;

        var src = e.pointerDrag.GetComponent<InventorySlotUI>();
        if (src == null || src == this || !src.dragging || src.inventory != inventory) return;

        inventory.MoveStack(src.index, index);   // merges, swaps or moves into an empty slot
    }

    private void EndDragVisuals()
    {
        dragging = false;
        icon.color = Color.white;
        if (InventoryView.Instance != null) InventoryView.Instance.HideGhost();
    }

    private void OnDisable()
    {
        if (dragging) EndDragVisuals();
    }
}