using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Attach to any XRGrabInteractable object (book, card, etc.).
/// When the player picks it up a clue card appears in front of the headset,
/// identical to ProximityClueNote and GlobeClueReveal.
/// GOT IT dismisses it and fires OnClueSolved.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class ClueNote : MonoBehaviour
{
    [Header("Clue Content")]
    [TextArea(3, 6)]
    public string clueText  = "Where the cushions end and the shadows begin —\nyour next answer hides behind the larger seat.";
    public string clueLabel = "CLUE";
    public int    clueIndex = 1;

    // ── Event ─────────────────────────────────────────────────────────────
    public static event System.Action<int> OnClueSolved;

    // ── Colours ───────────────────────────────────────────────────────────
    static readonly Color BgPaper   = new Color(0.96f, 0.93f, 0.84f, 0.98f);
    static readonly Color HeaderCol = new Color(0.20f, 0.14f, 0.08f, 1.00f);
    static readonly Color Ink       = new Color(0.12f, 0.10f, 0.08f, 1.00f);
    static readonly Color InkFaded  = new Color(0.45f, 0.32f, 0.14f, 0.80f);
    static readonly Color BtnCol    = new Color(0.25f, 0.18f, 0.10f, 1.00f);
    static readonly Color BtnText   = new Color(0.92f, 0.87f, 0.75f, 1.00f);

    // ── Runtime ───────────────────────────────────────────────────────────
    private XRGrabInteractable _grab;
    private Canvas             _clueCanvas;
    private GameObject         _cluePanel;
    private Button             _gotItBtn;
    private bool               _solved  = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────
    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _grab.selectEntered.AddListener(_ => OnGrabbed());
        _grab.selectExited.AddListener(_  => OnReleased());
    }

    private void Start()
    {
        BuildCluePanel();
        EnsureEventSystem();
    }

    private void OnDestroy()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveAllListeners();
        _grab.selectExited.RemoveAllListeners();
    }

    // ── LateUpdate — keep panel in front of headset ────────────────────────
    private void LateUpdate()
    {
        if (_cluePanel == null || !_cluePanel.activeSelf) return;
        if (Camera.main == null) return;
        Transform cam = Camera.main.transform;
        _clueCanvas.transform.position = cam.position + cam.forward * 0.6f;
        _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
    }

    // ── Grab callbacks ────────────────────────────────────────────────────
    private void OnGrabbed()
    {
        if (_solved) return;

        _cluePanel.SetActive(true);

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * 0.6f;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        StartCoroutine(EnableGotItAfterDelay(5f));
    }

    private void OnReleased()
    {
        // Keep panel visible if still reading; it stays until GOT IT is pressed
    }

    private IEnumerator EnableGotItAfterDelay(float delay)
    {
        _gotItBtn.interactable = false;
        yield return new WaitForSeconds(delay);
        if (!_solved) _gotItBtn.interactable = true;
    }

    private void Dismiss()
    {
        _solved = true;
        _cluePanel.SetActive(false);
        OnClueSolved?.Invoke(clueIndex);
    }

    // ── Build clue panel (root object, never parented to book) ────────────
    private void BuildCluePanel()
    {
        var canvasObj = new GameObject("ClueCanvas_Clue1");
        canvasObj.transform.position   = new Vector3(0f, -1000f, 0f);
        canvasObj.transform.localScale = Vector3.one;

        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        var xrRay = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRay != null) canvasObj.AddComponent(xrRay);

        var canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta  = new Vector2(0.60f, 0.80f);
        canvasRT.localScale = Vector3.one;
        _clueCanvas = canvas;

        // ── Paper panel ────────────────────────────────────────────────
        const float PW = 0.54f, PH = 0.74f;
        var panel = MakeRect("CluePanel", canvasObj.transform,
            new Vector2(PW, PH), Vector2.zero, BgPaper);
        _cluePanel = panel;

        // Header
        float headerH = PH * 0.13f;
        float headerY = PH * 0.5f - headerH * 0.5f;
        var header = MakeRect("Header", panel.transform,
            new Vector2(PW, headerH), new Vector2(0f, headerY), HeaderCol);
        MakeLabel("HeaderTxt", header.transform,
            clueLabel, headerH * 0.52f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center, new Vector2(PW * 0.85f, headerH), Vector2.zero);

        // GOT IT button
        float btnH = PH * 0.13f;
        float btnY = -(PH * 0.5f) + btnH * 0.5f + PH * 0.04f;
        _gotItBtn = MakeButton("GotItBtn", "GOT IT",
            panel.transform, new Vector2(0f, btnY), 0.28f, btnH);
        _gotItBtn.onClick.AddListener(Dismiss);
        _gotItBtn.interactable = false;

        // Body text — fills space between header and button
        float bodyGap   = PH * 0.03f;
        float headerBot = headerY - headerH * 0.5f;
        float btnTop    = btnY    + btnH   * 0.5f;
        float bodyH     = (headerBot - bodyGap) - (btnTop + bodyGap);
        float bodyY     = (headerBot - bodyGap + btnTop + bodyGap) * 0.5f;
        var bodyTmp = MakeLabel("BodyTxt", panel.transform,
            clueText, PH * 0.065f, FontStyles.Italic, Ink,
            TextAlignmentOptions.Center,
            new Vector2(PW - 0.08f * 2f, Mathf.Max(bodyH, 0.01f)),
            new Vector2(0f, bodyY));
        bodyTmp.enableAutoSizing = true;
        bodyTmp.fontSizeMin      = PH * 0.035f;
        bodyTmp.fontSizeMax      = PH * 0.065f;

        _cluePanel.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static GameObject MakeRect(string name, Transform parent,
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

    private static TextMeshProUGUI MakeLabel(string name, Transform parent,
        string text, float fontSize, FontStyles style, Color color,
        TextAlignmentOptions align, Vector2 size, Vector2 pos)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp                = obj.AddComponent<TextMeshProUGUI>();
        tmp.text               = text;
        tmp.fontSize           = fontSize;
        tmp.fontStyle          = style;
        tmp.color              = color;
        tmp.alignment          = align;
        tmp.enableWordWrapping = true;
        var rt                 = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
        return tmp;
    }

    private Button MakeButton(string name, string label,
        Transform parent, Vector2 pos, float w, float h)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<Image>().color = BtnCol;
        var btn = obj.AddComponent<Button>();
        var cb              = btn.colors;
        cb.normalColor      = BtnCol;
        cb.highlightedColor = Color.Lerp(BtnCol, Color.white, 0.35f);
        cb.pressedColor     = Color.Lerp(BtnCol, Color.black, 0.30f);
        cb.disabledColor    = new Color(0.35f, 0.35f, 0.35f, 0.5f);
        btn.colors          = cb;
        var rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = pos;

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(obj.transform, false);
        var tmp       = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.fontSize  = h * 0.50f;
        tmp.color     = BtnText;
        var trt       = textObj.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;

        var box       = obj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size      = new Vector3(w, h, 0.05f);

        return btn;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
