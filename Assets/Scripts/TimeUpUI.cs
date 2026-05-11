using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Full-screen "TIME'S UP" panel shown when FloatingAutoTimer.OnTimerExpired fires.
///
/// Game logic (puzzle system) is disabled to prevent clue reveals/solving.
/// Player can still move and interact with physics objects via locomotion providers.
/// Play Again uses XR UI ray interaction (no 3D colliders on buttons).
/// </summary>
[DisallowMultipleComponent]
public class TimeUpUI : MonoBehaviour
{
    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BgDark      = MenuThemes.Hud.Background;
    static readonly Color HeaderColor = new Color(MenuThemes.Hud.Warning.r * 0.35f, MenuThemes.Hud.Warning.g * 0.35f, MenuThemes.Hud.Warning.b * 0.35f, 1f);
    static readonly Color AccentRed   = MenuThemes.Hud.Warning;
    static readonly Color AccentTeal  = MenuThemes.Hud.AccentSecondary;
    static readonly Color TextSub     = MenuThemes.Hud.TextDanger;
    static readonly Color TextPrimary = MenuThemes.Hud.TextPrimary;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private Canvas    _canvas;
    private GameObject _panel;
    private Button     _playAgainButton;

    // ── Bootstrap ─────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (FindFirstObjectByType<TimeUpUI>() != null) return;
        var go = new GameObject("[TimeUpUI]");
        DontDestroyOnLoad(go);
        go.AddComponent<TimeUpUI>();
    }

    private void Awake()
    {
        BuildUI();
        FloatingAutoTimer.OnTimerExpired += Show;
    }

    private void Start()
    {
        // Hand finding not needed anymore — Button component handles interaction
    }

    private void Update()
    {
        // No failsafe needed — just let Button component handle it
    }

    private void OnDestroy()
    {
        FloatingAutoTimer.OnTimerExpired -= Show;
    }

    // ── Show ──────────────────────────────────────────────────────────────────
    private void Show()
    {
        RefreshStats();
        _panel.SetActive(true);
        PositionInFrontOfCamera();

        // Disable puzzle manager to stop game logic (clue reveals, key reveal, light, etc.)
        // But leave locomotion and physics enabled so player can still move and interact
        var pm = FindFirstObjectByType<PuzzleManager>();
        if (pm != null)
            pm.enabled = false;

        // Disable FloatingAutoTimer to stop other countdown-related logic
        var timer = FindFirstObjectByType<FloatingAutoTimer>();
        if (timer != null)
            timer.enabled = false;

        // Disable button interaction so "Play Again" is only option
        _playAgainButton.interactable = true;
    }

    private void PositionInFrontOfCamera()
    {
        if (Camera.main == null) return;
        Transform cam = Camera.main.transform;
        _canvas.transform.position =
            cam.position + cam.forward * 0.65f + Vector3.down * 0.05f;
        _canvas.transform.rotation =
            Quaternion.Euler(0f, cam.rotation.eulerAngles.y, 0f);
    }

    private void LateUpdate()
    {
        if (_panel != null && _panel.activeSelf)
            PositionInFrontOfCamera();
    }

    // ── Stats ─────────────────────────────────────────────────────────────────
    private void RefreshStats()
    {
        foreach (var tmp in _panel.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (tmp.gameObject.name == "StatsLabel")
            {
                tmp.text =
                    $"Clues solved: {PuzzleManager.SolvedClueCount} / 3     " +
                    $"Hints used: {FloatingAutoTimer.HintsUsed}";
                break;
            }
        }
    }

    // ── UI Construction ───────────────────────────────────────────────────────
    private void BuildUI()
    {
        var canvasObj = new GameObject("TimeUpCanvas");
        canvasObj.transform.SetParent(transform);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        var canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta  = new Vector2(0.80f, 0.70f);
        canvasRT.localScale = Vector3.one;

        EnsureEventSystem();

        // Panel card
        _panel = MakePanel("TimeUpPanel", canvasObj.transform,
            new Vector2(0.70f, 0.60f), Vector2.zero, BgDark);
        MakeBorderGlow(_panel.transform, new Vector2(0.70f, 0.60f), AccentRed);
        _panel.SetActive(false);

        // Header bar
        var header = MakePanel("Header", _panel.transform,
            new Vector2(0.70f, 0.10f), new Vector2(0f, 0.25f), HeaderColor);
        MakeLabel("HeaderLabel", header.transform,
            "ESCAPE ROOM", 0.036f, FontStyles.Bold,
            MenuThemes.Hud.TextDanger, TextAlignmentOptions.Center,
            new Vector2(0.60f, 0.10f), Vector2.zero);

        // Title
        MakeLabel("TitleLabel", _panel.transform,
            "TIME'S UP!", 0.062f, FontStyles.Bold,
            AccentRed, TextAlignmentOptions.Center,
            new Vector2(0.60f, 0.09f), new Vector2(0f, 0.095f));

        // Subtitle
        MakeLabel("SubtitleLabel", _panel.transform,
            "You didn't escape in time.\nBetter luck next round!", 0.028f, FontStyles.Normal,
            TextSub, TextAlignmentOptions.Center,
            new Vector2(0.60f, 0.09f), new Vector2(0f, -0.010f));

        // Divider
        var divider = new GameObject("Divider");
        divider.transform.SetParent(_panel.transform, false);
        divider.AddComponent<Image>().color = new Color(MenuThemes.Hud.Warning.r, MenuThemes.Hud.Warning.g, MenuThemes.Hud.Warning.b, 0.40f);
        var drt = divider.GetComponent<RectTransform>();
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta        = new Vector2(0.60f, 0.003f);
        drt.anchoredPosition = new Vector2(0f, -0.090f);

        // Stats
        MakeLabel("StatsLabel", _panel.transform,
            "", 0.024f, FontStyles.Normal,
            new Color(MenuThemes.Hud.TextPrimary.r, MenuThemes.Hud.TextPrimary.g, MenuThemes.Hud.TextPrimary.b, 0.85f), TextAlignmentOptions.Center,
            new Vector2(0.62f, 0.06f), new Vector2(0f, -0.135f));

        // Play Again button — uses standard Button component for reliable interaction
        _playAgainButton = MakeButton("PlayAgainButton", "PLAY AGAIN",
            _panel.transform, new Vector2(0f, -0.220f), 0.28f, 0.08f, AccentTeal);

        _playAgainButton.onClick.AddListener(() =>
        {
            if (_panel != null)
                _panel.SetActive(false);
            if (_playAgainButton != null)
                _playAgainButton.interactable = false;
            var timer = FindFirstObjectByType<FloatingAutoTimer>();
            if (timer != null)
            {
                timer.enabled = true;
                timer.ResetForRestart();
            }
            Debug.Log("[TimeUpUI] Play Again clicked — reloading scene");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static GameObject MakePanel(string name, Transform parent,
        Vector2 size, Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<Image>().color = color;
        var rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
        return obj;
    }

    private static void MakeBorderGlow(Transform parent, Vector2 innerSize, Color color)
    {
        const float b = 0.014f;
        var obj = new GameObject("BorderGlow");
        obj.transform.SetParent(parent, false);
        obj.transform.SetAsFirstSibling();
        obj.AddComponent<Image>().color = color;
        var rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = innerSize + new Vector2(b, b);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void MakeLabel(string name, Transform parent,
        string text, float fontSize, FontStyles style, Color color,
        TextAlignmentOptions align, Vector2 sizeDelta, Vector2 pos)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp                = obj.AddComponent<TextMeshProUGUI>();
        tmp.text               = text;
        tmp.fontSize           = fontSize;
        tmp.fontStyle          = style;
        tmp.alignment          = align;
        tmp.color              = color;
        tmp.textWrappingMode   = TextWrappingModes.Normal;
        var rt                 = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = sizeDelta;
        rt.anchoredPosition = pos;
    }

    private static void EnsureEventSystem()
    {
        var xrModule = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            if (xrModule != null) es.AddComponent(xrModule);
            else es.AddComponent<StandaloneInputModule>();
        }
        else if (xrModule != null && EventSystem.current.GetComponent(xrModule) == null)
        {
            EventSystem.current.gameObject.AddComponent(xrModule);
        }
    }

    private static Button MakeButton(string name, string label,
        Transform parent, Vector2 pos, float w, float h, Color color)
    {
        var btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        var img   = btnObj.AddComponent<Image>();
        img.color = color;
        var btn   = btnObj.AddComponent<Button>();

        var cb              = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = Color.Lerp(color, Color.white, 0.30f);
        cb.pressedColor     = Color.Lerp(color, Color.black, 0.30f);
        cb.selectedColor    = cb.highlightedColor;
        btn.colors          = cb;

        var rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = pos;

        // Label
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

        return btn;
    }
}
