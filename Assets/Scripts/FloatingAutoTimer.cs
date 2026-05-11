using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// World-space HUD: timer, score, Hint 1 / Hint 2. Starts hidden; <b>left Y</b> toggles (H in Editor).
/// When shown, it anchors once in front of the headset and then stays fixed in world space.
/// </summary>
public class FloatingAutoTimer : MonoBehaviour
{
    static readonly Color BgDark       = MenuThemes.Hud.Background;
    static readonly Color AccentPurple = MenuThemes.Hud.AccentPrimary;
    static readonly Color AccentTeal   = MenuThemes.Hud.AccentSecondary;
    static readonly Color GoldTimer    = MenuThemes.Hud.TimerGold;

    private static FloatingAutoTimer _instance;

    private Canvas            canvas;
    private GameObject        menuPanel;
    private TextMeshProUGUI   timeText;
    private TextMeshProUGUI   hintDisplayText;
    private TextMeshProUGUI   scoreLineText;
    private Button            hint1Button;
    private Button            hint2Button;
    private bool              timerIsRunning  = true;
    private float             timeRemaining   = 600f;
    private bool              _timerExpired   = false;
    private int               _hintsUsed      = 0;
    private float             _initialTimeSeconds;

    bool _hudPanelVisible;
    bool _prevLeftSecondary;

    [Header("HUD placement (camera-relative)")]
    [SerializeField] float hudForwardMeters = 0.48f;
    [SerializeField] float hudRightMeters = 0.12f;
    [SerializeField] float hudUpMeters = -0.04f;
    [Tooltip("Log when HUD is shown (paste Console if it clips).")]
    [SerializeField] bool debugHudPlacement;

    public static event System.Action OnTimerExpired;

    public static float TimeRemaining => _instance != null ? _instance.timeRemaining : 0f;

