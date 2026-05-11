using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Auto-spawns at runtime and listens for DoorProximityHinge.OnDoorOpened.
/// When the door opens it shows a congratulations panel in front of the player
/// with a Play Again button that restarts the scene.
/// Interaction uses XR UI ray (TrackedDeviceGraphicRaycaster + XRUIInputModule).
/// </summary>
public class GameCompletionUI : MonoBehaviour
{
    // ── Colours (matching FloatingAutoTimer theme) ────────────────────────
    static readonly Color BgDark       = MenuThemes.Hud.Background;
    static readonly Color HeaderColor  = MenuThemes.Hud.Header;
    static readonly Color AccentPurple = MenuThemes.Hud.AccentPrimary;
    static readonly Color AccentTeal   = MenuThemes.Hud.AccentSecondary;
    static readonly Color GoldText     = MenuThemes.Hud.TimerGold;
    static readonly Color TextPrimary  = MenuThemes.Hud.TextPrimary;
    static readonly Color TextSub      = MenuThemes.Hud.TextSecondary;

    private Canvas           _canvas;
    private GameObject       _panel;
    private TextMeshProUGUI  _scoreLine;
    private TextMeshProUGUI  _breakdownLine;

    [Header("Placement")]
    [SerializeField] private bool useFixedWorldTransform = false;
    [SerializeField] private Vector3 fixedWorldPosition = new Vector3(-5.857f, 1.468f, -3.285f);
    [SerializeField] private Vector3 fixedWorldRotation = new Vector3(0f, 0f, -90f);

