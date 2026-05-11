using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;

/// <summary>
/// Short haptic when a hand hovers any XR interactable (clues, decoys, props, UI grabbles).
/// Teleport volumes are skipped so the floor does not buzz constantly.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class InteractableHapticFeedback : MonoBehaviour
{
    static InteractableHapticFeedback _instance;

    [SerializeField] float clueAmplitude = 0.78f;
    [SerializeField] float clueDuration = 0.12f;
    [SerializeField] float ambientAmplitude = 0.10f;
    [SerializeField] float ambientDuration = 0.025f;
    [Header("Visual relevance cue")]
    [SerializeField] bool useFloatingTextCue = false;
    [SerializeField] bool useObjectFlashCue = true;
    [SerializeField] float cueDuration = 2.8f;
    [SerializeField] Vector3 cueOffset = new Vector3(0f, 0.18f, 0f);
    [SerializeField] float cueFontSize = 0.072f;
    [SerializeField] Color clueCueColor = new Color(0.35f, 1f, 0.62f, 1f);
    [SerializeField] Color ambientCueColor = new Color(1f, 0.36f, 0.36f, 1f);
    [SerializeField] Color wrongOrderCueColor = new Color(1f, 0.78f, 0.22f, 1f);
    [SerializeField] float flashEmission = 2.3f;

    readonly Dictionary<XRBaseInteractable, UnityEngine.Events.UnityAction<HoverEnterEventArgs>> _listeners = new();
    readonly Dictionary<XRBaseInteractable, bool> _isClueTier = new();
    readonly Dictionary<XRBaseInteractable, TextMeshPro> _cueLabels = new();
    readonly Dictionary<XRBaseInteractable, float> _cueExpiry = new();
    readonly List<XRBaseInteractable> _cueExpiryCleanup = new();
    readonly Dictionary<XRBaseInteractable, Coroutine> _flashRoutines = new();
    UnityEngine.Events.UnityAction<HoverEnterEventArgs> _hoverHandler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindObjectsByType<InteractableHapticFeedback>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
            return;
        var go = new GameObject("[InteractableHapticFeedback]");
        go.AddComponent<InteractableHapticFeedback>();
    }

    void Awake()
    {
        _instance = this;
        _hoverHandler = OnHoverEntered;
    }

    void Update()
    {
        if (!useFloatingTextCue) return;
        if (_cueExpiry.Count == 0) return;

        var now = Time.unscaledTime;
        var cam = Camera.main != null ? Camera.main.transform : null;
        _cueExpiryCleanup.Clear();

        foreach (var kv in _cueExpiry)
        {
            var ix = kv.Key;
            if (ix == null || !_cueLabels.TryGetValue(ix, out var label) || label == null)
            {
                _cueExpiryCleanup.Add(ix);
                continue;
            }

            if (now > kv.Value)
            {
                label.gameObject.SetActive(false);
                _cueExpiryCleanup.Add(ix);
                continue;
            }

            label.transform.position = GetCueWorldPosition(ix);
            if (cam != null)
            {
                var toCam = cam.position - label.transform.position;
                if (toCam.sqrMagnitude > 0.0001f)
                    label.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
        }

        foreach (var ix in _cueExpiryCleanup)
            _cueExpiry.Remove(ix);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        RegisterAllInActiveScene();
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnregisterAll();
        if (_instance == this) _instance = null;
    }

    public static void ShowTransientWorldCue(Transform target, string text)
    {
        if (_instance == null || target == null || string.IsNullOrEmpty(text))
            return;
        _instance.StartCoroutine(_instance.ShowStandaloneCue(target, text, _instance.wrongOrderCueColor));
    }

    public static void ShowTargetFlashCue(Transform target, bool isClueTier)
    {
        if (_instance == null || target == null || !_instance.useObjectFlashCue)
            return;
        Color cueColor = isClueTier ? _instance.clueCueColor : _instance.ambientCueColor;
        _instance.StartCoroutine(_instance.FlashTargetTransform(target, cueColor));
    }

    public static void ShowWrongOrderCue(Transform target, string text = "[WRONG ORDER]")
    {
        if (_instance == null || target == null)
            return;

        if (_instance.useObjectFlashCue)
            _instance.StartCoroutine(_instance.FlashTargetTransform(target, _instance.wrongOrderCueColor));
        if (_instance.useFloatingTextCue || !string.IsNullOrEmpty(text))
            _instance.StartCoroutine(_instance.ShowStandaloneCue(target, text, _instance.wrongOrderCueColor));
    }

    IEnumerator ShowStandaloneCue(Transform target, string text, Color color)
    {
        var go = new GameObject("WrongOrderCue");
        go.transform.SetParent(null, true);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.fontSize = cueFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder = 1001;
        tmp.text = text;
        tmp.color = color;

        float end = Time.unscaledTime + Mathf.Max(0.35f, cueDuration);
        while (Time.unscaledTime < end)
        {
            if (target == null) break;
            go.transform.position = target.position + cueOffset;
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                var toCam = cam.position - go.transform.position;
                if (toCam.sqrMagnitude > 0.0001f)
                    go.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
            yield return null;
        }

        if (go != null) Destroy(go);
    }

    IEnumerator FlashTargetTransform(Transform target, Color cueColor)
    {
        if (target == null) yield break;
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) yield break;
        float dur = Mathf.Max(0.18f, cueDuration * 0.45f);
        float t = 0f;
        var block = new MaterialPropertyBlock();

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float pulse = 0.25f + 0.75f * (1f - u);
            Color tint = Color.Lerp(Color.white, cueColor, pulse);
            Color emission = cueColor * (flashEmission * pulse);

            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                block.SetColor("_EmissionColor", emission);
                r.SetPropertyBlock(block);
            }
            yield return null;
        }

        foreach (var r in renderers)
        {
            if (r == null) continue;
            block.Clear();
            r.SetPropertyBlock(block);
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        UnregisterAll();
        RegisterAllInActiveScene();
    }

    void RegisterAllInActiveScene()
    {
        foreach (var ix in FindObjectsByType<XRBaseInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ix == null || _listeners.ContainsKey(ix)) continue;
            if (!ShouldPulse(ix)) continue;

            ix.hoverEntered.AddListener(_hoverHandler);
            _listeners[ix] = _hoverHandler;
            _isClueTier[ix] = IsClueTier(ix);
        }
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        bool isClue = false;
        XRBaseInteractable ix = null;
        if (args.interactableObject is XRBaseInteractable typed)
        {
            ix = typed;
            if (_isClueTier.TryGetValue(ix, out bool cached))
                isClue = cached;
        }

        if (ix != null)
            ShowCue(ix, isClue);

        float amplitude = isClue ? clueAmplitude : ambientAmplitude;
        float duration = isClue ? clueDuration : ambientDuration;

        var t = args.interactorObject?.transform;
        if (t == null)
        {
            XrHaptics.PulseLeft(amplitude, duration);
            XrHaptics.PulseRight(amplitude, duration);
            return;
        }

        var n = t.name;
        if (n.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
            XrHaptics.PulseLeft(amplitude, duration);
        else if (n.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
            XrHaptics.PulseRight(amplitude, duration);
        else
        {
            XrHaptics.PulseLeft(amplitude, duration);
            XrHaptics.PulseRight(amplitude, duration);
        }
    }

    void ShowCue(XRBaseInteractable ix, bool isClueTier)
    {
        if (ix == null) return;
        Color cueColor = isClueTier ? clueCueColor : ambientCueColor;

        if (useFloatingTextCue)
        {
            var label = GetOrCreateCue(ix);
            if (label != null)
            {
                label.text = isClueTier ? "CLUE RELATED" : "NOT RELEVANT";
                label.color = cueColor;
                label.transform.position = GetCueWorldPosition(ix);
                label.gameObject.SetActive(true);
                _cueExpiry[ix] = Time.unscaledTime + Mathf.Max(0.2f, cueDuration);
            }
        }

        if (useObjectFlashCue)
        {
            if (_flashRoutines.TryGetValue(ix, out var running) && running != null)
                StopCoroutine(running);
            _flashRoutines[ix] = StartCoroutine(FlashInteractable(ix, cueColor));
        }
    }

    IEnumerator FlashInteractable(XRBaseInteractable ix, Color cueColor)
    {
        if (ix == null) yield break;
        var renderers = ix.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) yield break;

        float dur = Mathf.Max(0.18f, cueDuration * 0.45f);
        float t = 0f;
        var block = new MaterialPropertyBlock();

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float pulse = 0.25f + 0.75f * (1f - u);
            Color tint = Color.Lerp(Color.white, cueColor, pulse);
            Color emission = cueColor * (flashEmission * pulse);

            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                block.SetColor("_EmissionColor", emission);
                r.SetPropertyBlock(block);
            }
            yield return null;
        }

        foreach (var r in renderers)
        {
            if (r == null) continue;
            block.Clear();
            r.SetPropertyBlock(block);
        }

        if (ix != null)
            _flashRoutines.Remove(ix);
    }

    TextMeshPro GetOrCreateCue(XRBaseInteractable ix)
    {
        if (_cueLabels.TryGetValue(ix, out var existing) && existing != null)
            return existing;

        var go = new GameObject($"RelevanceCue_{ix.name}");
        go.transform.SetParent(null, true);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.fontSize = cueFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder = 999;
        go.SetActive(false);

        _cueLabels[ix] = tmp;
        return tmp;
    }

    Vector3 GetCueWorldPosition(XRBaseInteractable ix)
    {
        if (ix == null) return Vector3.zero;
        var tr = ix.transform;
        var pos = tr.position + cueOffset;
        var r = tr.GetComponentInChildren<Renderer>();
        if (r != null)
            pos = r.bounds.center + cueOffset;
        return pos;
    }

    void UnregisterAll()
    {
        foreach (var kv in _listeners)
        {
            if (kv.Key != null)
                kv.Key.hoverEntered.RemoveListener(kv.Value);
        }
        _listeners.Clear();
        _isClueTier.Clear();
        _cueExpiry.Clear();
        _cueExpiryCleanup.Clear();
        foreach (var kv in _cueLabels)
        {
            if (kv.Value != null)
                Destroy(kv.Value.gameObject);
        }
        _cueLabels.Clear();
        foreach (var kv in _flashRoutines)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);
        }
        _flashRoutines.Clear();
    }

    static bool ShouldPulse(XRBaseInteractable ix)
    {
        var t = ix.GetType();
        var ns = t.Namespace ?? string.Empty;
        if (ns.Contains("Locomotion.Teleportation", StringComparison.Ordinal))
            return false;
        return true;
    }

    static bool IsClueTier(XRBaseInteractable ix)
    {
        if (ix == null) return false;
        var tr = ix.transform;
        if (tr.GetComponentInParent<DecoyGramophoneCD>() != null ||
            tr.GetComponentInParent<DecoyGramophoneHandle>() != null)
            return PuzzleManager.IsDecoyPathUnlocked("gramophone");

        return tr.GetComponentInParent<ClueNote>() != null
            || tr.GetComponentInParent<ProximityClueNote>() != null
            || tr.GetComponentInParent<GlobeClueReveal>() != null
            || tr.GetComponentInParent<ClockProximityTilt>() != null
            || tr.GetComponentInParent<WallClockInteractable>() != null
            || tr.GetComponentInParent<SageLight>() != null
            || tr.GetComponentInParent<DecoyNote>() != null
            || tr.GetComponentInParent<DecoyGramophoneCD>() != null
            || tr.GetComponentInParent<DecoyGramophoneHandle>() != null;
    }
}
