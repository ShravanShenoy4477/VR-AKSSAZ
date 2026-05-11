using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Short haptic when a hand hovers any XR interactable (clues, decoys, props, UI grabbles).
/// Teleport volumes are skipped so the floor does not buzz constantly.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class InteractableHapticFeedback : MonoBehaviour
{
    [SerializeField] float amplitude = 0.32f;
    [SerializeField] float duration = 0.055f;

    readonly Dictionary<XRBaseInteractable, UnityEngine.Events.UnityAction<HoverEnterEventArgs>> _listeners = new();
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
        _hoverHandler = OnHoverEntered;
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
        }
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
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

    void UnregisterAll()
    {
        foreach (var kv in _listeners)
        {
            if (kv.Key != null)
                kv.Key.hoverEntered.RemoveListener(kv.Value);
        }
        _listeners.Clear();
    }

    static bool ShouldPulse(XRBaseInteractable ix)
    {
        var t = ix.GetType();
        var ns = t.Namespace ?? string.Empty;
        if (ns.Contains("Locomotion.Teleportation", StringComparison.Ordinal))
            return false;
        return true;
    }
}
