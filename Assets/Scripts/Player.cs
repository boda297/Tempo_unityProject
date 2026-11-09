using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Unity.Netcode;

public class Player : NetworkBehaviour
{
    public InputActionAsset InputActions;
    public CharacterController playerCharacterController;

    private InputAction playerMoveAction;
    private InputAction playerJumpAction;
    private InputAction playerSprintAction;

    private CinemachineCamera cineCam;

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

    private void OnEnable()
    {
        InputActions.FindActionMap("Player").Enable(); 
    }

    private void OnDisable()
    {
        InputActions.FindActionMap("Player").Disable();
    }

    private void Awake()
    {
        // Explicitly reading from the assigned InputActions asset
        playerMoveAction = InputActions.FindAction("Move");
        playerJumpAction = InputActions.FindAction("Jump");
        playerSprintAction = InputActions.FindAction("Sprint");
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Aggressive initial camera search
            Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                playerCamera = cam.transform;
            }

            CinemachineCamera cineCam = FindFirstObjectByType<CinemachineCamera>();
            if (cineCam != null)
            {
                cineCam.Target.TrackingTarget = cameraTargetObject;

                if (cameraAxisController == null)
                {
                    cameraAxisController = cineCam.GetComponent<CinemachineInputAxisController>();
                }
            }

            CacheBaseCameraGains();
            ApplyCameraSensitivity();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        // Fallback: If Cinemachine camera wasn't found during spawn, find it now
        if (cineCam == null)
        {
            cineCam = FindFirstObjectByType<CinemachineCamera>();
            if (cineCam != null)
            {
                cineCam.Target.TrackingTarget = cameraTargetObject;
                if (cameraAxisController == null)
                {
                    cameraAxisController = cineCam.GetComponent<CinemachineInputAxisController>();
                }
            }
        }

        playerMoveAmount = playerMoveAction.ReadValue<Vector2>();
        PlayerMoveAndRotate();
        Jump();
        ApplyCameraSensitivity();
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

    private void PlayerMoveAndRotate()
    {
        // Fallback check: if it failed during spawn, try again now
        if (playerCamera == null)
        {
            Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (cam != null) playerCamera = cam.transform;
            else return; // If still null, safely skip to prevent errors
        }

        Vector3 playerDirection = new Vector3(playerMoveAmount.x, 0f, playerMoveAmount.y).normalized;
        Vector3 verticalMove = new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime;

        float currentSpeed = playerSprintAction.IsPressed() ? playerSprintSpeed : playerWalkSpeed;

        if (playerDirection.magnitude >= 0.1f)
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

    private void Jump()
    {
        if (playerCharacterController.isGrounded)
        {
            verticalVelocity = -1f;
            if (playerJumpAction.WasPressedThisFrame())
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            float appliedGravity = verticalVelocity < 0f ? gravity * fallMultiplier : gravity;
            verticalVelocity += appliedGravity * Time.deltaTime;
        }
    }
}