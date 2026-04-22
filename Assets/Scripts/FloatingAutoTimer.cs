using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class FloatingAutoTimer : MonoBehaviour
{
    // ── Colours ──────────────────────────────────────────────────────────────
    static readonly Color BgDark      = new Color(0.07f, 0.08f, 0.13f, 0.97f);
    static readonly Color HeaderColor  = new Color(0.11f, 0.08f, 0.28f, 1.00f);
    static readonly Color HintAreaBg   = new Color(0.04f, 0.08f, 0.18f, 1.00f);
    static readonly Color AccentPurple = new Color(0.50f, 0.25f, 0.90f, 1.00f);
    static readonly Color AccentTeal   = new Color(0.10f, 0.62f, 0.68f, 1.00f);
    static readonly Color AccentRed    = new Color(0.72f, 0.18f, 0.18f, 1.00f);
    static readonly Color TextPrimary  = new Color(0.94f, 0.94f, 1.00f, 1.00f);
    static readonly Color TextDim      = new Color(0.50f, 0.70f, 1.00f, 0.65f);
    static readonly Color GoldTimer    = new Color(1.00f, 0.84f, 0.18f, 1.00f);

    // ── State ────────────────────────────────────────────────────────────────
    private static FloatingAutoTimer _instance;

    private Canvas            canvas;
    private GameObject        menuPanel;
    private TextMeshProUGUI   timeText;
    private TextMeshProUGUI   hintDisplayText;
    private Button            hint1Button;
    private Button            hint2Button;
    private bool              timerIsRunning  = true;
    private float             timeRemaining   = 600f;
    private bool              isMenuOpen      = false;
    private bool              _timerExpired   = false;
    private int               _hintsUsed      = 0;
    private InputAction       toggleAction;

    /// <summary>Fired once when the 10-minute timer reaches zero.</summary>
    public static event System.Action OnTimerExpired;

    /// <summary>Seconds remaining on the timer (read by GameCompletionUI for score).</summary>
    public static float TimeRemaining => _instance != null ? _instance.timeRemaining : 0f;

    /// <summary>How many hint buttons the player has pressed.</summary>
    public static int HintsUsed => _instance != null ? _instance._hintsUsed : 0;

    // ── Bootstrap ────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        var obj = new GameObject("[FloatingMenu]");
        DontDestroyOnLoad(obj);
        obj.AddComponent<FloatingAutoTimer>();
    }

    private void Awake()
    {
        _instance = this;
        EnsureEventSystem();
        BuildUI();
        SetupInput();

        // Stop the timer when the door is opened (game won)
        DoorProximityHinge.OnDoorOpened += OnGameWon;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        toggleAction?.Disable();
        toggleAction?.Dispose();
        DoorProximityHinge.OnDoorOpened -= OnGameWon;
    }

    private void OnGameWon()
    {
        timerIsRunning = false;
    }

    // ── Input ────────────────────────────────────────────────────────────────
    private void SetupInput()
    {
        toggleAction = new InputAction("ToggleMenu",
            binding: "<XRController>{LeftHand}/primaryButton");
        toggleAction.AddBinding("<Keyboard>/x");
        toggleAction.performed += _ => ToggleMenu();
        toggleAction.Enable();
    }

    private void ToggleMenu()
    {
        isMenuOpen = !isMenuOpen;
        menuPanel.SetActive(isMenuOpen);

        if (isMenuOpen && Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            canvas.transform.position =
                cam.position + cam.forward * 0.85f + Vector3.down * 0.10f;
            canvas.transform.rotation =
                Quaternion.Euler(0f, cam.rotation.eulerAngles.y, 0f);
        }
    }

    // ── EventSystem ──────────────────────────────────────────────────────────
    private void EnsureEventSystem()
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

    // ── UI Construction ──────────────────────────────────────────────────────
    private void BuildUI()
    {
        // Canvas (WorldSpace, 1 unit = 1 metre) ─────────────────────────────
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
        canvasRT.sizeDelta  = new Vector2(1.6f, 1.0f);
        canvasRT.localScale = Vector3.one;

        // Panel card ─────────────────────────────────────────────────────────
        menuPanel = MakePanel("MenuPanel", canvasObj.transform,
            new Vector2(1.40f, 0.88f), Vector2.zero, BgDark);
        MakeBorderGlow(menuPanel.transform, new Vector2(1.40f, 0.88f), AccentPurple);
        menuPanel.SetActive(false);

        // Header ─────────────────────────────────────────────────────────────
        var header = MakePanel("Header", menuPanel.transform,
            new Vector2(1.40f, 0.16f), new Vector2(0f, 0.36f), HeaderColor);

        MakeLabel("Title", header.transform,
            "ESCAPE ROOM", 0.075f, FontStyles.Bold, TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 0f), new Vector2(0.55f, 1f),
            new Vector2(0.065f, 0f), new Vector2(0f, 0f));

        var clockObj = new GameObject("Clock");
        clockObj.transform.SetParent(header.transform, false);
        timeText = clockObj.AddComponent<TextMeshProUGUI>();
        timeText.text      = "10:00";
        timeText.fontSize  = 0.082f;
        timeText.fontStyle = FontStyles.Bold;
        timeText.alignment = TextAlignmentOptions.MidlineRight;
        timeText.color     = GoldTimer;
        var crt = clockObj.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.55f, 0f);
        crt.anchorMax = new Vector2(1.00f, 1f);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = new Vector2(-0.055f, 0f);

        // Hint area ──────────────────────────────────────────────────────────
        var hintArea = MakePanel("HintArea", menuPanel.transform,
            new Vector2(1.24f, 0.36f), new Vector2(0f, 0.09f), HintAreaBg);
        MakeBorderGlow(hintArea.transform, new Vector2(1.24f, 0.36f),
            new Color(0.30f, 0.45f, 0.70f, 0.40f));

        MakeLabel("HintLabel", hintArea.transform,
            "HINT", 0.05f, FontStyles.Bold, TextDim,
            TextAlignmentOptions.TopLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.055f, -0.52f), new Vector2(-0.055f, 0f));

        var hintTextObj = new GameObject("HintDisplayText");
        hintTextObj.transform.SetParent(hintArea.transform, false);
        hintDisplayText = hintTextObj.AddComponent<TextMeshProUGUI>();
        hintDisplayText.text               = "Press a hint button to reveal a clue.";
        hintDisplayText.fontSize           = 0.063f;
        hintDisplayText.alignment          = TextAlignmentOptions.Center;
        hintDisplayText.color              = new Color(0.70f, 0.88f, 1.00f, 1f);
        hintDisplayText.enableWordWrapping = true;
        var hrt = hintTextObj.GetComponent<RectTransform>();
        hrt.anchorMin = Vector2.zero;
        hrt.anchorMax = Vector2.one;
        hrt.offsetMin = new Vector2( 0.055f,  0.03f);
        hrt.offsetMax = new Vector2(-0.055f, -0.075f);

        // Button row ─────────────────────────────────────────────────────────
        float btnY = -0.335f;

        hint1Button = MakeButton("Hint1Button", "HINT 1",
            menuPanel.transform, new Vector2(-0.44f, btnY), 0.38f, 0.12f, AccentTeal);

        hint2Button = MakeButton("Hint2Button", "HINT 2",
            menuPanel.transform, new Vector2(0.00f, btnY), 0.38f, 0.12f,
            new Color(0.22f, 0.22f, 0.28f, 1f));
        hint2Button.interactable = false;
        ApplyLockedStyle(hint2Button);

        var closeBtn = MakeButton("CloseButton", "CLOSE",
            menuPanel.transform, new Vector2(0.44f, btnY), 0.38f, 0.12f, AccentRed);

        // Button logic ───────────────────────────────────────────────────────
        closeBtn.onClick.AddListener(() =>
        {
            isMenuOpen = false;
            menuPanel.SetActive(false);
        });

        hint1Button.onClick.AddListener(() =>
        {
            _hintsUsed++;
            hintDisplayText.text  = "Not all seats are equal. One hides more than cushions — check beneath the larger one.";
            hintDisplayText.color = new Color(0.70f, 0.88f, 1.00f, 1f);
            if (!hint2Button.interactable)
            {
                hint2Button.interactable = true;
                hint2Button.GetComponent<Image>().color = AccentTeal;
                SetButtonColors(hint2Button, AccentTeal);
                hint2Button.GetComponentInChildren<TextMeshProUGUI>().color = Color.white;
            }
        });

        hint2Button.onClick.AddListener(() =>
        {
            _hintsUsed++;
            hintDisplayText.text  = "The world at your fingertips knows what comes next. Find the globe in the corner and give it a spin.";
            hintDisplayText.color = new Color(0.70f, 0.88f, 1.00f, 1f);
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static GameObject MakePanel(string name, Transform parent,
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

    private static void MakeBorderGlow(Transform parent, Vector2 innerSize, Color color)
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

    private static void MakeLabel(string name, Transform parent,
        string text, float size, FontStyles style, Color color,
        TextAlignmentOptions align,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp       = obj.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color     = color;
        var rt        = obj.GetComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = offsetMin;
        rt.offsetMax  = offsetMax;
    }

    /// <summary>
    /// Creates a button with a BoxCollider trigger so VRHandPoker's
    /// OverlapSphere can detect it. No Rigidbody needed.
    /// </summary>
    private static Button MakeButton(string name, string label,
        Transform parent, Vector2 pos, float w, float h, Color color)
    {
        var btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        var img   = btnObj.AddComponent<Image>();
        img.color = color;
        var btn   = btnObj.AddComponent<Button>();
        SetButtonColors(btn, color);

        var rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = pos;

        // Text label
        var textObj = new GameObject("Label");
        textObj.transform.SetParent(btnObj.transform, false);
        var tmp       = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.fontSize  = h * 0.42f;
        tmp.color     = Color.white;
        var trt       = textObj.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;

        // BoxCollider trigger — detected by VRHandPoker.OverlapSphere
        // Depth (Z) is generous so a moving controller can't pass through
        var box       = btnObj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size      = new Vector3(w, h, 0.08f);

        return btn;
    }

    private static void SetButtonColors(Button btn, Color normal)
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

    private static void ApplyLockedStyle(Button btn)
    {
        SetButtonColors(btn, new Color(0.22f, 0.22f, 0.28f, 1f));
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = new Color(1f, 1f, 1f, 0.35f);
    }

    // ── Update ───────────────────────────────────────────────────────────────
    private void Update()
    {
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

        int m = Mathf.FloorToInt(timeRemaining / 60);
        int s = Mathf.FloorToInt(timeRemaining % 60);
        timeText.text = $"{m:00}:{s:00}";

        timeText.color = timeRemaining <= 60f
            ? Color.Lerp(GoldTimer, new Color(1f, 0.25f, 0.25f), (60f - timeRemaining) / 60f)
            : GoldTimer;
    }

    private void OnTimesUp()
    {
        // Lock the display at 00:00 in red; TimeUpUI handles the full-screen panel
        timeText.text  = "00:00";
        timeText.color = new Color(1f, 0.25f, 0.25f, 1f);
        if (isMenuOpen) menuPanel.SetActive(false);  // hide HUD so TimeUpUI is unobstructed

        OnTimerExpired?.Invoke();
    }
}
