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
            PositionPanel();
        if (debugPlacement && _panel.activeSelf && Camera.main != null)
        {
            var c = Camera.main.transform;
            Debug.Log(
                $"[VROnboardingOverlay] Opened: canvasPos={_canvas.transform.position} camPos={c.position} " +
                $"fwd={panelForwardMeters} right={panelRightMeters} up={panelUpMeters}");
        }

        XrHaptics.PulseLeft(0.3f, 0.04f);
    }

    [Header("Panel placement (camera-relative)")]
    [SerializeField] float panelForwardMeters = 0.48f;
    [SerializeField] float panelRightMeters = 0.12f;
    [SerializeField] float panelUpMeters = 0.02f;
    [Tooltip("Log position when you open this card (paste Console if it clips).")]
    [SerializeField] bool debugPlacement;

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
        var t = title.AddComponent<TextMeshProUGUI>();
        t.text = "How to play — press left X button";
        t.fontSize = 0.044f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = MenuThemes.Hud.TextPrimary;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.028f;
        t.fontSizeMax = 0.044f;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Truncate;
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.04f, 0.88f);
        trt.anchorMax = new Vector2(0.96f, 0.98f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var body = new GameObject("Body");
        body.transform.SetParent(_panel.transform, false);
        var b = body.AddComponent<TextMeshProUGUI>();
        b.text =
            "<b>Open this help anytime with Left X.</b>\n\n" +
            "<b>Start here:</b> Find the <b>Sage statue</b> in the room. Touch or interact with it first — it lights important spots and guides you to all clues in order. Return to it whenever you feel stuck.\n\n" +
            "Move: left thumbstick. Turn: right thumbstick (snap).\n\n" +
            "Teleport: aim at the floor and use the trigger when you see the arc.\n\n" +
            "Clues unlock one at a time. Hints: press <b>left Y</b> to open the timer HUD, then use <b>Hint 1 / Hint 2</b> (grip near the row or use the controller ray).\n\n" +
            "<b>Left X</b> opens and closes this card (keyboard <b>O</b> in Editor). <b>Left Y</b> toggles the timer HUD (<b>H</b> in Editor). This help starts hidden — open it whenever you need a reminder.";
        b.fontSize = 0.023f;
        b.enableAutoSizing = true;
        b.fontSizeMin = 0.016f;
        b.fontSizeMax = 0.023f;
        b.alignment = TextAlignmentOptions.TopLeft;
        b.color = MenuThemes.Hud.TextSecondary;
        b.textWrappingMode = TextWrappingModes.Normal;
        b.overflowMode = TextOverflowModes.Truncate;
        b.lineSpacing = 0.2f;
        b.paragraphSpacing = 0.65f;
        b.margin = new Vector4(0.008f, 0.008f, 0.008f, 0.008f);
        b.richText = true;
        var brt = body.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.06f, 0.05f);
        brt.anchorMax = new Vector2(0.94f, 0.87f);
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;
    }
}
