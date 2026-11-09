using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop this on empty GameObjects in the world scene to mark respawn locations.
/// Registers itself, so the (scene-less) player prefab never needs scene references.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    private static readonly List<SpawnPoint> All = new List<SpawnPoint>();

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    public static void GetRandom(out Vector3 position, out Quaternion rotation)
    {
        if (All.Count == 0)
        {
            Debug.LogWarning("[SpawnPoint] No spawn points in scene - respawning at origin.");
            position = Vector3.up;
            rotation = Quaternion.identity;
            return;
        }

        var point = All[Random.Range(0, All.Count)];
        position = point.transform.position;
        rotation = point.transform.rotation;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward);
    }
}