    // ── Bootstrap ─────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        var obj = new GameObject("[GameCompletionUI]");
        DontDestroyOnLoad(obj);
        obj.AddComponent<GameCompletionUI>();
    }

    private void Awake()
    {
        BuildUI();
        DoorProximityHinge.OnDoorOpened += ShowCompletionScreen;
    }

    private void OnDestroy()
    {
        DoorProximityHinge.OnDoorOpened -= ShowCompletionScreen;
    }

    // ── Show ──────────────────────────────────────────────────────────────
    private void ShowCompletionScreen()
    {
        // ── Score calculation ──────────────────────────────────────────
        int   cluesSolved  = PuzzleManager.SolvedClueCount;
        float minsLeft     = FloatingAutoTimer.TimeRemaining / 60f;
        int   hintsUsed    = FloatingAutoTimer.HintsUsed;
        float score        = cluesSolved + minsLeft - 0.25f * hintsUsed;
        score              = Mathf.Max(0f, score);

        if (_scoreLine != null)
            _scoreLine.text = $"SCORE   {score:0.00}";

        if (_breakdownLine != null)
            _breakdownLine.text =
                $"Clues {cluesSolved}/3  +{cluesSolved}     " +
                $"Time  +{minsLeft:0.0} min     " +
                $"Hints  -{0.25f * hintsUsed:0.00}";

        _panel.SetActive(true);

        if (useFixedWorldTransform)
        {
            _canvas.transform.position = fixedWorldPosition;
            _canvas.transform.rotation = Quaternion.Euler(fixedWorldRotation);
        }
        else if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _canvas.transform.position =
                cam.position + cam.forward * 1.0f + Vector3.down * 0.05f;
            _canvas.transform.rotation =
                Quaternion.Euler(0f, cam.rotation.eulerAngles.y, 0f);
        }
    }

    // ── UI Construction ───────────────────────────────────────────────────
    private void BuildUI()
    {
        // Canvas
        var canvasObj = new GameObject("CompletionCanvas");
        canvasObj.transform.SetParent(transform);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        var xrRay = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRay != null) canvasObj.AddComponent(xrRay);

        var canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta  = new Vector2(1.6f, 1.1f);
        canvasRT.localScale = Vector3.one;

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

        // Panel card
        _panel = MakePanel("CompletionPanel", canvasObj.transform,
            new Vector2(1.30f, 1.10f), Vector2.zero, BgDark);
        MakeBorderGlow(_panel.transform, new Vector2(1.30f, 1.10f), AccentPurple);
        _panel.SetActive(false);

        // Header bar (top strip)
        var header = MakePanel("Header", _panel.transform,
            new Vector2(1.30f, 0.18f), new Vector2(0f, 0.37f), HeaderColor);
        MakeLabel("HeaderLabel", header.transform,
            "ESCAPE ROOM", 0.070f, FontStyles.Bold,
            MenuThemes.Hud.AccentPrimary, TextAlignmentOptions.Center,
            new Vector2(1.10f, 0.18f), Vector2.zero);

        // "YOU ESCAPED!" — sits below the header with a clear gap
        MakeLabel("CongratTitle", _panel.transform,
            "YOU ESCAPED!", 0.100f, FontStyles.Bold,
            GoldText, TextAlignmentOptions.Center,
            new Vector2(1.10f, 0.14f), new Vector2(0f, 0.12f));

        // Subtitle — below the title
        MakeLabel("Subtitle", _panel.transform,
            "Congratulations!\nYou solved every clue and made it out.", 0.055f, FontStyles.Normal,
            TextSub, TextAlignmentOptions.Center,
            new Vector2(1.10f, 0.14f), new Vector2(0f, -0.07f));

        // Score divider line
        var divider = new GameObject("Divider");
        divider.transform.SetParent(_panel.transform, false);
        divider.AddComponent<Image>().color = new Color(MenuThemes.Hud.AccentPrimary.r, MenuThemes.Hud.AccentPrimary.g, MenuThemes.Hud.AccentPrimary.b, 0.45f);
        var drt = divider.GetComponent<RectTransform>();
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta        = new Vector2(1.10f, 0.004f);
        drt.anchoredPosition = new Vector2(0f, -0.165f);

        // Score total
        var scoreObj = new GameObject("ScoreLine");
        scoreObj.transform.SetParent(_panel.transform, false);
        _scoreLine           = scoreObj.AddComponent<TextMeshProUGUI>();
        _scoreLine.text      = "SCORE   0.00";
        _scoreLine.fontSize  = 0.072f;
        _scoreLine.fontStyle = FontStyles.Bold;
        _scoreLine.color     = GoldText;
        _scoreLine.alignment = TextAlignmentOptions.Center;
        var srt = scoreObj.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta        = new Vector2(1.10f, 0.10f);
        srt.anchoredPosition = new Vector2(0f, -0.235f);

        // Breakdown row
        var bdObj = new GameObject("BreakdownLine");
        bdObj.transform.SetParent(_panel.transform, false);
        _breakdownLine                 = bdObj.AddComponent<TextMeshProUGUI>();
        _breakdownLine.text            = "";
        _breakdownLine.fontSize        = 0.036f;
        _breakdownLine.color           = new Color(MenuThemes.Hud.TextSecondary.r, MenuThemes.Hud.TextSecondary.g, MenuThemes.Hud.TextSecondary.b, 0.85f);
        _breakdownLine.alignment       = TextAlignmentOptions.Center;
        _breakdownLine.textWrappingMode = TextWrappingModes.NoWrap;
        var brt2 = bdObj.GetComponent<RectTransform>();
        brt2.anchorMin = brt2.anchorMax = brt2.pivot = new Vector2(0.5f, 0.5f);
        brt2.sizeDelta        = new Vector2(1.20f, 0.06f);
        brt2.anchoredPosition = new Vector2(0f, -0.300f);

        // Play Again button
        var playAgainBtn = MakeButton("PlayAgainButton", "PLAY AGAIN",
            _panel.transform, new Vector2(0f, -0.385f), 0.46f, 0.13f, AccentTeal);

        playAgainBtn.onClick.AddListener(() =>
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

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

    /// <summary>
    /// Creates a TextMeshPro label using simple centre-anchored sizing.
    /// sizeDelta = (width, height), pos = anchoredPosition from parent centre.
    /// </summary>
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
        tmp.textWrappingMode    = TextWrappingModes.Normal;
        var rt                 = obj.GetComponent<RectTransform>();
        rt.anchorMin           = new Vector2(0.5f, 0.5f);
        rt.anchorMax           = new Vector2(0.5f, 0.5f);
        rt.pivot               = new Vector2(0.5f, 0.5f);
        rt.sizeDelta           = sizeDelta;
        rt.anchoredPosition    = pos;
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
