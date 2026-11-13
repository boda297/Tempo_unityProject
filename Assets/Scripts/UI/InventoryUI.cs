using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private Transform slotGrid;
    [SerializeField] private GameObject slotPrefab;

    [Header("Settings")]
    [SerializeField] private int slotCount = 18;
    [SerializeField] private Key toggleKey = Key.Tab;
    [SerializeField] private bool startOpen = true; // set to false when you're done styling

    private bool isOpen;

    private void Start() => SetOpen(startOpen);

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            SetOpen(!isOpen);
    }

    public static bool IsOpen { get; private set; }
    private CinemachineInputAxisController lookController;

    public void SetOpen(bool open)
    {
        isOpen = open;
        IsOpen = open;
        panel.alpha = open ? 1f : 0f;
        panel.interactable = open;
        panel.blocksRaycasts = open;

        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;

        if (lookController == null)
            lookController = FindFirstObjectByType<CinemachineInputAxisController>();
        if (lookController != null)
            lookController.enabled = !open;      // mouse no longer rotates the camera over the UI
    }

    private void OnDestroy()
    {
        IsOpen = false;
        if (lookController != null) lookController.enabled = true;
    }

#if UNITY_EDITOR
    [ContextMenu("Generate Slots")]
    private void GenerateSlots()
    {
        for (int i = slotGrid.childCount - 1; i >= 0; i--)
            UnityEditor.Undo.DestroyObjectImmediate(slotGrid.GetChild(i).gameObject);

        for (int i = 0; i < slotCount; i++)
        {
            var slot = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(slotPrefab, slotGrid);
            slot.name = $"Slot_{i:00}";
            UnityEditor.Undo.RegisterCreatedObjectUndo(slot, "Generate Slots");
        }
    }
#endif
}