using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerStamina))]
public class PlayerController : NetworkBehaviour
{
    public InputActionAsset InputActions;
    public CharacterController playerCharacterController;

    private InputAction playerMoveAction;
    private InputAction playerJumpAction;
    private InputAction playerSprintAction;

    private CinemachineCamera cineCam;
    private bool cinemachineBound;

    [SerializeField]
    private Transform playerCamera;
    private Vector2 playerMoveAmount;

    [Header("Camera")]
    [Tooltip("The specific transform (e.g., a child object at head-level) the Cinemachine camera will track.")]
    [SerializeField] private Transform cameraTargetObject;
    [Tooltip("Scales the Cinemachine camera's look sensitivity. 1 = default, 2 = double speed, etc.")]
    [SerializeField] private float cameraSensitivity = 1f;
    [Tooltip("The CinemachineInputAxisController on your CinemachineCamera (drives mouse/stick look).")]
    [SerializeField] private CinemachineInputAxisController cameraAxisController;
    private float[] baseCameraGains;

    [Header("Movement")]
    [SerializeField] private float playerWalkSpeed = 5.0f;
    [SerializeField] private float playerSprintSpeed = 8.0f;
    [SerializeField] private float playerRotateDampening = 0.1f;
    private float turnSmoothingVelocity;

    [Header("Jumping & Gravity")]
    [SerializeField] private float verticalVelocity = 0f;
    [SerializeField] private float gravity = -9.8f;
    [SerializeField] private float jumpHeight = 5.0f;
    [Tooltip("Multiplies gravity while falling (verticalVelocity < 0) so descent hits harder than the rise, closer to how a real fall feels.")]
    [SerializeField] private float fallMultiplier = 2.5f;

    // Health / stamina (separate components on the same prefab)
    private PlayerHealth health;
    private PlayerStamina stamina;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        stamina = GetComponent<PlayerStamina>();

        // Explicitly reading from the assigned InputActions asset
        playerMoveAction = InputActions.FindAction("Move");
        playerJumpAction = InputActions.FindAction("Jump");
        playerSprintAction = InputActions.FindAction("Sprint");
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        // The InputActionAsset is shared by every instance, so only the OWNER may enable/disable
        // the map. (If remote players did it in OnEnable/OnDisable, a remote player despawning
        // would switch off the local player's input.)
        InputActions.FindActionMap("Player").Enable();

        // Aggressive initial camera search
        Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            playerCamera = cam.transform;
        }

        cinemachineBound = TryBindCinemachine();
        if (!cinemachineBound)
            Debug.LogWarning("[PlayerController] No CinemachineCamera found in the scene yet - will keep retrying.", this);

        CacheBaseCameraGains();
        ApplyCameraSensitivity();

        // Show health + stamina bars for the local player only
        if (PlayerHUD.Instance != null)
            PlayerHUD.Instance.Bind(health, stamina);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        if (PlayerHUD.Instance != null)
            PlayerHUD.Instance.Unbind();

        InputActions.FindActionMap("Player").Disable();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        // Fallback: If the Cinemachine camera wasn't found during spawn, keep trying
        if (!cinemachineBound)
        {
            cinemachineBound = TryBindCinemachine();
            if (cinemachineBound) CacheBaseCameraGains();
        }

        // Dead players lose control but still fall to the ground.
        bool canAct = !health.IsDead.Value;

        playerMoveAmount = canAct ? playerMoveAction.ReadValue<Vector2>() : Vector2.zero;
        PlayerMoveAndRotate(canAct);
        Jump(canAct);
        ApplyCameraSensitivity();
    }

    /// <summary>Finds the scene's CinemachineCamera and points it at this player.</summary>
    private bool TryBindCinemachine()
    {
        if (cineCam == null)
            cineCam = FindFirstObjectByType<CinemachineCamera>();
        if (cineCam == null) return false;

        // Never hand Cinemachine a null target - that is exactly "camera doesn't follow".
        Transform target = cameraTargetObject != null ? cameraTargetObject : transform;
        if (cameraTargetObject == null)
            Debug.LogWarning("[PlayerController] 'Camera Target Object' is not assigned on the prefab - following the player root instead.", this);

        cineCam.Target.TrackingTarget = target;

        if (cameraAxisController == null)
            cameraAxisController = cineCam.GetComponent<CinemachineInputAxisController>();

        return true;
    }

    public void SetCameraSensitivity(float sensitivity)
    {
        cameraSensitivity = sensitivity;
        ApplyCameraSensitivity();
    }

    private void CacheBaseCameraGains()
    {
        if (cameraAxisController == null) return;

        baseCameraGains = new float[cameraAxisController.Controllers.Count];
        for (int i = 0; i < cameraAxisController.Controllers.Count; i++)
            baseCameraGains[i] = cameraAxisController.Controllers[i].Input.Gain;
    }

    private void ApplyCameraSensitivity()
    {
        if (cameraAxisController == null || baseCameraGains == null) return;

        for (int i = 0; i < cameraAxisController.Controllers.Count; i++)
            cameraAxisController.Controllers[i].Input.Gain = baseCameraGains[i] * cameraSensitivity;
    }

    private void PlayerMoveAndRotate(bool canAct)
    {
        // Fallback check: if it failed during spawn, try again now
        if (playerCamera == null)
        {
            Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (cam != null) playerCamera = cam.transform;
            else
            {
                stamina.SetSprinting(false);
                return; // If still null, safely skip to prevent errors
            }
        }

        Vector3 playerDirection = new Vector3(playerMoveAmount.x, 0f, playerMoveAmount.y).normalized;
        Vector3 verticalMove = new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime;

        bool hasInput = playerDirection.magnitude >= 0.1f;

        // Sprint only while held, moving, alive, and with stamina available.
        bool sprinting = canAct && hasInput && playerSprintAction.IsPressed() && stamina.CanSprint;
        stamina.SetSprinting(sprinting);

        float currentSpeed = sprinting ? playerSprintSpeed : playerWalkSpeed;

        if (hasInput)
        {
            float targetAngle = Mathf.Atan2(playerDirection.x, playerDirection.z) * Mathf.Rad2Deg + playerCamera.eulerAngles.y;
            float smoothTargetAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothingVelocity, playerRotateDampening);
            transform.rotation = Quaternion.Euler(0, smoothTargetAngle, 0);

            Vector3 moveDirection = Quaternion.Euler(0, targetAngle, 0) * Vector3.forward;
            playerCharacterController.Move(moveDirection.normalized * currentSpeed * Time.deltaTime + verticalMove);
        }
        else
        {
            playerCharacterController.Move(verticalMove);
        }
    }

    private void Jump(bool canAct)
    {
        if (playerCharacterController.isGrounded)
        {
            verticalVelocity = -1f;
            if (canAct && playerJumpAction.WasPressedThisFrame() && stamina.TryConsumeJump())
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            float appliedGravity = verticalVelocity < 0f ? gravity * fallMultiplier : gravity;
            verticalVelocity += appliedGravity * Time.deltaTime;
        }
    }
}