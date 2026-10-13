using UnityEngine;
using UnityEngine.InputSystem;

namespace CommodoreCircuit.Lobby
{
    /// <summary>
    /// Basic walker for the classroom lobby. Put the Main Camera as a child
    /// of this object and it rides along, like it does on the scooter.
    ///
    /// Keyboard: W/S walk forward/back, A/D turn.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class LobbyPlayerController : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 2.5f;
        [SerializeField] private float turnSpeed = 120f;
        [SerializeField] private float gravity = -9.81f;

        private CharacterController controller;
        private InputAction moveAction;
        private InputAction turnAction;
        private float verticalVelocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();

            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/s")
                .With("Negative", "<Keyboard>/downArrow")
                .With("Positive", "<Keyboard>/w")
                .With("Positive", "<Keyboard>/upArrow");

            turnAction = new InputAction("Turn", InputActionType.Value);
            turnAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/d")
                .With("Positive", "<Keyboard>/rightArrow");
        }

        private void OnEnable()
        {
            moveAction.Enable();
            turnAction.Enable();
        }

        private void OnDisable()
        {
            moveAction.Disable();
            turnAction.Disable();
        }

        private void Update()
        {
            transform.Rotate(0f, turnAction.ReadValue<float>() * turnSpeed * Time.deltaTime, 0f);

            Vector3 move = transform.forward * moveAction.ReadValue<float>() * walkSpeed;

            // Keep the player stuck to the floor.
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -1f;
            verticalVelocity += gravity * Time.deltaTime;
            move.y = verticalVelocity;

            controller.Move(move * Time.deltaTime);
        }
    }
}
