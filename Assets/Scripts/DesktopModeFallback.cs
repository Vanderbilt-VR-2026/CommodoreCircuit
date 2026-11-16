using System.Collections;
using CommodoreCircuit.Lobby;
using CommodoreCircuit.Scooter;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Lets people without a headset test the game with keyboard and mouse.
/// Adds itself to every scene, so nothing needs to be placed by hand. If no
/// headset turns up, it lifts the camera to eye height, lets the mouse click
/// world-space menus like the chalkboard, and adds the WASD walker in the lobby.
/// The scooter already drives with WASD, so the race scene only gets the mouse fix.
/// </summary>
public class DesktopModeFallback : MonoBehaviour
{
    private const float HeadsetWait = 5f;
    private const float EyeHeight = 1.6f;

    // Null until the first scene has checked for a headset; later scenes reuse the answer.
    private static bool? desktopMode;
    public static bool IsDesktopMode => desktopMode == true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // The first scene has already loaded by now, so cover it here and the rest on load.
        AddToScene();
        SceneManager.sceneLoaded += (scene, mode) => { if (mode == LoadSceneMode.Single) AddToScene(); };
    }

    // Not DontDestroyOnLoad: each scene gets its own, which is gone when the scene unloads.
    private static void AddToScene() => new GameObject(nameof(DesktopModeFallback)).AddComponent<DesktopModeFallback>();

    private IEnumerator Start()
    {
        if (desktopMode == null)
        {
            float waited = 0f;
            while (!XRSettings.isDeviceActive && waited < HeadsetWait)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            desktopMode = !XRSettings.isDeviceActive;
            if (desktopMode == true) Debug.Log("No headset found; using keyboard and mouse.");
        }
        if (desktopMode != true) yield break;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        EnableMouseOnWorldCanvases();

        var scooter = FindFirstObjectByType<ScooterController>();
        if (scooter)
        {
            UseChaseCamera(scooter.transform);
            yield break;
        }

        var origin = FindFirstObjectByType<XROrigin>();
        if (!origin) yield break;

        // Floor tracking leaves the camera on the ground when nothing tracks a head.
        if (origin.CameraFloorOffsetObject)
            origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
        if (!origin.GetComponent<LobbyPlayerController>())
            origin.gameObject.AddComponent<LobbyPlayerController>();
    }

    // Behind and above the scooter, matching where the scenes place the camera in the editor.
    private static readonly Vector3 ChaseOffset = new Vector3(0f, 1.8f, -3.3f);
    private static readonly Vector3 ChaseLookAt = new Vector3(0f, 1f, 2f);

    private Transform chaseTarget;
    private Transform chaseCamera;

    // The camera's Tracked Pose Driver would otherwise drag it down to the scooter's base.
    private void UseChaseCamera(Transform scooter)
    {
        Camera eye = Camera.main;
        if (!eye) return;
        var poseDriver = eye.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        if (poseDriver) poseDriver.enabled = false;
        chaseTarget = scooter;
        chaseCamera = eye.transform;
    }

    // Runs every frame so nothing else can move the camera back.
    private void LateUpdate()
    {
        if (!chaseTarget || !chaseCamera) return;
        chaseCamera.position = chaseTarget.TransformPoint(ChaseOffset);
        chaseCamera.rotation = Quaternion.LookRotation(chaseTarget.TransformPoint(ChaseLookAt) - chaseCamera.position, Vector3.up);
    }

    // The chalkboard only has a Tracked Device Graphic Raycaster, which ignores the mouse.
    private static void EnableMouseOnWorldCanvases()
    {
        Camera eye = Camera.main;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.renderMode != RenderMode.WorldSpace || !canvas.isRootCanvas) continue;
            if (!canvas.GetComponent<GraphicRaycaster>()) canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (eye) canvas.worldCamera = eye;
        }
    }
}
