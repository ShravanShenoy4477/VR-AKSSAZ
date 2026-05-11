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
    [SerializeField] bool enableDoorGuidanceOnKeyPickup = true;
    [SerializeField] Color doorGuideColor = new Color(1f, 0.88f, 0.45f, 1f);
    [SerializeField] float doorGuideSpotIntensity = 2.6f;
    [SerializeField] float doorGuideSpotRange = 4.6f;
    [SerializeField] float doorGuideSpotAngle = 34f;
    [SerializeField] Vector3 doorGuideSpotOffset = new Vector3(0f, 0.12f, 0.10f);
    [SerializeField] float doorGuideHingeFillIntensity = 1.1f;
    [SerializeField] float doorGuideHingePulseIntensity = 2.2f;
    [SerializeField] float doorGuideHingePulseRange = 1.8f;
    [SerializeField] float doorGuideHingePulseAngle = 28f;
    [SerializeField] Vector3 doorGuideHingePulseOffset = new Vector3(0f, 0f, 0f);
    [SerializeField] float doorGuidePulseSpeed = 3.8f;
    [SerializeField] float doorGuidePulseAmount = 0.25f;
    [Header("Key spotlight fallback")]
    [SerializeField] Color keyGuideColor = new Color(1f, 0.94f, 0.82f, 1f);
    [SerializeField] float keyGuideIntensity = 3.1f;
    [SerializeField] float keyGuideRange = 2.4f;
    [SerializeField] float keyGuideAngle = 46f;
    [SerializeField] Vector3 keyGuideOffset = new Vector3(0.22f, 0.30f, 0.20f);

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

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BgDark      = new Color(0.07f, 0.08f, 0.13f, 0.95f);
    static readonly Color AccentGold  = new Color(1.00f, 0.84f, 0.18f, 1.00f);
    static readonly Color AccentGreen = new Color(0.18f, 0.78f, 0.40f, 1.00f);
    static readonly Color AccentBlue  = new Color(0.25f, 0.55f, 0.95f, 1.00f);
    static readonly Color TextPrimary = new Color(0.94f, 0.94f, 1.00f, 1.00f);

    // ── Runtime ───────────────────────────────────────────────────────────────
    private static PuzzleManager _instance;

    private readonly HashSet<int> _solvedClues = new HashSet<int>();
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
    private Vector3 _doorGuideTargetWorld;
    private bool _hasDoorGuideTarget;
    private bool _doorGuidanceActive;

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
        UnsubscribeKeyGrabGuidance();
        if (_keyGuideSpot != null)
            Destroy(_keyGuideSpot.gameObject);
        if (_instance == this) _instance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ── Scene reload reset ────────────────────────────────────────────────────
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _solvedClues.Clear();
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
        foreach (var p in Object.FindObjectsByType<ProximityClueNote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (p.clueIndex == 2)
                p.gameObject.SetActive(_solvedClues.Contains(1));
        }

        foreach (var g in Object.FindObjectsByType<GlobeClueReveal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.clueIndex == 3)
                g.gameObject.SetActive(_solvedClues.Contains(1) && _solvedClues.Contains(2));
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
        SetDoorGuidanceEnabled(true);
        SetKeyGuideLightEnabled(true);
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
        _doorGuideSpot.transform.rotation = Quaternion.LookRotation((_doorGuideTargetWorld - _doorGuideSpot.transform.position).normalized, Vector3.up);
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
        _doorGuideHingeFill.transform.position = _doorGuideTargetWorld + new Vector3(0f, 0.10f, 0f);
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
            ShowNotification(
                "FINAL STEP",
                "Open the clock to reveal the key.",
                AccentGreen, autoDismissSeconds: 6f);
        }

        if (solved < RequiredClues.Length)
        {
            // Intermediate progress toast
            ShowNotification(
                $"Clue {solved} / {RequiredClues.Length} Solved",
                GetProgressMessage(solved),
                AccentBlue, autoDismissSeconds: 4f);
            return true;
        }

        // All required clues solved
        _puzzleComplete = true;
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
            _doorGuideSpot.transform.rotation =
                Quaternion.LookRotation((_doorGuideTargetWorld - _doorGuideSpot.transform.position).normalized, Vector3.up);
        }
        if (_doorGuideHingeFill != null)
            _doorGuideHingeFill.transform.position = _doorGuideTargetWorld + new Vector3(0f, 0.10f, 0f);
        if (_doorGuideHingePulseSpot != null)
            _doorGuideHingePulseSpot.transform.position = _doorGuideTargetWorld + doorGuideHingePulseOffset;

        if (_doorGuideSpot != null && _doorGuideSpot.enabled)
        {
            float pulse = 1f + Mathf.Sin(Time.time * doorGuidePulseSpeed) * doorGuidePulseAmount;
            _doorGuideSpot.intensity = doorGuideSpotIntensity * pulse;
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
        _titleText.fontStyle       = FontStyles.Bold;
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
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        var bort = bodyObj.GetComponent<RectTransform>();
        bort.anchorMin = bort.anchorMax = bort.pivot = new Vector2(0.5f, 0.5f);
        bort.sizeDelta        = new Vector2(0.64f, 0.090f);
        bort.anchoredPosition = new Vector2(0f, -0.028f);

        _notifPanel.SetActive(false);
    }
}