    public static int HintsUsed => _instance != null ? _instance._hintsUsed : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (FindFirstObjectByType<FloatingAutoTimer>() != null) return;
        var obj = new GameObject("[FloatingMenu]");
        DontDestroyOnLoad(obj);
        obj.AddComponent<FloatingAutoTimer>();
    }

    void Awake()
    {
        _instance = this;
        _initialTimeSeconds = timeRemaining;
        _hudPanelVisible    = false;
        EnsureEventSystem();
        BuildUI();
        ApplyHudPanelVisibility();

        DoorProximityHinge.OnDoorOpened += OnGameWon;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        DoorProximityHinge.OnDoorOpened -= OnGameWon;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResetForRestart();
    }

    public void ResetForRestart()
    {
        timeRemaining  = _initialTimeSeconds;
        timerIsRunning = true;
        _timerExpired  = false;
        _hintsUsed     = 0;

        _hudPanelVisible = false;
        ApplyHudPanelVisibility();

        if (hintDisplayText != null)
        {
            hintDisplayText.text =
                "Hints: <b>grip</b> inside Hint 1 / Hint 2, or use the laser + trigger. Press <b>left Y</b> to show or hide this HUD (H in Editor).";
            hintDisplayText.color = MenuThemes.Hud.TextSecondary;
        }

        if (hint1Button != null)
            hint1Button.interactable = true;

        if (hint2Button != null)
        {
            hint2Button.interactable = false;
            ApplyLockedStyle(hint2Button);
        }
    }

    void OnGameWon()
    {
        timerIsRunning = false;
    }

    void LateUpdate()
    {
        if (!_hudPanelVisible || menuPanel == null || !menuPanel.activeSelf)
            return;
        RefreshScoreLine();
    }

    public void ForceHudVisible()
    {
        _hudPanelVisible = true;
        ApplyHudPanelVisibility();
    }

    void ApplyHudPanelVisibility()
    {
        if (menuPanel != null)
            menuPanel.SetActive(_hudPanelVisible);
        if (_hudPanelVisible)
            PositionHud();
    }

    void TryToggleHudVisibility()
    {
        bool pressedEdge = false;

        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid &&
            left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool y))
        {
            if (y && !_prevLeftSecondary)
                pressedEdge = true;
            _prevLeftSecondary = y;
        }
        else
            _prevLeftSecondary = false;

        if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
            pressedEdge = true;

        if (!pressedEdge) return;

        _hudPanelVisible = !_hudPanelVisible;
        ApplyHudPanelVisibility();
        if (debugHudPlacement && _hudPanelVisible && Camera.main != null && canvas != null)
        {
            var c = Camera.main.transform;
            Debug.Log(
                $"[FloatingAutoTimer] HUD visible: canvasPos={canvas.transform.position} camPos={c.position} " +
                $"fwd={hudForwardMeters} right={hudRightMeters} up={hudUpMeters}");
        }

        XrHaptics.PulseLeft(0.35f, 0.04f);
    }

    void PositionHud()
    {
        if (Camera.main == null || canvas == null) return;
        Transform cam = Camera.main.transform;
        canvas.transform.position =
            cam.position
            + cam.forward * hudForwardMeters
            + cam.right * hudRightMeters
            + cam.up * hudUpMeters;
        canvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
    }

    void RefreshScoreLine()
    {
        if (scoreLineText == null) return;
        int   clues = PuzzleManager.SolvedClueCount;
        float mins  = Mathf.Max(0f, timeRemaining / 60f);
        float score = Mathf.Max(0f, clues + mins - 0.25f * _hintsUsed);
        scoreLineText.text = $"Clues {Mathf.Min(clues, 99)}/3   Score≈{score:0.0}   Hints used:{_hintsUsed}";
    }

    void EnsureEventSystem()
    {
        var xrModule = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");

        if (EventSystem.current == null)
        {
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            if (xrModule != null)
                esObj.AddComponent(xrModule);
            else
                esObj.AddComponent<StandaloneInputModule>();
        }
        else if (xrModule != null && EventSystem.current.GetComponent(xrModule) == null)
        {
            EventSystem.current.gameObject.AddComponent(xrModule);
        }
    }

    void BuildUI()
    {
        var canvasObj = new GameObject("HUDCanvas");
        canvasObj.transform.SetParent(transform);
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        var xrRay = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRay != null) canvasObj.AddComponent(xrRay);

        var canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta  = new Vector2(0.55f, 0.40f);
        canvasRT.localScale = Vector3.one;

        menuPanel = MakePanel("MenuPanel", canvasObj.transform,
            new Vector2(0.50f, 0.34f), Vector2.zero, BgDark);
        MakeBorderGlow(menuPanel.transform, new Vector2(0.50f, 0.34f), AccentPurple);

        var clockObj = new GameObject("Clock");
        clockObj.transform.SetParent(menuPanel.transform, false);
        timeText = clockObj.AddComponent<TextMeshProUGUI>();
        timeText.text      = "10:00";
        timeText.fontSize  = 0.072f;
        timeText.fontStyle = FontStyles.Bold;
        timeText.alignment = TextAlignmentOptions.Center;
        timeText.color     = GoldTimer;
        var crt = clockObj.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.05f, 0.72f);
        crt.anchorMax = new Vector2(0.95f, 0.98f);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;

        var scoreObj = new GameObject("ScoreLine");
        scoreObj.transform.SetParent(menuPanel.transform, false);
        scoreLineText = scoreObj.AddComponent<TextMeshProUGUI>();
        scoreLineText.fontSize  = 0.028f;
        scoreLineText.alignment = TextAlignmentOptions.Center;
        scoreLineText.color     = new Color(MenuThemes.Hud.TextSecondary.r, MenuThemes.Hud.TextSecondary.g, MenuThemes.Hud.TextSecondary.b, 0.9f);
        var srt = scoreObj.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.05f, 0.58f);
        srt.anchorMax = new Vector2(0.95f, 0.70f);
        srt.offsetMin = Vector2.zero;
        srt.offsetMax = Vector2.zero;

        hint1Button = MakeRayButton("Hint1", "HINT 1", menuPanel.transform,
            new Vector2(-0.14f, 0.40f), 0.16f, 0.07f, AccentTeal);
        hint1Button.onClick.AddListener(OnHint1);
        WorldSpaceGripButton.Attach(hint1Button, OnHint1);

        hint2Button = MakeRayButton("Hint2", "HINT 2", menuPanel.transform,
            new Vector2(0.14f, 0.40f), 0.16f, 0.07f, AccentPurple);
        hint2Button.onClick.AddListener(OnHint2);
        WorldSpaceGripButton.Attach(hint2Button, OnHint2);
        ApplyLockedStyle(hint2Button);

        var hintBody = new GameObject("HintBody");
        hintBody.transform.SetParent(menuPanel.transform, false);
        hintDisplayText = hintBody.AddComponent<TextMeshProUGUI>();
        hintDisplayText.text =
            "Hints: move a controller into a hint row and <b>squeeze grip</b> briefly, or use the laser + trigger.\nPress <b>left Y</b> to show or hide this HUD (H in Editor). When shown, this HUD stays where you opened it.";
        hintDisplayText.fontSize  = 0.026f;
        hintDisplayText.alignment = TextAlignmentOptions.Center;
        hintDisplayText.color     = MenuThemes.Hud.TextSecondary;
        hintDisplayText.textWrappingMode = TextWrappingModes.Normal;
        hintDisplayText.richText  = true;
        var hrt = hintBody.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0.05f, 0.08f);
        hrt.anchorMax = new Vector2(0.95f, 0.52f);
        hrt.offsetMin = Vector2.zero;
        hrt.offsetMax = Vector2.zero;
    }

    void OnHint1()
    {
        if (!hint1Button.interactable) return;
        _hintsUsed++;
        hintDisplayText.text =
            "Start near the seating: pick up the book you can grab. After clue 1, touch anywhere on the big sofa with a hand and press grip once to slide it sideways and reach the next book.";
        hintDisplayText.color = Color.white;
        hint1Button.interactable = false;
        hint2Button.interactable = true;
        SetButtonColors(hint2Button, AccentPurple);
        var l = hint2Button.GetComponentInChildren<TextMeshProUGUI>();
        if (l != null) l.color = Color.white;
        XrHaptics.PulseRight(0.45f, 0.06f);
    }

    void OnHint2()
    {
        if (!hint2Button.interactable) return;
        _hintsUsed++;
        hintDisplayText.text =
            "Later: search around the larger couches, then the corner globe. Keys unlock the door when you bring them to the knob area.";
        hint2Button.interactable = false;
        XrHaptics.PulseRight(0.45f, 0.06f);
    }

    static GameObject MakePanel(string name, Transform parent,
        Vector2 size, Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<Image>().color = color;
        var rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
        return obj;
    }

    static void MakeBorderGlow(Transform parent, Vector2 innerSize, Color color)
    {
        const float b = 0.012f;
        var obj = new GameObject("BorderGlow");
        obj.transform.SetParent(parent, false);
        obj.transform.SetAsFirstSibling();
        obj.AddComponent<Image>().color = color;
        var rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta        = innerSize + new Vector2(b, b);
        rt.anchoredPosition = Vector2.zero;
    }

    static Button MakeRayButton(string name, string label, Transform parent,
        Vector2 pos, float w, float h, Color color)
    {
        var btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        var img = btnObj.AddComponent<Image>();
        img.color = color;
        var btn = btnObj.AddComponent<Button>();
        SetButtonColors(btn, color);

        var rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = pos;

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(btnObj.transform, false);
        var tmp       = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.fontSize  = h * 0.38f;
        tmp.color     = Color.white;
        var trt       = textObj.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;

        return btn;
    }

    static void SetButtonColors(Button btn, Color normal)
    {
        var cb              = btn.colors;
        cb.normalColor      = normal;
        cb.highlightedColor = Color.Lerp(normal, Color.white, 0.30f);
        cb.pressedColor     = Color.Lerp(normal, Color.black, 0.30f);
        cb.selectedColor    = cb.highlightedColor;
        cb.disabledColor    = new Color(normal.r * 0.35f, normal.g * 0.35f,
                                        normal.b * 0.35f, 0.45f);
        btn.colors = cb;
    }

    static void ApplyLockedStyle(Button btn)
    {
        SetButtonColors(btn, new Color(0.22f, 0.22f, 0.28f, 1f));
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = new Color(1f, 1f, 1f, 0.35f);
    }

    void Update()
    {
        TryToggleHudVisibility();

        if (timerIsRunning)
        {
            timeRemaining = Mathf.Max(0f, timeRemaining - Time.deltaTime);
            if (timeRemaining <= 0f)
            {
                timerIsRunning = false;
                if (!_timerExpired)
                {
                    _timerExpired = true;
                    OnTimesUp();
                }
            }
        }

        if (timeText != null && _hudPanelVisible && menuPanel != null && menuPanel.activeSelf)
        {
            int m = Mathf.FloorToInt(timeRemaining / 60);
            int s = Mathf.FloorToInt(timeRemaining % 60);
            timeText.text = $"{m:00}:{s:00}";

            timeText.color = timeRemaining <= 60f
                ? Color.Lerp(GoldTimer, new Color(1f, 0.25f, 0.25f), (60f - timeRemaining) / 60f)
                : GoldTimer;
        }
    }

    void OnTimesUp()
    {
        if (timeText != null)
        {
            timeText.text  = "00:00";
            timeText.color = new Color(1f, 0.25f, 0.25f, 1f);
        }

        OnTimerExpired?.Invoke();
    }
}
