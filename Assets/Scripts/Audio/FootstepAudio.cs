using UnityEngine;

namespace CommodoreCircuit.Audio
{
    /// <summary>
    /// Plays a footstep every time the player's head travels one stride across
    /// the floor, whether they walk for real or push the thumbstick. Put it on
    /// any object in the classroom scene: it follows the main camera, which is
    /// the local player's head, so only you hear your own steps.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FootstepAudio : MonoBehaviour
    {
        [Tooltip("One is picked at random for each step. Add more for variety.")]
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
        [Tooltip("Meters of floor travel between steps.")]
        [SerializeField] private float strideLength = 0.7f;
        [Tooltip("Moving slower than this (m/s) counts as standing still, so leaning and looking around stay quiet.")]
        [SerializeField] private float minWalkSpeed = 0.4f;
        [Tooltip("Moving more than this in one frame is a teleport or respawn, not a step.")]
        [SerializeField] private float teleportDistance = 1f;
        [Tooltip("Each step gets a random pitch in this range so repeats don't sound identical.")]
        [SerializeField] private Vector2 pitchRange = new Vector2(0.92f, 1.08f);

        private AudioSource source;
        private Transform head;
        private Vector3 lastPosition;
        private float distanceSinceStep;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // your own feet, so no 3D position
            GameAudio.Route(source, GameAudio.Channel.Sfx);
        }

        private void Update()
        {
            // The rig is spawned after the scene loads, so keep looking until the camera exists.
            if (head == null)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                head = cam.transform;
                lastPosition = head.position;
                return;
            }

            Vector3 travel = head.position - lastPosition;
            travel.y = 0f;
            lastPosition = head.position;

            float moved = travel.magnitude;
            if (moved > teleportDistance)
            {
                distanceSinceStep = 0f;
                return;
            }
            if (Time.deltaTime <= 0f || moved / Time.deltaTime < minWalkSpeed) return;

            distanceSinceStep += moved;
            if (distanceSinceStep >= strideLength)
            {
                distanceSinceStep = 0f;
                PlayStep();
            }
        }

        private void PlayStep()
        {
            if (footstepClips == null || footstepClips.Length == 0) return;
            AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
            if (clip == null) return;
            source.pitch = Random.Range(pitchRange.x, pitchRange.y);
            source.PlayOneShot(clip, volume);
        }
    }
}
