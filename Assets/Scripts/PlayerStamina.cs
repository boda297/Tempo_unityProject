using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-local stamina. Movement is owner-authoritative in this project, so stamina
/// lives with the owner too - no network traffic needed. If you later move to
/// server-authoritative movement, move this logic to the server as well.
/// </summary>
[DisallowMultipleComponent]
public class PlayerStamina : NetworkBehaviour
{
    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float sprintDrainPerSecond = 20f;
    [SerializeField] private float regenPerSecond = 15f;
    [SerializeField] private float regenDelay = 1.5f;

    [Header("Actions")]
    [SerializeField] private float jumpCost = 15f;

    [Header("Exhaustion")]
    [Tooltip("After hitting 0, sprint stays locked until stamina refills to this fraction of max.")]
    [SerializeField, Range(0f, 1f)] private float exhaustionRecoverFraction = 0.25f;

    private float current;
    private float lastNotified = -1f;
    private float lastUseTime;
    private bool exhausted;
    private bool sprintRequested;

    public float Current => current;
    public float Max => maxStamina;
    public bool IsExhausted => exhausted;
    public bool CanSprint => !exhausted && current > 0f;

    /// <summary>(current, max)</summary>
    public event Action<float, float> OnStaminaChanged;

    private void Awake()
    {
        current = maxStamina; // valid value before spawn so HUD binding never reads 0
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false; // remote clones don't simulate stamina
            return;
        }

        current = maxStamina;
        exhausted = false;
        Notify(force: true);
    }

    /// <summary>Called by the controller every frame: is the player actively sprinting?</summary>
    public void SetSprinting(bool sprinting) => sprintRequested = sprinting;

    /// <summary>Spend a one-off amount (jump, dodge, melee...). Returns false if not enough.</summary>
    public bool TryConsume(float amount)
    {
        if (exhausted || current < amount) return false;

        current -= amount;
        lastUseTime = Time.time;
        if (current <= 0f) { current = 0f; exhausted = true; }
        Notify();
        return true;
    }

    public bool TryConsumeJump() => TryConsume(jumpCost);

    private void Update()
    {
        if (sprintRequested && CanSprint)
        {
            current -= sprintDrainPerSecond * Time.deltaTime;
            lastUseTime = Time.time;

            if (current <= 0f)
            {
                current = 0f;
                exhausted = true;
            }
        }
        else if (Time.time - lastUseTime >= regenDelay && current < maxStamina)
        {
            current = Mathf.Min(maxStamina, current + regenPerSecond * Time.deltaTime);

            if (exhausted && current >= maxStamina * exhaustionRecoverFraction)
                exhausted = false;
        }

        Notify();
    }

    private void Notify(bool force = false)
    {
        if (!force && Mathf.Approximately(current, lastNotified)) return;
        lastNotified = current;
        OnStaminaChanged?.Invoke(current, maxStamina);
    }
}