using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lives on the HUDManager object (keep THIS object active).
/// Assign the StatusBars child as 'statusBarsRoot' - it is hidden until a local player binds.
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GameObject statusBarsRoot;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Slider staminaSlider;

    private PlayerHealth health;
    private PlayerStamina stamina;

    private void Awake()
    {
        Instance = this;
        if (statusBarsRoot != null) statusBarsRoot.SetActive(false); // hidden on the title screen
    }

    private void Start()
    {
        // Fallback: the local player may have spawned before this HUD existed.
        var nm = NetworkManager.Singleton;
        if (health == null && nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
        {
            var player = nm.LocalClient.PlayerObject;
            Bind(player.GetComponent<PlayerHealth>(), player.GetComponent<PlayerStamina>());
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (Instance == this) Instance = null;
    }

    public void Bind(PlayerHealth h, PlayerStamina s)
    {
        Unsubscribe();
        health = h;
        stamina = s;

        health.OnHealthChanged += SetHealth;
        stamina.OnStaminaChanged += SetStamina;

        SetHealth(health.CurrentHealth.Value, health.MaxHealth);
        SetStamina(stamina.Current, stamina.Max);

        if (statusBarsRoot != null) statusBarsRoot.SetActive(true);
    }

    /// <summary>Call when the local player despawns (disconnect / back to title).</summary>
    public void Unbind()
    {
        Unsubscribe();
        if (statusBarsRoot != null) statusBarsRoot.SetActive(false);
    }

    private void Unsubscribe()
    {
        if (health != null) health.OnHealthChanged -= SetHealth;
        if (stamina != null) stamina.OnStaminaChanged -= SetStamina;
        health = null;
        stamina = null;
    }

    private void SetHealth(float current, float max)
    {
        if (healthSlider == null) return;
        healthSlider.maxValue = max;
        healthSlider.SetValueWithoutNotify(current);
    }

    private void SetStamina(float current, float max)
    {
        if (staminaSlider == null) return;
        staminaSlider.maxValue = max;
        staminaSlider.SetValueWithoutNotify(current);
    }
}