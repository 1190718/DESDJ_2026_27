using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MistIsles.Core
{
    public enum Sfx
    {
        SwordSwing, HitFlesh, ShellClang, PlayerHurt,
        CrabClick, CrabDie, CrabAttack,
        GuardianRoar, GuardianSlam, GuardianArmorOpen,
        Pickup, UiClick, QuizCorrect, QuizWrong,
        MistLift, RaftSplash, TremorWarning
    }

    public enum MusicTrack { None, Island, Guardian }

    /// <summary>
    /// Plays one-shot effects from a small pool of 3D sources, plus the ambience bed and
    /// two music tracks that crossfade (exploration and guardian fight).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public Sfx id;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume;
        }

        public static AudioManager Instance { get; private set; }

        [SerializeField] private Entry[] effects = Array.Empty<Entry>();
        [SerializeField] private AudioClip ambience;
        [SerializeField] private AudioClip islandMusic;
        [SerializeField] private AudioClip guardianMusic;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.35f;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] private float effectsVolume = 0.9f;
        [SerializeField, Min(1)] private int poolSize = 12;

        private readonly Dictionary<Sfx, Entry> map = new Dictionary<Sfx, Entry>();
        private readonly List<AudioSource> pool = new List<AudioSource>();
        private AudioSource ui, ambienceSource, musicA, musicB;
        private int poolIndex;
        private MusicTrack currentTrack = MusicTrack.None;
        private Coroutine fade;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            foreach (Entry e in effects)
                if (e.clip != null)
                    map[e.id] = e;

            ui = NewSource("UI", 0f);
            ambienceSource = NewSource("Ambience", 0f);
            ambienceSource.loop = true;
            musicA = NewSource("MusicA", 0f);
            musicB = NewSource("MusicB", 0f);
            musicA.loop = musicB.loop = true;
            for (int i = 0; i < poolSize; i++)
            {
                AudioSource s = NewSource("Sfx" + i, 0.85f);
                s.minDistance = 3f;
                s.maxDistance = 45f;
                s.rolloffMode = AudioRolloffMode.Linear;
                pool.Add(s);
            }
        }

        private void Start()
        {
            if (ambience != null)
            {
                ambienceSource.clip = ambience;
                ambienceSource.volume = ambienceVolume;
                ambienceSource.Play();
            }
            PlayMusic(MusicTrack.Island);
        }

        private AudioSource NewSource(string name, float spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = spatial;
            return s;
        }

        /// <summary>Plays an effect. With a position it is 3D, otherwise it plays as UI sound.</summary>
        public static void Play(Sfx id, Vector3? at = null, float pitchJitter = 0.06f, float volumeScale = 1f)
        {
            if (Instance == null || !Instance.map.TryGetValue(id, out Entry e))
                return;
            float vol = (e.volume <= 0f ? 1f : e.volume) * Instance.effectsVolume * volumeScale;
            if (at == null)
            {
                Instance.ui.pitch = 1f;
                Instance.ui.PlayOneShot(e.clip, vol);
                return;
            }
            AudioSource s = Instance.pool[Instance.poolIndex];
            Instance.poolIndex = (Instance.poolIndex + 1) % Instance.pool.Count;
            s.transform.position = at.Value;
            s.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            s.PlayOneShot(e.clip, vol);
        }

        public static void PlayMusic(MusicTrack track)
        {
            if (Instance == null || Instance.currentTrack == track)
                return;
            Instance.currentTrack = track;
            AudioClip clip = track == MusicTrack.Guardian ? Instance.guardianMusic
                           : track == MusicTrack.Island ? Instance.islandMusic : null;
            if (Instance.fade != null)
                Instance.StopCoroutine(Instance.fade);
            Instance.fade = Instance.StartCoroutine(Instance.Crossfade(clip, 1.5f));
        }

        private IEnumerator Crossfade(AudioClip clip, float seconds)
        {
            AudioSource from = musicA.isPlaying ? musicA : musicB;
            AudioSource to = from == musicA ? musicB : musicA;
            if (clip != null)
            {
                to.clip = clip;
                to.volume = 0f;
                to.Play();
            }
            float startFrom = from.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = t / seconds;
                from.volume = Mathf.Lerp(startFrom, 0f, k);
                if (clip != null)
                    to.volume = Mathf.Lerp(0f, musicVolume, k);
                yield return null;
            }
            from.Stop();
            if (clip != null)
                to.volume = musicVolume;
            fade = null;
        }
    }
}
