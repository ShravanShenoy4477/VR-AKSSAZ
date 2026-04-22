using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Singleton that wires together the full puzzle chain.
///
/// Responsibilities:
///   - Hides the key at game start (finds it by name or tag "Key").
///   - Listens to OnClueSolved from ClueNote, ProximityClueNote, GlobeClueReveal.
///   - Shows a "Clue X/3 Solved" progress toast after each real clue.
///   - Reveals the key and shows "KEY REVEALED!" when all 3 clues are solved.
///   - Resets correctly when the scene is reloaded (Play Again).
///
/// No scene setup needed — bootstraps itself at runtime.
/// </summary>
[DisallowMultipleComponent]
public class PuzzleManager : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────────────────
    /// <summary>Clue indices that must all be solved before the key is revealed.</summary>
    private static readonly int[] RequiredClues = { 1, 2, 3 };

    [Tooltip("Exact name of the key GameObject. Falls back to tag 'Key' if not found.")]
    public string keyObjectName = "TableProp_Keys";

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired when all required clues are solved and the key is revealed.</summary>
    public static event System.Action OnPuzzleComplete;

    /// <summary>Number of required clues the player has solved so far.</summary>
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
    private GameObject _keyObject;

    // Notification HUD
    private Canvas          _notifCanvas;
    private GameObject      _notifPanel;
    private Image           _accentBar;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _bodyText;
    private Coroutine       _dismissRoutine;

    // ── Bootstrap ─────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        // Prevent duplicates on Play Again (scene reload)
        if (FindObjectOfType<PuzzleManager>() != null) return;

        var go = new GameObject("[PuzzleManager]");
        DontDestroyOnLoad(go);
        go.AddComponent<PuzzleManager>();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        _instance = this;
        ClueNote.OnClueSolved          += HandleClueSolved;
        ProximityClueNote.OnClueSolved += HandleClueSolved;
        GlobeClueReveal.OnClueSolved   += HandleClueSolved;

        SceneManager.sceneLoaded += OnSceneLoaded;

        BuildNotificationUI();
    }

    void Start()
    {
        FindAndHideKey();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        ClueNote.OnClueSolved          -= HandleClueSolved;
        ProximityClueNote.OnClueSolved -= HandleClueSolved;
        GlobeClueReveal.OnClueSolved   -= HandleClueSolved;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ── Scene reload reset ────────────────────────────────────────────────────
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _solvedClues.Clear();
        _puzzleComplete = false;
        _keyObject      = null;
        if (_notifPanel != null) _notifPanel.SetActive(false);

        // Wait one frame so all scene objects exist before searching for the key
        StartCoroutine(HideKeyNextFrame());
    }

    IEnumerator HideKeyNextFrame()
    {
        yield return null;
        FindAndHideKey();
    }

    // ── Key management ────────────────────────────────────────────────────────
    void FindAndHideKey()
    {
        _keyObject = GameObject.Find(keyObjectName);
        if (_keyObject == null)
            _keyObject = GameObject.FindWithTag("Key");

        if (_keyObject != null)
        {
            _keyObject.SetActive(false);
            Debug.Log($"[PuzzleManager] Key '{_keyObject.name}' hidden.");
        }
        else
        {
            Debug.LogWarning($"[PuzzleManager] Key '{keyObjectName}' not found. " +
                             "Name it exactly or add the 'Key' tag.");
        }
    }

    void RevealKey()
    {
        // Re-search in case scene reloaded
        if (_keyObject == null)
        {
            _keyObject = GameObject.Find(keyObjectName);
            if (_keyObject == null) _keyObject = GameObject.FindWithTag("Key");
        }

        if (_keyObject != null)
        {
            _keyObject.SetActive(true);
            Debug.Log("[PuzzleManager] Key revealed!");
        }
    }

    // ── Clue handling ─────────────────────────────────────────────────────────
    void HandleClueSolved(int clueIndex)
    {
        if (_puzzleComplete) return;

        _solvedClues.Add(clueIndex);

        // Count how many of the required clues are done
        int solved = 0;
        foreach (int req in RequiredClues)
            if (_solvedClues.Contains(req)) solved++;

        if (solved < RequiredClues.Length)
        {
            // Intermediate progress toast
            ShowNotification(
                $"Clue {solved} / {RequiredClues.Length} Solved",
                GetProgressMessage(solved),
                AccentBlue, autoDismissSeconds: 4f);
            return;
        }

        // All required clues solved
        _puzzleComplete = true;
        RevealKey();
        ShowNotification(
            "KEY REVEALED!",
            "All clues solved.\nFind the key and escape!",
            AccentGreen, autoDismissSeconds: 7f);
        OnPuzzleComplete?.Invoke();
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
        PositionInFrontOfCamera(0.9f, -0.22f);

        if (_dismissRoutine != null) StopCoroutine(_dismissRoutine);
        _dismissRoutine = StartCoroutine(AutoDismiss(autoDismissSeconds));
    }

    void PositionInFrontOfCamera(float distance, float verticalOffset)
    {
        if (Camera.main == null) return;
        Transform cam = Camera.main.transform;
        _notifCanvas.transform.position =
            cam.position + cam.forward * distance + Vector3.up * verticalOffset;
        _notifCanvas.transform.rotation =
            Quaternion.Euler(0f, cam.rotation.eulerAngles.y, 0f);
    }

    void LateUpdate()
    {
        // Keep the notification facing the player while it is visible
        if (_notifPanel != null && _notifPanel.activeSelf)
            PositionInFrontOfCamera(0.9f, -0.22f);
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
        _titleText.enableWordWrapping = false;
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
        _bodyText.enableWordWrapping = true;
        var bort = bodyObj.GetComponent<RectTransform>();
        bort.anchorMin = bort.anchorMax = bort.pivot = new Vector2(0.5f, 0.5f);
        bort.sizeDelta        = new Vector2(0.64f, 0.090f);
        bort.anchoredPosition = new Vector2(0f, -0.028f);

        _notifPanel.SetActive(false);
    }
}
