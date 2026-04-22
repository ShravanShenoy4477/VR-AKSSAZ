using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Full-screen "TIME'S UP" panel shown when FloatingAutoTimer.OnTimerExpired fires.
///
/// Movement is frozen via Time.timeScale = 0.
/// The Play Again button is detected by direct hand-distance check every Update
/// (not via VRHandPoker / physics) so it works reliably even with timeScale = 0.
/// </summary>
[DisallowMultipleComponent]
public class TimeUpUI : MonoBehaviour
{
    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BgDark      = new Color(0.06f, 0.04f, 0.04f, 0.97f);
    static readonly Color HeaderColor = new Color(0.28f, 0.06f, 0.06f, 1.00f);
    static readonly Color AccentRed   = new Color(0.85f, 0.18f, 0.18f, 1.00f);
    static readonly Color AccentTeal  = new Color(0.10f, 0.62f, 0.68f, 1.00f);
    static readonly Color TextSub     = new Color(1.00f, 0.70f, 0.70f, 1.00f);
    static readonly Color TextPrimary = new Color(0.94f, 0.94f, 1.00f, 1.00f);

    // ── Runtime ───────────────────────────────────────────────────────────────
    private Canvas    _canvas;
    private GameObject _panel;
    private Transform  _restartBtnTransform;   // world position used for proximity
    private Image      _restartBtnImage;        // tinted on hover

    // Hand transforms — found by name in Start
    private Transform _rightHand;
    private Transform _leftHand;

    // Prevents firing Restart more than once
    private bool _restarting = false;

    // Hover tint state
    private bool  _wasHovering    = false;
    private Color _btnNormalColor = new Color(0.10f, 0.62f, 0.68f, 1.00f);

    // How close a hand must be to the button centre to trigger it (metres)
    private const float TouchRadius = 0.07f;

    // ── Bootstrap ─────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (FindObjectOfType<TimeUpUI>() != null) return;
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
        // Find hand controllers — same names used everywhere in the project
        var r = GameObject.Find("RightHandController");
        var l = GameObject.Find("LeftHandController");
        if (r != null) _rightHand = r.transform;
        if (l != null) _leftHand  = l.transform;
    }

    private void OnDestroy()
    {
        FloatingAutoTimer.OnTimerExpired -= Show;
    }

    // ── Update — hand proximity check (runs even with timeScale = 0) ──────────
    private void Update()
    {
        if (_panel == null || !_panel.activeSelf || _restarting) return;
        if (_restartBtnTransform == null) return;

        bool hovering = IsHandNear(_restartBtnTransform.position);

        // Visual hover tint
        if (hovering != _wasHovering)
        {
            if (_restartBtnImage != null)
                _restartBtnImage.color = hovering
                    ? Color.Lerp(_btnNormalColor, Color.white, 0.35f)
                    : _btnNormalColor;
            _wasHovering = hovering;
        }

        // Rising edge — fire once when hand first enters the button
        if (hovering)
            Restart();
    }

    private bool IsHandNear(Vector3 worldPos)
    {
        if (_rightHand != null &&
            Vector3.Distance(_rightHand.position, worldPos) < TouchRadius) return true;
        if (_leftHand  != null &&
            Vector3.Distance(_leftHand.position,  worldPos) < TouchRadius) return true;
        return false;
    }

    // ── Show ──────────────────────────────────────────────────────────────────
    private void Show()
    {
        _restarting = false;
        _wasHovering = false;
        RefreshStats();
        _panel.SetActive(true);
        PositionInFrontOfCamera();

        // Freeze movement — locomotion providers use Time.deltaTime → they stop.
        // XR head/hand tracking is driven by the XR subsystem, unaffected by timeScale.
        Time.timeScale = 0f;
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

    // ── Restart ───────────────────────────────────────────────────────────────
    private void Restart()
    {
        if (_restarting) return;
        _restarting    = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
            new Color(1.00f, 0.45f, 0.45f, 1f), TextAlignmentOptions.Center,
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
        divider.AddComponent<Image>().color = new Color(0.85f, 0.18f, 0.18f, 0.40f);
        var drt = divider.GetComponent<RectTransform>();
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta        = new Vector2(0.60f, 0.003f);
        drt.anchoredPosition = new Vector2(0f, -0.090f);

        // Stats
        MakeLabel("StatsLabel", _panel.transform,
            "", 0.024f, FontStyles.Normal,
            new Color(0.80f, 0.80f, 0.95f, 0.85f), TextAlignmentOptions.Center,
            new Vector2(0.62f, 0.06f), new Vector2(0f, -0.135f));

        // Play Again button — no BoxCollider needed, proximity handled in Update
        var btnObj = new GameObject("PlayAgainButton");
        btnObj.transform.SetParent(_panel.transform, false);
        _restartBtnImage       = btnObj.AddComponent<Image>();
        _restartBtnImage.color = _btnNormalColor;
        var brt = btnObj.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta        = new Vector2(0.28f, 0.08f);
        brt.anchoredPosition = new Vector2(0f, -0.220f);
        _restartBtnTransform = btnObj.transform;

        var lblObj = new GameObject("Label");
        lblObj.transform.SetParent(btnObj.transform, false);
        var lbl       = lblObj.AddComponent<TextMeshProUGUI>();
        lbl.text      = "PLAY AGAIN";
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.fontStyle = FontStyles.Bold;
        lbl.fontSize  = 0.08f * 0.42f;
        lbl.color     = Color.white;
        var lrt       = lblObj.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = Vector2.zero;
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
        tmp.enableWordWrapping = true;
        var rt                 = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = sizeDelta;
        rt.anchoredPosition = pos;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
