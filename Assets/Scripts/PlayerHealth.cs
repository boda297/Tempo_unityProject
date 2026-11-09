using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Server-authoritative health. Only the server writes; everyone reads.
/// Damage/heal must always go through the server (never trust clients).
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealth : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 3f;

    public readonly NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float MaxHealth => maxHealth;

    /// <summary>(current, max) - fires on every machine when health changes.</summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>Fires on every machine when the dead state flips.</summary>
    public event Action<bool> OnDeadStateChanged;

    /// <summary>SERVER ONLY. (victim, attackerClientId) - hook scoring / kill feed here.</summary>
    public event Action<PlayerHealth, ulong> ServerOnDied;

    public override void OnNetworkSpawn()
    {
        CurrentHealth.OnValueChanged += HandleHealthChanged;
        IsDead.OnValueChanged += HandleDeadChanged;

        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
            IsDead.Value = false;
        }

        OnHealthChanged?.Invoke(CurrentHealth.Value, maxHealth);
    }

    public override void OnNetworkDespawn()
    {
        CurrentHealth.OnValueChanged -= HandleHealthChanged;
        IsDead.OnValueChanged -= HandleDeadChanged;
        StopAllCoroutines();
    }

    private void HandleHealthChanged(float previous, float current) => OnHealthChanged?.Invoke(current, maxHealth);
    private void HandleDeadChanged(bool previous, bool current) => OnDeadStateChanged?.Invoke(current);

    // ------------------------------------------------------------------
    // SERVER API - call these from your weapon / hazard / ability code
    // ------------------------------------------------------------------

    public void TakeDamage(float amount, ulong attackerClientId = ulong.MaxValue)
    {
        if (!IsServer || IsDead.Value || amount <= 0f) return;

        CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - amount);

        if (CurrentHealth.Value <= 0f)
            Die(attackerClientId);
    }

    public void Heal(float amount)
    {
        if (!IsServer || IsDead.Value || amount <= 0f) return;
        CurrentHealth.Value = Mathf.Min(maxHealth, CurrentHealth.Value + amount);
    }

    private void Die(ulong attackerClientId)
    {
        IsDead.Value = true;
        ServerOnDied?.Invoke(this, attackerClientId);
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        SpawnPoint.GetRandom(out Vector3 pos, out Quaternion rot);

        // Movement is owner-authoritative, so the owner must do the teleport.
        TeleportOwnerRpc(pos, rot);

        CurrentHealth.Value = maxHealth;
        IsDead.Value = false;
    }

    [Rpc(SendTo.Owner)]
    private void TeleportOwnerRpc(Vector3 position, Quaternion rotation)
    {
        // CharacterController overrides transform.position unless disabled first.
        var cc = GetComponent<CharacterController>();
        cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        cc.enabled = true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Debug: press H to damage yourself by 25 (goes through the server like real damage would).
    private void Update()
    {
        if (!IsOwner || Keyboard.current == null) return;
        if (Keyboard.current.hKey.wasPressedThisFrame)
            DebugDamageRpc(25f);
    }

    [Rpc(SendTo.Server)]
    private void DebugDamageRpc(float amount) => TakeDamage(amount, OwnerClientId);
#endif
}