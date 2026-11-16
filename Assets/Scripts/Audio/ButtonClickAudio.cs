using UnityEngine;
using UnityEngine.UI;

namespace CommodoreCircuit.Audio
{
    /// <summary>
    /// Plays a click whenever any UI button in this scene is pressed, by laser
    /// or by finger. Put one in the scene and every Button gets the sound,
    /// including ones on panels that are hidden at the start (e.g. the keypad).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ButtonClickAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip clickClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        private AudioSource source;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            GameAudio.Route(source, GameAudio.Channel.UI);
        }

        private void Start()
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                button.onClick.AddListener(PlayClick);
        }

        public void PlayClick()
        {
            if (clickClip) source.PlayOneShot(clickClip, volume);
        }
    }
}
