using UnityEngine;

/// <summary>
/// Runtime audio cues (procedural) for menu toggles, clue/wrong interactions, progression, and ambience.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameAudioFeedback : MonoBehaviour
{
    static GameAudioFeedback _instance;

    AudioSource _musicSource;
    AudioSource _sfxSource;

    AudioClip _menuBeep;
    AudioClip _correctCue;
    AudioClip _wrongCue;
    AudioClip _decoyRevealCue;
    AudioClip _clueCompleteCue;
    AudioClip _keyAcquiredCue;
    AudioClip _doorWinCue;
    AudioClip _ambientLoop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<GameAudioFeedback>() != null) return;
        var go = new GameObject("[GameAudioFeedback]");
        DontDestroyOnLoad(go);
        go.AddComponent<GameAudioFeedback>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.loop = true;
        _musicSource.playOnAwake = false;
        _musicSource.spatialBlend = 0f;
        _musicSource.volume = 0.28f;

        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.loop = false;
        _sfxSource.playOnAwake = false;
        _sfxSource.spatialBlend = 0f;
        _sfxSource.volume = 0.9f;

        BuildClips();
        StartAmbient();
        DoorProximityHinge.OnDoorOpened += HandleDoorOpened;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        DoorProximityHinge.OnDoorOpened -= HandleDoorOpened;
    }

    void HandleDoorOpened()
    {
        PlayDoorOpenedSuccess();
    }

    void StartAmbient()
    {
        if (_ambientLoop == null || _musicSource == null) return;
        _musicSource.clip = _ambientLoop;
        _musicSource.Play();
    }

    void BuildClips()
    {
        _menuBeep = BuildToneSequence("menu_beep", new[] { 820f }, 0.07f, 0.22f);
        _correctCue = BuildToneSequence("correct_cue", new[] { 560f, 700f }, 0.08f, 0.28f);
        _wrongCue = BuildToneSequence("wrong_cue", new[] { 320f, 220f }, 0.11f, 0.30f, addBuzz: true);
        _decoyRevealCue = BuildToneSequence("decoy_reveal", new[] { 430f, 360f, 280f }, 0.10f, 0.34f, addBuzz: true);
        _clueCompleteCue = BuildToneSequence("clue_complete", new[] { 420f, 560f, 740f }, 0.12f, 0.32f);
        _keyAcquiredCue = BuildToneSequence("key_acquired", new[] { 360f, 520f }, 0.10f, 0.30f);
        _doorWinCue = BuildToneSequence("door_win", new[] { 523f, 659f, 784f, 1046f }, 0.12f, 0.36f);
        _ambientLoop = BuildAmbientLoop("ambient_loop", 8f);
    }

    static AudioClip BuildToneSequence(string name, float[] freqs, float secondsPerTone, float volume, bool addBuzz = false)
    {
        int sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int totalSamples = Mathf.Max(1, Mathf.RoundToInt(secondsPerTone * freqs.Length * sampleRate));
        var data = new float[totalSamples];
        int cursor = 0;

        for (int i = 0; i < freqs.Length; i++)
        {
            int toneSamples = Mathf.RoundToInt(secondsPerTone * sampleRate);
            float f = Mathf.Max(60f, freqs[i]);
            for (int s = 0; s < toneSamples && cursor < totalSamples; s++, cursor++)
            {
                float t = s / (float)sampleRate;
                float env = Envelope01(s / (float)Mathf.Max(1, toneSamples - 1), 0.08f, 0.25f);
                float v = Mathf.Sin(2f * Mathf.PI * f * t);
                if (addBuzz)
                    v = 0.65f * v + 0.35f * Mathf.Sign(v);
                data[cursor] = v * env * volume;
            }
        }

        var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip BuildAmbientLoop(string name, float seconds)
    {
        int sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int totalSamples = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
        var data = new float[totalSamples];
        for (int i = 0; i < totalSamples; i++)
        {
            float t = i / (float)sampleRate;
            float drift = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.06f * t);
            float a = Mathf.Sin(2f * Mathf.PI * (92f + 4f * drift) * t);
            float b = Mathf.Sin(2f * Mathf.PI * (138f + 5f * drift) * t + 0.7f);
            float c = Mathf.Sin(2f * Mathf.PI * (184f + 3f * drift) * t + 1.3f);
            float mix = 0.5f * a + 0.34f * b + 0.16f * c;
            data[i] = mix * 0.12f;
        }
        var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Envelope01(float u, float attack, float release)
    {
        attack = Mathf.Clamp01(attack);
        release = Mathf.Clamp01(release);
        float a = attack <= 0f ? 1f : Mathf.Clamp01(u / attack);
        float r = release <= 0f ? 1f : Mathf.Clamp01((1f - u) / release);
        return Mathf.Min(a, r);
    }

    void PlayOneShot(AudioClip clip, float volumeScale = 1f)
    {
        if (_sfxSource == null || clip == null) return;
        _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    public static void PlayMenuSelect()
    {
        if (_instance == null) return;
        _instance.PlayOneShot(_instance._menuBeep, 0.78f);
    }

    public static void PlayCorrectSelection()
    {
        if (_instance == null) return;
        _instance.PlayOneShot(_instance._correctCue, 0.85f);
    }

    public static void PlayWrongSelection()
    {
        if (_instance == null) return;
        _instance.PlayOneShot(_instance._wrongCue, 0.90f);
    }

    public static void PlayClueComplete()
    {
        if (_instance == null) return;
        _instance.PlayOneShot(_instance._clueCompleteCue, 0.98f);
    }

    public static void PlayDecoyReveal()
    {
        if (_instance == null) return;
        _instance.PlayOneShot(_instance._decoyRevealCue, 0.95f);
    }

    public static void PlayKeyAcquired()
    {
        if (_instance == null) return;
        _instance.PlayOneShot(_instance._keyAcquiredCue, 0.92f);
    }

    public static void PlayDoorOpenedSuccess()
    {
        if (_instance == null) return;
        _instance.PlayOneShot(_instance._doorWinCue, 1f);
    }
}
