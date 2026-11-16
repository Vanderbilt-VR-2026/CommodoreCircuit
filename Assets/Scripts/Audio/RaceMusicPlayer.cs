using CommodoreCircuit.Race;
using UnityEngine;

namespace CommodoreCircuit.Audio
{
    /// <summary>
    /// Plays a playlist in order: when one song ends the next starts, and after
    /// the last one it stays quiet, or starts over with Loop Playlist (the lobby
    /// uses that). Music is local, so each player hears it on their own headset
    /// and nothing goes over the network.
    ///
    /// In a race the music dips when the final lap starts, so the final-lap
    /// sting (RaceTimerHud's Final Lap Clip) can be heard over it.
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
        [Tooltip("Start the playlist again after the last song. On for the lobby, where players can stay a while.")]
        [SerializeField] private bool loopPlaylist;

        [Header("Final lap")]
        [Tooltip("Music volume (fraction of normal) while the final-lap sting plays. 1 = no dip.")]
        [SerializeField, Range(0f, 1f)] private float finalLapDuckLevel = 0.35f;
        [Tooltip("How long the dip lasts, in seconds. Match it to the length of the sting.")]
        [SerializeField, Min(0f)] private float finalLapDuckSeconds = 6f;
        [Tooltip("Seconds to fade down into the dip and back up out of it.")]
        [SerializeField, Min(0.01f)] private float duckFade = 0.4f;

        private AudioSource source;
        private int trackIndex = -1;
        private float trackStartTime;
        private bool hasFocus = true;
        private float duckUntil = -1f;
        private bool duckedFinalLap;

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
            UpdateFinalLapDuck();

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
            if (index >= playlist.Length && loopPlaylist)
            {
                index = 0;
                while (index < playlist.Length && playlist[index] == null) index++;
            }
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

        // The final lap starts once every lap but one is done. Dip once per race.
        private void UpdateFinalLapDuck()
        {
            var me = RaceProgress.Local;
            if (me != null)
            {
                bool onFinalLap = me.Laps > 1 && me.CompletedLaps == me.Laps - 1 && !me.Finished;
                if (onFinalLap && !duckedFinalLap)
                {
                    duckedFinalLap = true;
                    duckUntil = Time.time + finalLapDuckSeconds;
                }
                else if (me.CompletedLaps == 0)
                {
                    duckedFinalLap = false;   // race restarted
                }
            }

            float target = Time.time < duckUntil ? volume * finalLapDuckLevel : volume;
            source.volume = Mathf.MoveTowards(source.volume, target, volume / duckFade * Time.deltaTime);
        }

        private void OnApplicationFocus(bool focus) => hasFocus = focus;
        private void OnApplicationPause(bool paused) => hasFocus = !paused;
    }
}
