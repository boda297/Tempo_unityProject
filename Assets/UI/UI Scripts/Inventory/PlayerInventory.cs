using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInventory : NetworkBehaviour
{
    public const int HotbarSize = 4;
    public const int GridSize = 18;
    public const int TotalSlots = HotbarSize + GridSize;

    [Serializable]
    public struct ItemStack
    {
        public int itemId;
        public int count;
        public bool IsEmpty => itemId == 0 || count <= 0;
    }

    [Header("Pickup")]
    [SerializeField] private float pickupRange = 2.5f;
    [SerializeField] private LayerMask itemMask;      // set to your "Item" layer

    [Header("Drop")]
    [SerializeField] private float dropDistance = 1.2f;
    [SerializeField] private float dropForce = 3f;

    public ItemStack[] Slots { get; } = new ItemStack[TotalSlots];
    public int SelectedIndex { get; private set; } = -1;
    public event Action OnChanged;

    private InputSystem_Actions controls;

    // ───────────── Lifecycle ─────────────
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        controls = new InputSystem_Actions();
        controls.Player.Enable();
        controls.Player.Interact.performed += OnInteract;

        if (InventoryView.Instance != null)
            InventoryView.Instance.Bind(this);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner || controls == null) return;

        controls.Player.Interact.performed -= OnInteract;
        controls.Player.Disable();
        controls.Dispose();
        controls = null;

        if (InventoryView.Instance != null)
            InventoryView.Instance.Unbind(this);
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.gKey.wasPressedThisFrame)
                DropSelected();

            // ── TEMP DEBUG ──
            if (Keyboard.current.hKey.wasPressedThisFrame) DebugDropFirst();
            if (Keyboard.current.jKey.wasPressedThisFrame) DumpInventory();
        }
    }

    private void DebugDropFirst()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i].IsEmpty) continue;
            SelectedIndex = i;
            Debug.Log($"[Debug] Dropping slot {i}: id {Slots[i].itemId} x{Slots[i].count}");
            DropSelected();
            return;
        }
        Debug.Log("[Debug] Inventory is empty");
    }

    private void DumpInventory()
    {
        for (int i = 0; i < Slots.Length; i++)
            if (!Slots[i].IsEmpty)
                Debug.Log($"Slot {i}: id {Slots[i].itemId} x{Slots[i].count}");
    }

    // ───────────── Selection / UI actions ─────────────
    public void Select(int index)
    {
        SelectedIndex = (SelectedIndex == index) ? -1 : index;
        OnChanged?.Invoke();
    }

    public void DropSelected()
    {
        if (SelectedIndex < 0) return;
        var stack = Slots[SelectedIndex];
        if (stack.IsEmpty) return;

        Slots[SelectedIndex] = default;
        OnChanged?.Invoke();
        DropServerRpc(stack.itemId, stack.count);
    }

    public void TrashSelected()
    {
        if (SelectedIndex < 0 || Slots[SelectedIndex].IsEmpty) return;
        Slots[SelectedIndex] = default;
        OnChanged?.Invoke();
    }

    // ───────────── Pickup ─────────────
    private void OnInteract(InputAction.CallbackContext ctx)
    {
        var hits = Physics.OverlapSphere(transform.position, pickupRange, itemMask, QueryTriggerInteraction.Collide);

        WorldItem best = null;
        float bestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            var item = hit.GetComponentInParent<WorldItem>();
            if (item == null || !item.IsSpawned) continue;
            if (!HasRoomFor(item.ItemId, item.Count)) continue;

            float d = (item.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = item; }
        }

        if (best != null)
            PickupServerRpc(best.NetworkObjectId);
    }

    [ServerRpc]
    private void PickupServerRpc(ulong itemNetworkId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemNetworkId, out var no)) return; // already taken
        var item = no.GetComponent<WorldItem>();
        if (item == null) return;

        // anti-cheat: make sure the player is actually close
        if (Vector3.Distance(transform.position, no.transform.position) > pickupRange + 1.5f) return;

        int id = item.ItemId;
        int amount = item.Count;

        // destroy only runtime-spawned items; just despawn + hide scene-placed ones
        bool isSceneItem = no.IsSceneObject ?? false;
        no.Despawn(!isSceneItem);
        if (isSceneItem) no.gameObject.SetActive(false);

        GrantItemClientRpc(id, amount, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });
    }

    [ClientRpc]
    private void GrantItemClientRpc(int id, int amount, ClientRpcParams rpcParams = default)
    {
        int leftover = Add(id, amount);
        if (leftover > 0)                       // rare race: inventory filled up meanwhile
            DropServerRpc(id, leftover);
    }

    // ───────────── Drop ─────────────
    [ServerRpc]
    private void DropServerRpc(int id, int amount)
    {
        var data = ItemDatabase.Instance.Get(id);
        if (data == null || data.worldPrefab == null || amount <= 0) return;

        Vector3 pos = transform.position + Vector3.up * 1f + transform.forward * dropDistance;
        var go = Instantiate(data.worldPrefab, pos, Quaternion.identity);

        go.GetComponent<WorldItem>().Init(id, amount);
        go.GetComponent<NetworkObject>().Spawn();

        if (go.TryGetComponent(out Rigidbody rb))
            rb.AddForce(transform.forward * dropForce, ForceMode.Impulse);
    }

    // ───────────── Inventory data ─────────────
    private bool HasRoomFor(int id, int amount)
    {
        var data = ItemDatabase.Instance.Get(id);
        if (data == null) return false;

        int room = 0;
        foreach (var s in Slots)
        {
            if (s.IsEmpty) room += data.maxStack;
            else if (s.itemId == id) room += data.maxStack - s.count;
        }
        return room >= amount;
    }

    /// Returns how many items did NOT fit.
    private int Add(int id, int amount)
    {
        var data = ItemDatabase.Instance.Get(id);
        if (data == null) return amount;

        // 1) top up existing stacks
        for (int i = 0; i < Slots.Length && amount > 0; i++)
        {
            if (Slots[i].IsEmpty || Slots[i].itemId != id) continue;
            int moved = Mathf.Min(data.maxStack - Slots[i].count, amount);
            Slots[i].count += moved;
            amount -= moved;
        }
        // 2) fill empty slots (hotbar first, then grid)
        for (int i = 0; i < Slots.Length && amount > 0; i++)
        {
            if (!Slots[i].IsEmpty) continue;
            int moved = Mathf.Min(data.maxStack, amount);
            Slots[i] = new ItemStack { itemId = id, count = moved };
            amount -= moved;
        }

        OnChanged?.Invoke();
        return amount;
    }

    public void MoveStack(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= TotalSlots || to >= TotalSlots) return;

        var a = Slots[from];
        var b = Slots[to];
        if (a.IsEmpty) return;

        // same item: merge into the target stack first
        if (!b.IsEmpty && b.itemId == a.itemId)
        {
            var data = ItemDatabase.Instance.Get(a.itemId);
            int moved = Mathf.Min(data.maxStack - b.count, a.count);
            if (moved > 0)
            {
                Slots[to].count += moved;
                Slots[from].count -= moved;
                if (Slots[from].count <= 0) Slots[from] = default;
                OnChanged?.Invoke();
                return;
            }
        }

        // otherwise swap (works for empty targets too)
        Slots[from] = b;
        Slots[to] = a;

        if (SelectedIndex == from) SelectedIndex = to;
        else if (SelectedIndex == to) SelectedIndex = from;

        OnChanged?.Invoke();
    }
}