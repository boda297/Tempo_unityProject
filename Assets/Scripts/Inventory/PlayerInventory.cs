using System;
using System.IO;
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

    [Serializable] public class SaveSlot { public int itemId; public int count; }
    [Serializable] public class SaveData { public int version = 1; public SaveSlot[] slots; }

    [Header("Pickup")]
    [SerializeField] private float pickupRange = 2.5f;
    [SerializeField] private LayerMask itemMask;      // "Item" layer

    [Header("Drop")]
    [SerializeField] private float dropDistance = 1.2f;
    [SerializeField] private float dropForce = 3f;

    [Header("Save")]
    [Tooltip("Change per instance if you test several clients on one machine.")]
    [SerializeField] private string saveProfile = "default";
    [SerializeField] private float saveInterval = 1f;

    public ItemStack[] Slots { get; } = new ItemStack[TotalSlots];

    /// Always a hotbar slot (0..HotbarSize-1). Grid slots can't be selected.
    public int SelectedIndex { get; private set; } = 0;

    /// The stack in the selected hotbar slot, for your future equip/use system.
    public ItemStack SelectedStack => Slots[SelectedIndex];

    public event Action OnChanged;
    public event Action OnInventoryFull;

    private InputSystem_Actions controls;
    private bool dirty;
    private bool loaded;
    private float lastSaveTime;

    private ClientRpcParams OwnerOnly => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
    };

    // ───────────── Lifecycle ─────────────
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        controls = new InputSystem_Actions();
        controls.Player.Enable();
        controls.Player.Interact.started += OnInteract;      // 'started' works with or without Hold
        controls.Player.Previous.performed += OnPrevious;
        controls.Player.Next.performed += OnNext;

        LoadInventory();
        SelectedIndex = 0;

        if (InventoryView.Instance != null)
            InventoryView.Instance.Bind(this);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner || controls == null) return;

        SaveNow();
        loaded = false;

        controls.Player.Interact.started -= OnInteract;
        controls.Player.Previous.performed -= OnPrevious;
        controls.Player.Next.performed -= OnNext;
        controls.Player.Disable();
        controls.Dispose();
        controls = null;

        if (InventoryView.Instance != null)
            InventoryView.Instance.Unbind(this);
    }

    private void OnApplicationQuit()
    {
        if (IsSpawned && IsOwner) SaveNow();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.gKey.wasPressedThisFrame)
                DropSelected();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Keyboard.current.f8Key.wasPressedThisFrame) DebugDropFirst();
            if (Keyboard.current.f9Key.wasPressedThisFrame) DumpInventory();
