using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// World-space onboarding / help card. Toggle with left <b>X</b> (primary). Editor: <b>O</b>.
/// Tell players to open this when they need controls, hints, and where to start (Sage statue).
/// Persists across loads; does not use PlayerPrefs.
/// </summary>
public sealed class VROnboardingOverlay : MonoBehaviour
{
    Canvas _canvas;
    GameObject _panel;
    bool _prevLeftPrimary;
    int _pageIndex;
    TextMeshProUGUI _titleText;
    TextMeshProUGUI _bodyText;
    TextMeshProUGUI _pageText;
    Button _nextButton;
    Button _prevButton;
    Button _closeButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Object.FindObjectsByType<VROnboardingOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
            return;
        var go = new GameObject("[VROnboardingOverlay]");
        DontDestroyOnLoad(go);
        go.AddComponent<VROnboardingOverlay>();
    }

    void Awake()
    {
        EnsureEventSystem();
        Build();
        if (_panel != null)
            _panel.SetActive(false);
    }

    void Update()
    {
        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid && left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool x))
        {
            if (x && !_prevLeftPrimary)
                TogglePanel();
            _prevLeftPrimary = x;
        }
        else
            _prevLeftPrimary = false;

        if (Keyboard.current != null && Keyboard.current.oKey.wasPressedThisFrame)
            TogglePanel();
    }

    void TogglePanel()
    {
        if (_panel == null) return;
        _panel.SetActive(!_panel.activeSelf);
        if (_panel.activeSelf)
        {
            PositionPanel();
            _pageIndex = 0;
            RefreshPage();
        }
        if (debugPlacement && _panel.activeSelf && Camera.main != null)
        {
            var c = Camera.main.transform;
            Debug.Log(
                $"[VROnboardingOverlay] Opened: canvasPos={_canvas.transform.position} camPos={c.position} " +
                $"fwd={panelForwardMeters} right={panelRightMeters} up={panelUpMeters}");
        }

        XrHaptics.PulseLeft(0.3f, 0.04f);
        GameAudioFeedback.PlayMenuSelect();
    }

    [Header("Panel placement (camera-relative)")]
    [SerializeField] float panelForwardMeters = 0.48f;
    [SerializeField] float panelRightMeters = 0.12f;
    [SerializeField] float panelUpMeters = 0.02f;
    [Tooltip("Log position when you open this card (paste Console if it clips).")]
    [SerializeField] bool debugPlacement;

    readonly (string title, string body)[] _pages = new[]
    {
        (
            "Core Controls",
            "<b>Left X</b>: open/close onboarding\n" +
            "<b>Left Y</b>: open/close timer + hints menu\n" +
            "<b>Move</b>: left thumbstick   <b>Turn</b>: right thumbstick (snap)\n" +
            "<b>Teleport</b>: aim at floor, then trigger\n\n" +
            "Hint menu also shows your remaining time and updates clue progress."
        ),
        (
            "Start at Sage Statue",
            "Your first action should be interacting with the <b>Sage statue</b>.\n" +
            "It activates guidance lighting and reveals important search zones.\n\n" +
            "If you feel lost later, return to Sage-lit regions before guessing random objects."
        ),
        (
            "Interaction Types",
            "<b>Select (trigger)</b> = reveal/mechanism actions\n" +
            "Examples: sliding seats, rotating clocks, globe spin.\n\n" +
            "<b>Grab (grip)</b> = object handling/read actions\n" +
            "Examples: picking up books/keys, reading clue notes, decoy branch checks."
        ),
        (
            "Feedback Language",
            "<b>Green mesh flash</b>: clue-related or valid path object\n" +
            "<b>Red mesh flash</b>: irrelevant object\n" +
            "<b>Yellow flash</b>: right object but wrong order\n\n" +
            "Use these colors as quick validation before spending time on an interaction."
        ),
        (
            "Puzzle Flow",
            "Clues unlock sequentially: clue 1 -> clue 2 -> clue 3.\n" +
            "Some clues are intentionally ambiguous and may branch into decoys.\n\n" +
            "If a branch feels wrong, backtrack to your last confirmed clue and test the nearest alternative."
        )
    };

    void PositionPanel()
    {
        if (_canvas == null || Camera.main == null) return;
        var cam = Camera.main.transform;
        _canvas.transform.position =
            cam.position
            + cam.forward * panelForwardMeters
            + cam.right * panelRightMeters
            + cam.up * panelUpMeters;
        _canvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem_Onboarding");
        es.AddComponent<EventSystem>();
        var xrModule = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
        if (xrModule != null) es.AddComponent(xrModule);
        else es.AddComponent<StandaloneInputModule>();
    }

    void Build()
    {
        var root = new GameObject("OnboardingCanvas");
        root.transform.SetParent(transform);
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        var xrRay = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRay != null) root.AddComponent(xrRay);

        var crt = root.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(0.60f, 0.54f);

        _panel = new GameObject("Panel");
        _panel.transform.SetParent(root.transform, false);
        var img = _panel.AddComponent<Image>();
        img.color = MenuThemes.Hud.Background;
        _panel.AddComponent<RectMask2D>();
        var prt = _panel.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(0.015f, 0.015f);
        prt.offsetMax = new Vector2(-0.015f, -0.015f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = Vector2.zero;

        var title = new GameObject("Title");
        title.transform.SetParent(_panel.transform, false);
        _titleText = title.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Onboarding";
        _titleText.fontSize = 0.044f;
        _titleText.fontStyle = MenuThemes.Typography.Header;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = MenuThemes.Hud.TextPrimary;
        _titleText.enableAutoSizing = true;
        _titleText.fontSizeMin = 0.028f;
        _titleText.fontSizeMax = 0.044f;
        _titleText.textWrappingMode = TextWrappingModes.Normal;
        _titleText.overflowMode = TextOverflowModes.Truncate;
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.04f, 0.88f);
        trt.anchorMax = new Vector2(0.96f, 0.98f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var body = new GameObject("Body");
        body.transform.SetParent(_panel.transform, false);
        _bodyText = body.AddComponent<TextMeshProUGUI>();
        _bodyText.fontSize = 0.023f;
        _bodyText.enableAutoSizing = true;
        _bodyText.fontSizeMin = 0.016f;
        _bodyText.fontSizeMax = 0.023f;
        _bodyText.alignment = TextAlignmentOptions.TopLeft;
        _bodyText.color = MenuThemes.Hud.TextSecondary;
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        _bodyText.overflowMode = TextOverflowModes.Truncate;
        _bodyText.lineSpacing = 0.2f;
        _bodyText.paragraphSpacing = 0.6f;
        _bodyText.margin = new Vector4(0.008f, 0.008f, 0.008f, 0.008f);
        _bodyText.richText = true;
        var brt = body.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.06f, 0.19f);
        brt.anchorMax = new Vector2(0.94f, 0.86f);
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        var footer = new GameObject("Footer");
        footer.transform.SetParent(_panel.transform, false);
        var frt = footer.AddComponent<RectTransform>();
        frt.anchorMin = new Vector2(0.06f, 0.05f);
        frt.anchorMax = new Vector2(0.94f, 0.17f);
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        _prevButton = CreateFooterButton(footer.transform, "Prev", new Vector2(-0.16f, 0f), OnPrevPage);
        _nextButton = CreateFooterButton(footer.transform, "Next", new Vector2(0.00f, 0f), OnNextPage);
        _closeButton = CreateFooterButton(footer.transform, "Close", new Vector2(0.16f, 0f), TogglePanel);

        var pageObj = new GameObject("PageIndicator");
        pageObj.transform.SetParent(footer.transform, false);
        _pageText = pageObj.AddComponent<TextMeshProUGUI>();
        _pageText.alignment = TextAlignmentOptions.Center;
        _pageText.fontSize = 0.019f;
        _pageText.color = MenuThemes.Hud.TextSecondary;
        var pr = pageObj.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.34f, -0.28f);
        pr.anchorMax = new Vector2(0.66f, -0.02f);
        pr.offsetMin = Vector2.zero;
        pr.offsetMax = Vector2.zero;

        RefreshPage();
    }

    Button CreateFooterButton(Transform parent, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        var obj = new GameObject(label + "Btn");
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = MenuThemes.Hud.AccentSecondary;
        var btn = obj.AddComponent<Button>();
        var rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0.14f, 0.052f);
        rt.anchoredPosition = anchoredPos;

        var txtObj = new GameObject("Label");
        txtObj.transform.SetParent(obj.transform, false);
        var tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label.ToUpperInvariant();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = MenuThemes.Typography.Emphasis;
        tmp.fontSize = 0.020f;
        tmp.color = Color.white;
        var tr = txtObj.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;

        btn.onClick.AddListener(onClick);
        ClueUiLayout.WireGotItButtonForXrDirectSelect(btn);
        return btn;
    }

    void OnNextPage()
    {
        _pageIndex = Mathf.Min(_pages.Length - 1, _pageIndex + 1);
        GameAudioFeedback.PlayMenuSelect();
        XrHaptics.PulseLeft(0.28f, 0.03f);
        RefreshPage();
    }

    void OnPrevPage()
    {
        _pageIndex = Mathf.Max(0, _pageIndex - 1);
        GameAudioFeedback.PlayMenuSelect();
        XrHaptics.PulseLeft(0.24f, 0.03f);
        RefreshPage();
    }

    void RefreshPage()
    {
        if (_pages == null || _pages.Length == 0) return;
        _pageIndex = Mathf.Clamp(_pageIndex, 0, _pages.Length - 1);
        if (_titleText != null) _titleText.text = _pages[_pageIndex].title;
        if (_bodyText != null) _bodyText.text = _pages[_pageIndex].body;
        if (_pageText != null) _pageText.text = $"Page {_pageIndex + 1}/{_pages.Length}";
        if (_prevButton != null) _prevButton.interactable = _pageIndex > 0;
        if (_nextButton != null) _nextButton.interactable = _pageIndex < _pages.Length - 1;
    }
}
