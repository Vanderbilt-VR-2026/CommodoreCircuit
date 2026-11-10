using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace CommodoreCircuit.Audio
{
    /// <summary>
    /// The player's Music and Sound Effects volumes, saved between launches and
    /// applied to the MainMixer. The mixer must be named MainMixer, sit in a
    /// Resources folder (Assets/Audio/Resources), and have these groups:
    ///
    ///   Master
    ///   ├─ Music          volume exposed as "Music Volume"
    ///   └─ SFX            volume exposed as "SFX Volume"
    ///      └─ UI          (follows the SFX slider)
    ///
    /// Without the mixer, sound still plays and the sliders still save; they
    /// just don't change anything.
    /// </summary>
    public static class GameAudio
    {
        public enum Channel { Music, Sfx, UI }

        private const string MixerResource = "MainMixer";
        private static AudioMixer mixer;
        private static bool triedLoading;

        private static AudioMixer Mixer
        {
            get
            {
                if (!triedLoading)
                {
                    triedLoading = true;
                    mixer = Resources.Load<AudioMixer>(MixerResource);
                    if (mixer == null)
                        Debug.LogWarning($"GameAudio: no AudioMixer named '{MixerResource}' in a Resources folder, so volume settings do nothing.");
                }
                return mixer;
            }
        }

        /// <summary>Saved volume, 0 (silent) to 1 (full).</summary>
        public static float GetVolume(Channel channel) => PlayerPrefs.GetFloat(PrefKey(channel), 1f);

        public static void SetVolume(Channel channel, float volume)
        {
            volume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(PrefKey(channel), volume);
            PlayerPrefs.Save();
            Apply(channel);
        }

        /// <summary>
        /// Sends a source to its mixer group, unless one was already set in the Inspector.
        /// </summary>
        public static void Route(AudioSource source, Channel channel)
        {
            if (source == null || source.outputAudioMixerGroup != null || Mixer == null) return;
            // Search matches paths, so "SFX" also finds SFX/UI: pick the one with the exact name.
            string name = GroupName(channel);
            foreach (AudioMixerGroup group in Mixer.FindMatchingGroups(name))
            {
                if (group.name != name) continue;
                source.outputAudioMixerGroup = group;
                return;
            }
        }

        // A mixer only takes SetFloat once the scene is running, so apply on every
        // scene load rather than once at startup.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Apply(Channel.Music);
                Apply(Channel.Sfx);
            };
        }

        private static void Apply(Channel channel)
        {
            if (Mixer == null || channel == Channel.UI) return;
            Mixer.SetFloat(channel == Channel.Music ? "Music Volume" : "SFX Volume", ToDecibels(GetVolume(channel)));
        }

        // Faders work in decibels: halving the slider is about -6 dB, and 0 is silent.
        private static float ToDecibels(float volume) => volume > 0.0001f ? Mathf.Log10(volume) * 20f : -80f;

        private static string GroupName(Channel channel) =>
            channel == Channel.Music ? "Music" : channel == Channel.Sfx ? "SFX" : "UI";

        private static string PrefKey(Channel channel) => "CommodoreCircuit.Volume." + channel;
    }
}