#endif
        }

        if (dirty && Time.unscaledTime - lastSaveTime >= saveInterval)
            SaveNow();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void DebugDropFirst()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i].IsEmpty) continue;
            SelectedIndex = i;
            OnChanged?.Invoke();
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
#endif

    // ───────────── Selection / hotbar ─────────────
    public void Select(int index)
    {
        // Only hotbar slots are selectable; clicks on grid slots are ignored.
        if (index < 0 || index >= HotbarSize || index == SelectedIndex) return;
        SelectedIndex = index;
        OnChanged?.Invoke();
    }

    private void OnPrevious(InputAction.CallbackContext ctx) => StepHotbar(-1);
    private void OnNext(InputAction.CallbackContext ctx) => StepHotbar(+1);

    private void StepHotbar(int dir)
    {
        SelectedIndex = (SelectedIndex + dir + HotbarSize) % HotbarSize;   // wraps both ways
        OnChanged?.Invoke();
    }

    // ───────────── Drop / trash (owner side) ─────────────
    public void DropSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= TotalSlots) return;
        var stack = Slots[SelectedIndex];
        if (stack.IsEmpty) return;

        Slots[SelectedIndex] = default;
        MarkDirty();
        OnChanged?.Invoke();
        DropServerRpc(stack.itemId, stack.count, transform.forward);
    }

    public void TrashSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= TotalSlots || Slots[SelectedIndex].IsEmpty) return;
        Slots[SelectedIndex] = default;
        MarkDirty();
        OnChanged?.Invoke();
    }

    // ───────────── Pickup ─────────────
    private void OnInteract(InputAction.CallbackContext ctx)
    {
        var hits = Physics.OverlapSphere(transform.position, pickupRange, itemMask, QueryTriggerInteraction.Collide);

        WorldItem best = null;
        float bestDist = float.MaxValue;
        bool blockedByFull = false;

        foreach (var hit in hits)
        {
            var item = hit.GetComponentInParent<WorldItem>();
            if (item == null || !item.IsSpawned || item.ItemId == 0) continue;

            if (!HasRoomFor(item.ItemId, item.Count)) { blockedByFull = true; continue; }

            float d = (item.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = item; }
        }

        if (best != null)
            PickupServerRpc(best.NetworkObjectId);
        else if (blockedByFull)
        {
            Debug.Log("[Inventory] Inventory full.");
            OnInventoryFull?.Invoke();
        }
    }

    [ServerRpc]
    private void PickupServerRpc(ulong itemNetworkId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(itemNetworkId, out var no)) return; // already taken
        if (!no.TryGetComponent(out WorldItem item)) return;

        // anti-cheat: player must actually be close
        if (Vector3.Distance(transform.position, no.transform.position) > pickupRange + 1.5f) return;

        int id = item.ItemId;
        int amount = item.Count;
        if (id == 0 || amount <= 0 || ItemDatabase.Instance.Get(id) == null) return; // never destroy unknown items

        // Destroy (works for runtime-spawned and scene-placed items alike). Clients receive the despawn.
        no.Despawn(true);

        GrantItemClientRpc(id, amount, OwnerOnly);
    }

    [ClientRpc]
    private void GrantItemClientRpc(int id, int amount, ClientRpcParams rpcParams = default)
    {
        int leftover = Add(id, amount);
        if (leftover > 0)    // rare race: inventory filled up meanwhile
            DropServerRpc(id, leftover, transform.forward);
    }

    // ───────────── Drop (server side) ─────────────
    [ServerRpc]
    private void DropServerRpc(int id, int amount, Vector3 direction)
    {
        if (!TrySpawnDrop(id, amount, direction))
            RefundClientRpc(id, amount, OwnerOnly);    // never lose the item silently
    }

    private bool TrySpawnDrop(int id, int amount, Vector3 direction)
    {
        var data = ItemDatabase.Instance.Get(id);
        if (data == null || data.worldPrefab == null || amount <= 0 || amount > data.maxStack)
            return false;

        if (!data.worldPrefab.TryGetComponent(out WorldItem _) ||
            !data.worldPrefab.TryGetComponent(out NetworkObject _))
        {
            Debug.LogError($"[Inventory] World prefab of '{data.name}' needs WorldItem + NetworkObject.");
            return false;
        }

        direction.y = 0f;
        if (!(direction.sqrMagnitude > 0.0001f)) direction = transform.forward;    // also catches NaN
        direction.Normalize();

        Vector3 pos = transform.position + Vector3.up + direction * dropDistance;
        var go = Instantiate(data.worldPrefab, pos, Quaternion.identity);

        go.GetComponent<WorldItem>().Init(id, amount);
        go.GetComponent<NetworkObject>().Spawn();

        if (go.TryGetComponent(out Rigidbody rb))
            rb.AddForce(direction * dropForce, ForceMode.Impulse);

        return true;
    }

    [ClientRpc]
    private void RefundClientRpc(int id, int amount, ClientRpcParams rpcParams = default)
    {
        int leftover = Add(id, amount);
        if (leftover > 0)
            Debug.LogWarning($"[Inventory] Could not refund {leftover}x item {id}: inventory full.");
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

        for (int i = 0; i < Slots.Length && amount > 0; i++)
        {
            if (Slots[i].IsEmpty || Slots[i].itemId != id) continue;
            int moved = Mathf.Min(data.maxStack - Slots[i].count, amount);
            Slots[i].count += moved;
            amount -= moved;
        }
        for (int i = 0; i < Slots.Length && amount > 0; i++)
        {
            if (!Slots[i].IsEmpty) continue;
            int moved = Mathf.Min(data.maxStack, amount);
            Slots[i] = new ItemStack { itemId = id, count = moved };
            amount -= moved;
        }

        MarkDirty();
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
            if (data != null)
            {
                int moved = Mathf.Min(data.maxStack - b.count, a.count);
                if (moved > 0)
                {
                    Slots[to].count += moved;
                    Slots[from].count -= moved;
                    if (Slots[from].count <= 0) Slots[from] = default;
                    MarkDirty();
                    OnChanged?.Invoke();
                    return;
                }
            }
        }

        // otherwise swap (works for empty targets too)
        Slots[from] = b;
        Slots[to] = a;

        MarkDirty();
        OnChanged?.Invoke();
    }

    // ───────────── Save / Load (local, owner only) ─────────────
    private string SavePath => Path.Combine(Application.persistentDataPath, $"inventory_{saveProfile}.json");

    private void MarkDirty() => dirty = true;

    private void LoadInventory()
    {
        loaded = true;
        Array.Clear(Slots, 0, Slots.Length);

        try
        {
            if (!File.Exists(SavePath)) return;

            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data?.slots == null) return;

            for (int i = 0; i < TotalSlots && i < data.slots.Length; i++)
            {
                var s = data.slots[i];
                if (s == null || s.itemId == 0 || s.count <= 0) continue;

                var def = ItemDatabase.Instance.Get(s.itemId);
                if (def == null)
                {
                    Debug.LogWarning($"[Inventory] Save slot {i}: unknown item id {s.itemId}, skipped.");
                    continue;
                }
                Slots[i] = new ItemStack { itemId = s.itemId, count = Mathf.Clamp(s.count, 1, def.maxStack) };
            }
            OnChanged?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Inventory] Failed to load '{SavePath}': {e.Message}");
        }
    }

    private void SaveNow()
    {
        dirty = false;
        lastSaveTime = Time.unscaledTime;
        if (!loaded) return;     // never overwrite a save before it was read

        try
        {
            var data = new SaveData { slots = new SaveSlot[TotalSlots] };
            for (int i = 0; i < TotalSlots; i++)
                data.slots[i] = new SaveSlot
                {
                    itemId = Slots[i].IsEmpty ? 0 : Slots[i].itemId,
                    count = Slots[i].IsEmpty ? 0 : Slots[i].count
                };

            string tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(tmp, SavePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Inventory] Failed to save: {e.Message}");
        }
    }
}