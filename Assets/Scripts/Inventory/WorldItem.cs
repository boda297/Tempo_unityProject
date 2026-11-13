using Unity.Netcode;
using UnityEngine;

public class WorldItem : NetworkBehaviour
{
    [Header("For items placed in the scene (testing)")]
    [SerializeField] private ItemData startItem;
    [SerializeField] private int startCount = 1;

    private readonly NetworkVariable<int> itemId = new NetworkVariable<int>();
    private readonly NetworkVariable<int> count = new NetworkVariable<int>(1);

    private int pendingId;
    private int pendingCount;

    public int ItemId => itemId.Value;
    public int Count => count.Value;

    // Server only, call before Spawn()
    public void Init(int id, int amount)
    {
        pendingId = id;
        pendingCount = amount;
    }

    public override void OnNetworkSpawn()
    {
        // Server simulates physics; clients just follow the NetworkTransform.
        if (TryGetComponent(out Rigidbody rb))
            rb.isKinematic = !IsServer;

        if (!IsServer) return;

        if (pendingId != 0)
        {
            itemId.Value = pendingId;
            count.Value = pendingCount;
        }
        else if (startItem != null && itemId.Value == 0)
        {
            itemId.Value = startItem.id;
            count.Value = startCount;
        }
    }
}