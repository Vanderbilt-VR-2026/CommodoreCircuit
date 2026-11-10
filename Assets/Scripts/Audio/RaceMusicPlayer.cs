using CommodoreCircuit.Race;
using UnityEngine;

namespace CommodoreCircuit.Audio
{
    /// <summary>
    /// Plays the race playlist in order: when one song ends the next starts,
    /// and after the last one it stays quiet. Music is local, so each player
    /// hears it on their own headset and nothing goes over the network.
    ///
    /// Set the song files to Load Type = Streaming so only the song that is
    /// playing takes up memory.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RaceMusicPlayer : MonoBehaviour
    {
        [SerializeField] private AudioClip[] playlist;
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
        [Tooltip("Wait for GO before the first song, so the countdown beeps are clear. Off starts as soon as the scene loads.")]
        [SerializeField] private bool waitForGo = true;

        private AudioSource source;
        private int trackIndex = -1;
        private float trackStartTime;
        private bool hasFocus = true;

        public AudioClip CurrentTrack => trackIndex >= 0 && trackIndex < playlist.Length ? playlist[trackIndex] : null;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = volume;
            GameAudio.Route(source, GameAudio.Channel.Music);
        }

        private void Update()
        {
            if (playlist == null || playlist.Length == 0) return;

            if (trackIndex < 0)
            {
                if (waitForGo)
                {
                    var session = RaceSession.Current;
                    if (session == null || session.Phase < RacePhase.Racing) return;
                }
                PlayTrack(0);
                return;
            }

            if (trackIndex >= playlist.Length) return;

            // A song that just started may still be buffering, and the headset menu
            // pauses audio, so neither of those counts as the song having ended.
            if (!hasFocus || Time.unscaledTime - trackStartTime < 0.5f) return;
            if (!source.isPlaying)
                PlayTrack(trackIndex + 1);
        }

        private void PlayTrack(int index)
        {
            // Skip empty slots in the list.
            while (index < playlist.Length && playlist[index] == null) index++;
            trackIndex = index;
            if (index >= playlist.Length)
            {
                source.Stop();
                return;
            }

            source.clip = playlist[index];
            source.Play();
            trackStartTime = Time.unscaledTime;
        }

        private void OnApplicationFocus(bool focus) => hasFocus = focus;
        private void OnApplicationPause(bool paused) => hasFocus = !paused;
    }
}
