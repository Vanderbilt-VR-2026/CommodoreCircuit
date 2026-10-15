using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

// Spin the player 180° on a button press, e.g. to check behind you on the scooter.
// Not attached to anything yet: add it to the XR Origin (XR Rig) when Commodore Circuit needs it.
public class TurnAroundButton : MonoBehaviour
{
    public XROrigin Origin;
    public InputActionProperty TurnAroundAction;

    // Listen for the button only while this component is enabled.
    private void OnEnable()
    {
        TurnAroundAction.action.performed += OnTurnAround;
        TurnAroundAction.action.Enable();
    }

    private void OnDisable() => TurnAroundAction.action.performed -= OnTurnAround;

    // Rotate around the headset, not the rig's centre, so the player doesn't slide sideways.
    private void OnTurnAround(InputAction.CallbackContext context) =>
        Origin.RotateAroundCameraUsingOriginUp(180f);
}
