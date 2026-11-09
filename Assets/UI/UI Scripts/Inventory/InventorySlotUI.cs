using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image highlight;

    private PlayerInventory inventory;
    private int index;

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
        icon.color = Color.white;          // fixes an Icon image whose color alpha is 0
        icon.preserveAspect = true;

        countText.text = (has && stack.count > 1) ? stack.count.ToString() : "";
        if (highlight != null) highlight.enabled = selected;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (inventory != null) inventory.Select(index);
    }
}