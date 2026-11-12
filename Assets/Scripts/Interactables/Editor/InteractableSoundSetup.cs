#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CommodoreCircuit.Interactables.EditorTools
{
    /// <summary>
    /// CommodoreCircuit > Interactables > Add Sounds To Interactable Prefabs
    ///
    /// Gives each interactable prefab its sound: an AudioSource on the object that
    /// gets hit or collected (so the sound comes from where it is), plugged into the
    /// Hazard's Hit Audio or the PowerUp's Pickup Audio. Edits the existing prefabs in
    /// place, so tuning done on them is kept, and running it again just updates the
    /// clips. Build Interactable Prefabs runs it too, so rebuilt prefabs keep their sounds.
    /// </summary>
    public static class InteractableSoundSetup
    {
        const string SfxDir = "Assets/Audio/SFX/";
        const string MixerPath = "Assets/Audio/Resources/MainMixer.mixer";

        // Prefab name -> sound file in Assets/Audio/SFX. Empty = no sound yet.
        static readonly (string prefab, string clip)[] Sounds =
        {
            ("Squirrel", "Squirrel Collision.wav"),
            ("LostFreshman", "Student Collision.wav"),
            ("ConstructionPipe", "Pole Collision.mp3"),
            ("StrayFrisbee", ""),
            ("SpeedCookie", "Cookie Boost.wav"),
            ("MealSwipe", "Commodore Card.mp3"),
        };

        [MenuItem("CommodoreCircuit/Interactables/Add Sounds To Interactable Prefabs")]
        static void Run()
        {
            string report = Apply();
            Debug.Log("[Commodore Circuit] Interactable sounds:\n" + report);
            EditorUtility.DisplayDialog("Interactable sounds", report, "OK");
        }

        /// <summary>Adds or updates the sound on every interactable prefab. Returns a line per prefab.</summary>
        public static string Apply()
        {
            AudioMixerGroup sfx = SfxGroup();
            var report = new System.Text.StringBuilder();

            foreach (var (prefab, clipFile) in Sounds)
            {
                string path = $"{InteractablePaths.Prefabs}/{prefab}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    report.AppendLine($"{prefab}: prefab missing, skipped");
                    continue;
                }
                AudioClip clip = clipFile.Length > 0 ? AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + clipFile) : null;
                if (clipFile.Length > 0 && clip == null)
                {
                    report.AppendLine($"{prefab}: {clipFile} not found in {SfxDir}, skipped");
                    continue;
                }
                if (clip == null)
                {
                    report.AppendLine($"{prefab}: no sound yet");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var hazard = root.GetComponentInChildren<Hazard>(true);
                    var powerUp = root.GetComponentInChildren<PowerUp>(true);
                    GameObject host = hazard ? hazard.gameObject : powerUp ? powerUp.gameObject : null;
                    if (host == null)
                    {
                        report.AppendLine($"{prefab}: no Hazard or PowerUp on it, skipped");
                        continue;
                    }

                    AudioSource source = hazard ? hazard.hitAudio : powerUp.pickupAudio;
                    if (source == null) source = host.GetComponent<AudioSource>();
                    if (source == null) source = host.AddComponent<AudioSource>();
                    SetUp(source, clip, sfx);
                    if (hazard) hazard.hitAudio = source;
                    else powerUp.pickupAudio = source;

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.AppendLine($"{prefab}: {clipFile}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            return report.ToString().TrimEnd();
        }

        // Positioned in the world so it comes from the thing you hit, but at full volume
        // within a few metres, since the rider is always right on top of it.
        static void SetUp(AudioSource source, AudioClip clip, AudioMixerGroup sfx)
        {
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 1f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 6f;
            source.maxDistance = 35f;
            source.dopplerLevel = 0f;
            if (sfx != null) source.outputAudioMixerGroup = sfx;
        }

        static AudioMixerGroup SfxGroup()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null) return null;
            foreach (AudioMixerGroup group in mixer.FindMatchingGroups("SFX"))
                if (group.name == "SFX") return group;
            return null;
        }
    }
}
#endif
