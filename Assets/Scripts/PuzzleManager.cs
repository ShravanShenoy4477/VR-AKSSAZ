using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Singleton that wires together the full puzzle chain.
///
/// Responsibilities:
///   - Hides the key at game start (uses an Inspector-assigned key object).
///   - Receives clue solve reports from clue scripts (single source of truth).
///   - Shows a "Clue X/3 Solved" progress toast after each real clue.
///   - Reveals the key and shows "KEY REVEALED!" when all 3 clues are solved.
///   - Resets correctly when the scene is reloaded (Play Again).
///
/// Setup: Attach this script to an empty GameObject named "PuzzleManager" in the scene.
/// Do NOT delete it from the scene — clue scripts report completion to this manager.
/// </summary>
[DisallowMultipleComponent]
public class PuzzleManager : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────────────────
    /// <summary>Clue indices that must all be solved before the key is revealed.</summary>
    private static readonly int[] RequiredClues = { 1, 2, 3 };

    [Tooltip("Drag the key GameObject here. It will be hidden at start and revealed after all clues.")]
    public GameObject keyObject;

    [Header("Scene references")]
    [Tooltip("Optional: exact door hinge that should accept only this key instance.")]
    [SerializeField] DoorProximityHinge doorHinge;
    [Tooltip("Optional: explicit door-knob target for guidance lights. If empty, uses door trigger collider center.")]
    [SerializeField] Transform doorGuideTarget;

    [Tooltip("Light that will be enabled when all 3 clues are revealed.")]
    public Light revealLight;

    [Header("Key grab comfort")]
    [Tooltip("Attach offset on the key for natural horizontal hold (in local space of the key/grab object).")]
    [SerializeField] Vector3 keyGrabAttachLocalOffset = new Vector3(0f, 0f, 0f);
    [SerializeField] Vector3 keyGrabAttachLocalEuler = new Vector3(0f, 0f, 90f);

    [Header("Door guidance (on key pickup)")]
    [SerializeField] Color doorGuideColor = new Color(1f, 0.88f, 0.45f, 1f);
    [SerializeField] float doorGuideSpotIntensity = 0.45f;
    [SerializeField] float doorGuideSpotRange = 4.6f;
    [SerializeField] float doorGuideSpotAngle = 34f;
    [SerializeField] Vector3 doorGuideSpotOffset = new Vector3(-0.06f, -0.06f, 0f);
    [SerializeField] float doorGuideHingeFillIntensity = 1.1f;
    [SerializeField] Vector3 doorGuideFillOffset = new Vector3(-0.05f, 0.02f, 0f);
    [SerializeField] float doorGuideHingePulseIntensity = 3.0f;
    [SerializeField] float doorGuideHingePulseRange = 1.4f;
    [SerializeField] float doorGuideHingePulseAngle = 28f;
    [SerializeField] Vector3 doorGuideHingePulseOffset = new Vector3(-0.09f, -0.10f, 0f);
    [SerializeField] float doorGuidePulseSpeed = 3.8f;
    [SerializeField] float doorGuidePulseAmount = 0.25f;
    [Header("Key spotlight fallback")]
    [SerializeField] Color keyGuideColor = new Color(1f, 0.94f, 0.82f, 1f);
    [SerializeField] float keyGuideIntensity = 3.1f;
    [SerializeField] float keyGuideRange = 2.4f;
    [SerializeField] float keyGuideAngle = 46f;
    [SerializeField] Vector3 keyGuideOffset = new Vector3(0.22f, 0.30f, 0.20f);

    [Header("Clue 3 clock focus lighting")]
    [SerializeField] bool enableClockFocusAfterClue3 = true;
    [SerializeField] Vector3 clockFocusSpotOffset = new Vector3(0f, 0.75f, 0.12f);
    [SerializeField] Color clockFocusColor = new Color(1f, 0.93f, 0.78f, 1f);
    [SerializeField] float clockFocusSpotIntensity = 3.0f;
    [SerializeField] float clockFocusSpotRange = 4.5f;
    [SerializeField] float clockFocusSpotAngle = 36f;
    [SerializeField] float clockFocusAmbientColorScale = 0.34f;
    [SerializeField] float clockFocusAmbientIntensityScale = 0.30f;
    [SerializeField] float clockFocusOtherLightIntensityScale = 0.12f;

    [Header("Progress toast placement (head-relative)")]
    [SerializeField] float notifForwardMeters = 0.48f;
    [SerializeField] float notifRightMeters = 0.06f;
    [SerializeField] float notifUpMeters = -0.03f;
    [Tooltip("Log each time a toast is shown (paste if it still clips or sits wrong).")]
    [SerializeField] bool debugNotifPlacement;

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired when all required clues are solved and the key is revealed.</summary>
    public static event System.Action OnPuzzleComplete;

    /// <summary>Number of unique required clues solved so far.</summary>
    public static int SolvedClueCount => _instance != null ? _instance._solvedClues.Count : 0;
    public static int DecoyInteractionCount => _instance != null ? _instance._decoyInteractions : 0;

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BgDark      = MenuThemes.Completion.Background;
    static readonly Color AccentGold  = MenuThemes.Completion.AccentPrimary;
    static readonly Color AccentGreen = MenuThemes.Completion.AccentSuccess;
    static readonly Color AccentBlue  = MenuThemes.Completion.AccentInfo;
    static readonly Color TextPrimary = MenuThemes.Completion.TextPrimary;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private static PuzzleManager _instance;

    private readonly HashSet<int> _solvedClues = new HashSet<int>();
    private readonly HashSet<string> _seenDecoyIds = new HashSet<string>();
    private int _decoyInteractions;
    private bool _decoyGramophoneUnlocked;
    private bool _decoyTelescopeUnlocked;
    private bool       _puzzleComplete = false;

    // Notification HUD
    private Canvas          _notifCanvas;
    private GameObject      _notifPanel;
    private Image           _accentBar;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _bodyText;
    private Coroutine       _dismissRoutine;
    private XRGrabInteractable _keyGrabInteractable;
    private Light _doorGuideSpot;
    private Light _doorGuideHingeFill;
    private Light _doorGuideHingePulseSpot;
    private Light _keyGuideSpot;
    private readonly List<Light> _clockFocusSpots = new List<Light>(8);
    private readonly Dictionary<Light, float> _clockLightIntensityBeforeFocus = new Dictionary<Light, float>(128);
    private Color _ambientSkyBeforeClockFocus;
    private Color _ambientEqBeforeClockFocus;
    private Color _ambientGroundBeforeClockFocus;
    private Color _ambientFlatBeforeClockFocus;
    private float _ambientIntensityBeforeClockFocus;
    private bool _clockFocusLightingActive;
    private bool _clockFocusLightingSnapshotReady;
    private Vector3 _doorGuideTargetWorld;
    private bool _hasDoorGuideTarget;
    private bool _doorGuidanceActive;
    private float _nextOutOfTurnToastTime;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;

        SceneManager.sceneLoaded += OnSceneLoaded;

        BuildNotificationUI();
    }

    void Start()
    {
        FindAndHideKey();
        WireDoorToExactKey();
        ConfigureKeyGrabComfortAndGuidance();
        RefreshSequentialClueProps();
    }

    void OnDestroy()
    {
        RestoreLightingAfterClockFocus();
        UnsubscribeKeyGrabGuidance();
        if (_keyGuideSpot != null)
            Destroy(_keyGuideSpot.gameObject);
        if (_instance == this) _instance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ── Scene reload reset ────────────────────────────────────────────────────
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RestoreLightingAfterClockFocus();
        _solvedClues.Clear();
        _seenDecoyIds.Clear();
        _decoyInteractions = 0;
        _decoyGramophoneUnlocked = false;
        _decoyTelescopeUnlocked = false;
        _puzzleComplete = false;
        if (_notifPanel != null) _notifPanel.SetActive(false);

        // Disable the reveal light when scene reloads
        if (revealLight != null)
            revealLight.enabled = false;
        SetDoorGuidanceEnabled(false);

        // Wait one frame so all scene objects exist before searching for the key
        StartCoroutine(HideKeyNextFrame());
    }

    /// <summary>
    /// Entry point used by clue scripts. Returns true only for the first time a clue index is accepted.
    /// </summary>
    public static bool TryHandleClueSolved(int clueIndex, Object source = null)
    {
        if (_instance == null)
        {
            Debug.LogWarning($"[PuzzleManager] Ignored clue {clueIndex}: no PuzzleManager in scene.");
            return false;
        }
        return _instance.HandleClueSolved(clueIndex, source);
    }

    public static bool IsClueUnlocked(int clueIndex)
    {
        if (_instance == null) return true;
        return clueIndex switch
        {
            <= 1 => true,
            2 => _instance._solvedClues.Contains(1),
            3 => _instance._solvedClues.Contains(1) && _instance._solvedClues.Contains(2),
            _ => true
        };
    }

    public static bool CanUseSofa()
    {
        return IsClueUnlocked(2);
    }

    public static bool IsDecoyPathUnlocked(string pathId)
    {
        if (_instance == null || string.IsNullOrEmpty(pathId)) return true;
        return pathId switch
        {
            "gramophone" => _instance._decoyGramophoneUnlocked,
            "telescope" => _instance._decoyTelescopeUnlocked,
            _ => true
        };
    }

    public static void ReportDecoyInteraction(string decoyId, Object source = null)
    {
        if (_instance == null || string.IsNullOrEmpty(decoyId)) return;
        _instance.HandleDecoyInteraction(decoyId, source);
    }

    public static void UnlockDecoyPath(string pathId, Object source = null)
    {
        if (_instance == null || string.IsNullOrEmpty(pathId)) return;
        _instance.HandleDecoyPathUnlock(pathId, source);
    }

    public static void ShowOutOfTurnFeedback(string bodyText)
    {
        if (_instance == null || string.IsNullOrEmpty(bodyText)) return;
        _instance.ShowOutOfTurnFeedbackInternal(bodyText);
    }

    void ShowOutOfTurnFeedbackInternal(string bodyText)
    {
        if (Time.unscaledTime < _nextOutOfTurnToastTime) return;
        _nextOutOfTurnToastTime = Time.unscaledTime + 1.2f;
        ShowNotification("NOT YET", bodyText, AccentBlue, autoDismissSeconds: 1.8f);
    }

    void HandleDecoyInteraction(string decoyId, Object source)
    {
        if (!_seenDecoyIds.Add(decoyId)) return;
        _decoyInteractions++;

        if (decoyId == "decoy_note")
            _decoyGramophoneUnlocked = true;
        if (decoyId == "decoy_globe")
            _decoyTelescopeUnlocked = true;

        StartCoroutine(ShowDecoyNotificationDelayed(decoyId, 2f));

        Debug.Log($"[PuzzleManager] Decoy hit '{decoyId}' from '{(source != null ? source.name : "unknown")}'.");
    }

    IEnumerator ShowDecoyNotificationDelayed(string decoyId, float delaySeconds)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
        ShowNotification(
            "DECOY CLUE",
            GetDecoyRecoveryMessage(decoyId),
            AccentGold, autoDismissSeconds: 4.5f);
    }

    void HandleDecoyPathUnlock(string pathId, Object source)
    {
        bool changed = false;
        switch (pathId)
        {
            case "gramophone":
                if (!_decoyGramophoneUnlocked) { _decoyGramophoneUnlocked = true; changed = true; }
                break;
            case "telescope":
                if (!_decoyTelescopeUnlocked) { _decoyTelescopeUnlocked = true; changed = true; }
                break;
        }

        if (changed)
            Debug.Log($"[PuzzleManager] Decoy path unlocked '{pathId}' from '{(source != null ? source.name : "unknown")}'.");
    }

    static string GetDecoyRecoveryMessage(string decoyId)
    {
        _ = decoyId;
        return "Dead end. Go back to your previous confirmed clue and try the other nearby object.";
    }

    IEnumerator HideKeyNextFrame()
    {
        yield return null;
        FindAndHideKey();
        WireDoorToExactKey();
        RefreshSequentialClueProps();
    }

    /// <summary>Only clue 1 is available at start; clue 2 after 1 solved; clue 3 after 2 solved.</summary>
    void RefreshSequentialClueProps()
    {
        bool clue1Solved = _solvedClues.Contains(1);
        bool clue2Solved = _solvedClues.Contains(2);

        // Real clue visibility is strictly sequential.
        foreach (var c in Object.FindObjectsByType<ClueNote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.clueIndex == 1)
                c.gameObject.SetActive(true);
        }

        foreach (var p in Object.FindObjectsByType<ProximityClueNote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (p.clueIndex == 2)
                p.gameObject.SetActive(clue1Solved);
        }

        foreach (var g in Object.FindObjectsByType<GlobeClueReveal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.clueIndex == 3)
                g.gameObject.SetActive(clue1Solved && clue2Solved);
        }
    }

    // ── Key management ────────────────────────────────────────────────────────
    void FindAndHideKey()
    {
        UnsubscribeKeyGrabGuidance();
        SetDoorGuidanceEnabled(false);
        SetKeyGuideLightEnabled(false);
        if (keyObject != null)
        {
            keyObject.SetActive(false);
            Debug.Log($"[PuzzleManager] Key '{keyObject.name}' hidden.");
        }
        else
        {
            Debug.LogWarning("[PuzzleManager] Assign the key GameObject to the 'Key Object' field.");
        }
    }

    void RevealKey()
    {
        if (keyObject != null)
        {
            NotifyKeyRevealedFromClock(keyObject);
            Debug.Log("[PuzzleManager] Key revealed!");
        }
        else
        {
            Debug.LogWarning("[PuzzleManager] Cannot reveal key because 'Key Object' is not assigned.");
        }

        // Enable the reveal light (defensive)
        if (revealLight != null)
        {
            Debug.Log($"[PuzzleManager] RevealLight BEFORE: name={revealLight.name}, activeInHierarchy={revealLight.gameObject.activeInHierarchy}, enabled={revealLight.enabled}, intensity={revealLight.intensity}");
            if (!revealLight.gameObject.activeInHierarchy)
            {
                revealLight.gameObject.SetActive(true);
                Debug.Log("[PuzzleManager] RevealLight GameObject was inactive; activated.");
            }
            // Ensure component enabled and visible
            revealLight.enabled = true;
            if (revealLight.intensity <= 0f)
            {
                revealLight.intensity = 1f;
                Debug.Log("[PuzzleManager] RevealLight intensity was 0; set to 1.");
            }
            Debug.Log($"[PuzzleManager] RevealLight AFTER: activeInHierarchy={revealLight.gameObject.activeInHierarchy}, enabled={revealLight.enabled}, intensity={revealLight.intensity}");
            Debug.Log("[PuzzleManager] Reveal light enabled!");
        }
    }

    void WireDoorToExactKey()
    {
        if (keyObject == null) return;
        if (doorHinge == null)
            doorHinge = Object.FindFirstObjectByType<DoorProximityHinge>();
        if (doorHinge != null)
            doorHinge.SetKeyObject(keyObject);
    }

    /// <summary>
    /// Called by clock reveal flow so key orientation + pickup guidance are always configured.
    /// </summary>
    public void NotifyKeyRevealedFromClock(GameObject revealedKey = null)
    {
        if (revealedKey != null)
            keyObject = revealedKey;

        if (keyObject == null)
            return;

        keyObject.SetActive(true);
        WireDoorToExactKey();
        ConfigureKeyGrabComfortAndGuidance();
        SetDoorGuidanceEnabled(true);
        if (_keyGrabInteractable != null && _keyGrabInteractable.isSelected)
            OnKeyPickedUp(null);
    }

    void ConfigureKeyGrabComfortAndGuidance()
    {
        if (keyObject == null) return;

        _keyGrabInteractable = keyObject.GetComponentInChildren<XRGrabInteractable>(true);
        if (_keyGrabInteractable == null) return;

        var attach = _keyGrabInteractable.transform.Find("KeyGrabAttach");
        if (attach == null)
        {
            var go = new GameObject("KeyGrabAttach");
            go.transform.SetParent(_keyGrabInteractable.transform, false);
            attach = go.transform;
        }
        attach.localPosition = keyGrabAttachLocalOffset;
        attach.localRotation = Quaternion.Euler(keyGrabAttachLocalEuler);
        _keyGrabInteractable.attachTransform = attach;

        UnsubscribeKeyGrabGuidance();
        _keyGrabInteractable.selectEntered.AddListener(OnKeyPickedUp);
    }

    void UnsubscribeKeyGrabGuidance()
    {
        if (_keyGrabInteractable != null)
            _keyGrabInteractable.selectEntered.RemoveListener(OnKeyPickedUp);
        _keyGrabInteractable = null;
    }

    void OnKeyPickedUp(SelectEnterEventArgs _)
    {
        RestoreLightingAfterClockFocus();
        SetDoorGuidanceEnabled(true);
        SetKeyGuideLightEnabled(true);
        GameAudioFeedback.PlayKeyAcquired();
        ShowNotification(
            "QUEST UPDATED",
            "Move to the pulsating hinge light and use the key to complete your escape.",
            AccentGreen, autoDismissSeconds: 5f);
    }

    void SetDoorGuidanceEnabled(bool on)
    {
        _doorGuidanceActive = on;
        if (!on)
        {
            if (_doorGuideSpot != null) _doorGuideSpot.enabled = false;
            if (_doorGuideHingeFill != null) _doorGuideHingeFill.enabled = false;
            if (_doorGuideHingePulseSpot != null) _doorGuideHingePulseSpot.enabled = false;
            return;
        }

        if (doorHinge == null)
            doorHinge = Object.FindFirstObjectByType<DoorProximityHinge>();
        if (doorHinge == null) return;

        if (!TryResolveDoorGuideTarget(out var target))
            return;
        _doorGuideTargetWorld = target;
        _hasDoorGuideTarget = true;

        if (_doorGuideSpot == null)
        {
            var go = new GameObject("DoorGuideSpot");
            _doorGuideSpot = go.AddComponent<Light>();
            _doorGuideSpot.type = LightType.Spot;
            _doorGuideSpot.shadows = LightShadows.None;
        }

        _doorGuideSpot.color = doorGuideColor;
        _doorGuideSpot.intensity = doorGuideSpotIntensity;
        _doorGuideSpot.range = doorGuideSpotRange;
        _doorGuideSpot.spotAngle = doorGuideSpotAngle;
        _doorGuideSpot.transform.position = _doorGuideTargetWorld + doorGuideSpotOffset;
        Vector3 toTarget = _doorGuideTargetWorld - _doorGuideSpot.transform.position;
        if (toTarget.sqrMagnitude > 0.0001f)
            _doorGuideSpot.transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        _doorGuideSpot.enabled = true;

        if (_doorGuideHingeFill == null)
        {
            var go = new GameObject("DoorGuideHingeFill");
            _doorGuideHingeFill = go.AddComponent<Light>();
            _doorGuideHingeFill.type = LightType.Point;
            _doorGuideHingeFill.shadows = LightShadows.None;
        }

        _doorGuideHingeFill.color = doorGuideColor;
        _doorGuideHingeFill.range = 1.25f;
        _doorGuideHingeFill.intensity = doorGuideHingeFillIntensity;
        _doorGuideHingeFill.transform.position = _doorGuideTargetWorld + doorGuideFillOffset;
        _doorGuideHingeFill.enabled = true;

        if (_doorGuideHingePulseSpot == null)
        {
            var go = new GameObject("DoorGuideHingePulseSpot");
            _doorGuideHingePulseSpot = go.AddComponent<Light>();
            _doorGuideHingePulseSpot.type = LightType.Point;
            _doorGuideHingePulseSpot.shadows = LightShadows.None;
        }

        _doorGuideHingePulseSpot.color = doorGuideColor;
        _doorGuideHingePulseSpot.range = doorGuideHingePulseRange;
        _doorGuideHingePulseSpot.spotAngle = doorGuideHingePulseAngle;
        _doorGuideHingePulseSpot.intensity = doorGuideHingePulseIntensity;
        _doorGuideHingePulseSpot.transform.position = _doorGuideTargetWorld + doorGuideHingePulseOffset;
        _doorGuideHingePulseSpot.enabled = true;
    }

    // ── Clue handling ─────────────────────────────────────────────────────────
    bool HandleClueSolved(int clueIndex, Object source)
    {
        if (_puzzleComplete) return false;
        if (clueIndex <= 0)
        {
            Debug.LogWarning($"[PuzzleManager] Invalid clue index {clueIndex} from '{(source != null ? source.name : "unknown")}'.");
            return false;
        }

        if (!_solvedClues.Add(clueIndex))
        {
            Debug.Log($"[PuzzleManager] Ignored duplicate clue solve {clueIndex} from '{(source != null ? source.name : "unknown")}'.");
            return false;
        }

        ApplyClueSpecificChanges(clueIndex);

        // Count how many of the required clues are done
        int solved = Mathf.Min(_solvedClues.Count, RequiredClues.Length);

        RefreshSequentialClueProps();

        // Reveal key as soon as clue 3 (globe) is completed.
        // With sequential gating, clue 3 implies clue 1+2 are already done.
        if (clueIndex == 3 && keyObject != null && !keyObject.activeSelf)
        {
            ApplyClockFocusLighting();
            ShowNotification(
                "FINAL STEP",
                "Open the clock to reveal the key.",
                AccentGreen, autoDismissSeconds: 6f);
        }

        if (solved < RequiredClues.Length)
        {
            // Intermediate progress toast
            GameAudioFeedback.PlayClueComplete();
            ShowNotification(
                $"Clue {solved} / {RequiredClues.Length} Solved",
                GetProgressMessage(solved),
                AccentBlue, autoDismissSeconds: 4f);
            return true;
        }

        // All required clues solved
        _puzzleComplete = true;
        GameAudioFeedback.PlayClueComplete();
        ShowNotification(
            "ALL CLUES SOLVED!",
            "Find the key behind the clock and bring it to the door.",
            AccentGreen, autoDismissSeconds: 7f);
        
        OnPuzzleComplete?.Invoke();
        return true;
    }

    void ApplyClueSpecificChanges(int clueIndex)
    {
        // Keep clue-index specific logic centralized here as the puzzle evolves.
        switch (clueIndex)
        {
            case 1:
                break;
            case 2:
                break;
            case 3:
                break;
        }
    }

    static string GetProgressMessage(int solved)
    {
        return solved switch
        {
            1 => "Good start — keep searching.",
            2 => "Almost there — one clue remains.",
            _ => "Keep going..."
        };
    }

    void ApplyClockFocusLighting()
    {
        if (!enableClockFocusAfterClue3 || _clockFocusLightingActive)
            return;

        var clocks = FindClockTargets();
        if (clocks.Count == 0)
            return;

        _ambientSkyBeforeClockFocus = RenderSettings.ambientSkyColor;
        _ambientEqBeforeClockFocus = RenderSettings.ambientEquatorColor;
        _ambientGroundBeforeClockFocus = RenderSettings.ambientGroundColor;
        _ambientFlatBeforeClockFocus = RenderSettings.ambientLight;
        _ambientIntensityBeforeClockFocus = RenderSettings.ambientIntensity;
        _clockFocusLightingSnapshotReady = true;

        RenderSettings.ambientSkyColor = ScaleRgb(_ambientSkyBeforeClockFocus, clockFocusAmbientColorScale);
        RenderSettings.ambientEquatorColor = ScaleRgb(_ambientEqBeforeClockFocus, clockFocusAmbientColorScale);
        RenderSettings.ambientGroundColor = ScaleRgb(_ambientGroundBeforeClockFocus, clockFocusAmbientColorScale);
        RenderSettings.ambientLight = ScaleRgb(_ambientFlatBeforeClockFocus, clockFocusAmbientColorScale);
        RenderSettings.ambientIntensity = Mathf.Max(0f, _ambientIntensityBeforeClockFocus * clockFocusAmbientIntensityScale);

        _clockLightIntensityBeforeFocus.Clear();
        var allLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var L in allLights)
        {
            if (L == null) continue;
            if (!_clockLightIntensityBeforeFocus.ContainsKey(L))
                _clockLightIntensityBeforeFocus.Add(L, L.intensity);
            if (!IsClockLight(L.transform))
                L.intensity = Mathf.Max(0.0001f, L.intensity * clockFocusOtherLightIntensityScale);
        }

        for (int i = 0; i < clocks.Count; i++)
        {
            var target = clocks[i];
            var pos = GetClockFocusPoint(target);
            var go = new GameObject($"ClockFocusSpot_{i + 1}");
            var spot = go.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = clockFocusColor;
            spot.intensity = clockFocusSpotIntensity;
            spot.range = clockFocusSpotRange;
            spot.spotAngle = clockFocusSpotAngle;
            spot.shadows = LightShadows.None;
            go.transform.position = pos + clockFocusSpotOffset;
            var dir = pos - go.transform.position;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.down;
            go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _clockFocusSpots.Add(spot);
        }

        _clockFocusLightingActive = true;
    }

    void RestoreLightingAfterClockFocus()
    {
        if (!_clockFocusLightingActive && !_clockFocusLightingSnapshotReady && _clockFocusSpots.Count == 0)
            return;

        foreach (var kv in _clockLightIntensityBeforeFocus)
        {
            if (kv.Key != null)
                kv.Key.intensity = kv.Value;
        }
        _clockLightIntensityBeforeFocus.Clear();

        if (_clockFocusLightingSnapshotReady)
        {
            RenderSettings.ambientSkyColor = _ambientSkyBeforeClockFocus;
            RenderSettings.ambientEquatorColor = _ambientEqBeforeClockFocus;
            RenderSettings.ambientGroundColor = _ambientGroundBeforeClockFocus;
            RenderSettings.ambientLight = _ambientFlatBeforeClockFocus;
            RenderSettings.ambientIntensity = _ambientIntensityBeforeClockFocus;
        }

        for (int i = 0; i < _clockFocusSpots.Count; i++)
        {
            if (_clockFocusSpots[i] != null)
                Destroy(_clockFocusSpots[i].gameObject);
        }
        _clockFocusSpots.Clear();
        _clockFocusLightingSnapshotReady = false;
        _clockFocusLightingActive = false;
    }

    List<Transform> FindClockTargets()
    {
        var targets = new List<Transform>(8);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null) continue;
            var n = t.name.ToLowerInvariant();
            if (!n.Contains("clock")) continue;
            if (n.Contains("spot") || n.Contains("light")) continue;
            if (!IsTopmostClockTransform(t))
                continue;
            if (t.GetComponentInChildren<Renderer>(true) == null)
                continue;
            targets.Add(t);
        }
        return targets;
    }

    static bool IsTopmostClockTransform(Transform t)
    {
        if (t == null) return false;
        var p = t.parent;
        while (p != null)
        {
            if (p.name.ToLowerInvariant().Contains("clock"))
                return false;
            p = p.parent;
        }
        return true;
    }

    static bool IsClockLight(Transform lightTransform)
    {
        if (lightTransform == null) return false;
        var t = lightTransform;
        for (int i = 0; i < 6 && t != null; i++)
        {
            var n = t.name.ToLowerInvariant();
            if (n.Contains("clock")) return true;
            t = t.parent;
        }
        return false;
    }

    static Vector3 GetClockFocusPoint(Transform target)
    {
        if (target == null) return Vector3.zero;
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b.center;
        }
        return target.position;
    }

    static Color ScaleRgb(Color color, float scale)
    {
        return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
    }

    // ── Notification UI ───────────────────────────────────────────────────────
    void ShowNotification(string title, string body, Color accent, float autoDismissSeconds)
    {
        if (_notifPanel == null) return;

        _titleText.text  = title;
        _titleText.color = accent;
        _bodyText.text   = body;
        _accentBar.color = accent;

        _notifPanel.SetActive(true);
        PositionNotificationCanvas();

        if (debugNotifPlacement && Camera.main != null)
        {
            var p = _notifCanvas != null ? _notifCanvas.transform.position : Vector3.zero;
            Debug.Log(
                $"[PuzzleManager] Toast '{title}' camPos={Camera.main.transform.position} notifPos={p} " +
                $"fwd={notifForwardMeters} up={notifUpMeters} right={notifRightMeters}");
        }

        if (_dismissRoutine != null) StopCoroutine(_dismissRoutine);
        _dismissRoutine = StartCoroutine(AutoDismiss(autoDismissSeconds));
    }

    void PositionNotificationCanvas()
    {
        if (Camera.main == null || _notifCanvas == null) return;
        Transform cam = Camera.main.transform;
        _notifCanvas.transform.position =
            cam.position
            + cam.forward * notifForwardMeters
            + cam.right * notifRightMeters
            + cam.up * notifUpMeters;
        _notifCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
    }

    void LateUpdate()
    {
        UpdateKeyGuideLight();
        if (keyObject != null && keyObject.activeInHierarchy && !_doorGuidanceActive)
            SetDoorGuidanceEnabled(true);
        if (_keyGrabInteractable != null && _keyGrabInteractable.isSelected && !_doorGuidanceActive)
            SetDoorGuidanceEnabled(true);

        if (_doorGuidanceActive)
            UpdateDoorGuidancePulse();
        if (_notifPanel != null && _notifPanel.activeSelf)
            PositionNotificationCanvas();
    }

    void UpdateKeyGuideLight()
    {
        bool keyActive = keyObject != null && keyObject.activeInHierarchy;
        if (!keyActive)
        {
            SetKeyGuideLightEnabled(false);
            return;
        }

        SetKeyGuideLightEnabled(true);
        if (_keyGuideSpot == null) return;

        Vector3 target = keyObject.transform.position;
        Vector3 pos = target + keyGuideOffset;
        _keyGuideSpot.transform.position = pos;
        Vector3 toKey = target - pos;
        if (toKey.sqrMagnitude < 0.0001f) toKey = Vector3.down;
        _keyGuideSpot.transform.rotation = Quaternion.LookRotation(toKey.normalized, Vector3.up);
    }

    void SetKeyGuideLightEnabled(bool on)
    {
        if (!on)
        {
            if (_keyGuideSpot != null) _keyGuideSpot.enabled = false;
            return;
        }

        if (_keyGuideSpot == null)
        {
            var go = new GameObject("PuzzleKeyGuideSpot");
            _keyGuideSpot = go.AddComponent<Light>();
            _keyGuideSpot.type = LightType.Spot;
            _keyGuideSpot.shadows = LightShadows.None;
        }

        _keyGuideSpot.color = keyGuideColor;
        _keyGuideSpot.intensity = keyGuideIntensity;
        _keyGuideSpot.range = keyGuideRange;
        _keyGuideSpot.spotAngle = keyGuideAngle;
        _keyGuideSpot.enabled = true;
    }

    void UpdateDoorGuidancePulse()
    {
        if (!TryResolveDoorGuideTarget(out var target))
        {
            _hasDoorGuideTarget = false;
            return;
        }
        _doorGuideTargetWorld = target;
        _hasDoorGuideTarget = true;

        if (!_hasDoorGuideTarget) return;

        // Keep all guidance lights locked to knob target every frame.
        if (_doorGuideSpot != null)
        {
            _doorGuideSpot.transform.position = _doorGuideTargetWorld + doorGuideSpotOffset;
            Vector3 toTarget = _doorGuideTargetWorld - _doorGuideSpot.transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                _doorGuideSpot.transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        }
        if (_doorGuideHingeFill != null)
            _doorGuideHingeFill.transform.position = _doorGuideTargetWorld + doorGuideFillOffset;
        if (_doorGuideHingePulseSpot != null)
            _doorGuideHingePulseSpot.transform.position = _doorGuideTargetWorld + doorGuideHingePulseOffset;

        if (_doorGuideSpot != null && _doorGuideSpot.enabled)
        {
            _doorGuideSpot.intensity = doorGuideSpotIntensity;
        }
        if (_doorGuideHingeFill != null && _doorGuideHingeFill.enabled)
        {
            float pulse = 1f + Mathf.Sin(Time.time * (doorGuidePulseSpeed * 1.15f)) * (doorGuidePulseAmount * 0.7f);
            _doorGuideHingeFill.intensity = doorGuideHingeFillIntensity * pulse;
        }
        if (_doorGuideHingePulseSpot != null && _doorGuideHingePulseSpot.enabled)
        {
            float pulse = 0.55f + Mathf.Abs(Mathf.Sin(Time.time * (doorGuidePulseSpeed * 1.15f)));
            _doorGuideHingePulseSpot.intensity = doorGuideHingePulseIntensity * pulse;
        }
    }

    bool TryResolveDoorGuideTarget(out Vector3 target)
    {
        if (doorGuideTarget != null)
        {
            target = doorGuideTarget.position;
            return true;
        }

        if (doorHinge == null)
            doorHinge = Object.FindFirstObjectByType<DoorProximityHinge>();
        if (doorHinge == null)
        {
            target = Vector3.zero;
            return false;
        }

        var knobCollider = doorHinge.GetComponent<Collider>();
        if (knobCollider != null)
        {
            target = knobCollider.bounds.center;
            return true;
        }

        var hinge = doorHinge.HingePivotTransform;
        if (hinge != null)
        {
            target = hinge.position + new Vector3(0f, 0.95f, -0.55f);
            return true;
        }

        target = Vector3.zero;
        return false;
    }

    IEnumerator AutoDismiss(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_notifPanel != null) _notifPanel.SetActive(false);
    }

    void BuildNotificationUI()
    {
        var canvasObj = new GameObject("PuzzleNotifCanvas");
        canvasObj.transform.SetParent(transform);

        _notifCanvas = canvasObj.AddComponent<Canvas>();
        _notifCanvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        var crt = canvasObj.GetComponent<RectTransform>();
        crt.sizeDelta  = new Vector2(0.80f, 0.26f);
        crt.localScale = Vector3.one;

        // Card background
        _notifPanel = new GameObject("NotifPanel");
        _notifPanel.transform.SetParent(canvasObj.transform, false);
        _notifPanel.AddComponent<Image>().color = BgDark;
        var prt = _notifPanel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta        = new Vector2(0.70f, 0.20f);
        prt.anchoredPosition = Vector2.zero;

        // Accent bar — top edge
        var barObj = new GameObject("AccentBar");
        barObj.transform.SetParent(_notifPanel.transform, false);
        _accentBar       = barObj.AddComponent<Image>();
        _accentBar.color = AccentGold;
        var brt = barObj.GetComponent<RectTransform>();
        brt.anchorMin        = new Vector2(0f, 1f);
        brt.anchorMax        = new Vector2(1f, 1f);
        brt.pivot            = new Vector2(0.5f, 1f);
        brt.sizeDelta        = new Vector2(0f, 0.010f);
        brt.anchoredPosition = Vector2.zero;

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(_notifPanel.transform, false);
        _titleText                 = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text            = "";
        _titleText.fontSize        = 0.048f;
        _titleText.fontStyle       = MenuThemes.Typography.Header;
        _titleText.color           = AccentGold;
        _titleText.alignment       = TextAlignmentOptions.Center;
        _titleText.textWrappingMode = TextWrappingModes.NoWrap;
        var trt = titleObj.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta        = new Vector2(0.64f, 0.070f);
        trt.anchoredPosition = new Vector2(0f, 0.040f);

        // Body
        var bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(_notifPanel.transform, false);
        _bodyText                 = bodyObj.AddComponent<TextMeshProUGUI>();
        _bodyText.text            = "";
        _bodyText.fontSize        = 0.030f;
        _bodyText.color           = TextPrimary;
        _bodyText.alignment       = TextAlignmentOptions.Center;
        _bodyText.fontStyle       = MenuThemes.Typography.Body;
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        var bort = bodyObj.GetComponent<RectTransform>();
        bort.anchorMin = bort.anchorMax = bort.pivot = new Vector2(0.5f, 0.5f);
        bort.sizeDelta        = new Vector2(0.64f, 0.090f);
        bort.anchoredPosition = new Vector2(0f, -0.028f);

        _notifPanel.SetActive(false);
    }
}
