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
/// GOT IT dismisses it and reports completion to PuzzleManager.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class ClueNote : MonoBehaviour
{
    [Header("Clue Content")]
    [TextArea(3, 6)]
    public string clueText  = "Where the cushions end and the shadows begin —\nyour next answer hides behind the larger seat.";
    public string clueLabel = "CLUE";
    public int    clueIndex = 1;

    // ── Colours ───────────────────────────────────────────────────────────
    static readonly Color BgPaper   = MenuThemes.Clue.Background;
    static readonly Color HeaderCol = MenuThemes.Clue.Header;
    static readonly Color Ink       = MenuThemes.Clue.Ink;
    static readonly Color InkFaded  = MenuThemes.Clue.InkMuted;
    static readonly Color BtnCol    = MenuThemes.Clue.Button;
    static readonly Color BtnText   = MenuThemes.Clue.ButtonText;

    // ── Runtime ───────────────────────────────────────────────────────────
    private XRGrabInteractable _grab;
    private Canvas             _clueCanvas;
    private GameObject         _cluePanel;
    private Button             _gotItBtn;
    private TextMeshProUGUI    _gotItLabel;
    private bool               _progressSent;
    private Coroutine          _holdRevealCo;
    private bool               _fading;
    private float              _nearCloseAccum;
    private Transform          _rightCtrl;
    private Transform          _leftCtrl;

    [Header("Read timing")]
    [Tooltip("Minimum seconds the book must be held (while still holding) before the clue panel appears.")]
    [SerializeField] float minHoldSecondsForClueReveal = 0.55f;

    [Header("Dismiss")]
    [SerializeField] float panelFadeInSeconds = 0.25f;
    [SerializeField] float panelFadeOutSeconds = 0.55f;
    [Tooltip("After you release the book, holding a controller inside this padded box near GOT IT for this long dismisses with the same fade.")]
    [SerializeField] float nearCloseDismissHoldSeconds = 0.72f;
    [SerializeField] float nearCloseBoundsExpand = 0.12f;

    // ── Lifecycle ─────────────────────────────────────────────────────────
    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _grab.selectEntered.AddListener(_ => OnGrabbed());
        _grab.selectExited.AddListener(_  => OnReleased());
        ConfigureGrabForComfort();
    }

    void ConfigureGrabForComfort()
    {
        var t = transform.Find("GrabAttach");
        if (t == null)
        {
            var go = new GameObject("GrabAttach");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            t = go.transform;
        }

        _grab.attachTransform = t;
        _grab.trackRotation = false;
        _grab.throwOnDetach = false;
        _grab.forceGravityOnDetach = true;
        _grab.retainTransformParent = true;

        if (TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearDamping = 2.5f;
            rb.angularDamping = 2.5f;
        }
    }

    private void Start()
    {
        BuildCluePanel();
        EnsureEventSystem();
        if (_rightCtrl == null)
        {
            var r = GameObject.Find("RightHandController");
            if (r != null) _rightCtrl = r.transform;
        }

        if (_leftCtrl == null)
        {
            var l = GameObject.Find("LeftHandController");
            if (l != null) _leftCtrl = l.transform;
        }
    }

    private void OnDestroy()
    {
        if (_holdRevealCo != null) StopCoroutine(_holdRevealCo);
        if (_grab == null) return;
        _grab.selectEntered.RemoveAllListeners();
        _grab.selectExited.RemoveAllListeners();
    }

    // ── LateUpdate — keep panel in front of headset ────────────────────────
    private void Update()
    {
        if (_progressSent || _fading) return;
        if (_cluePanel == null || !_cluePanel.activeSelf) return;
        if (_grab != null && _grab.isSelected)
        {
            _nearCloseAccum = 0f;
            return;
        }

        if (_gotItBtn == null || !_gotItBtn.interactable)
        {
            _nearCloseAccum = 0f;
            return;
        }

        var box = _gotItBtn.GetComponent<BoxCollider>();
        if (box == null) return;

        var b = box.bounds;
        b.Expand(nearCloseBoundsExpand);
        bool near = false;
        if (_rightCtrl != null) near |= b.Contains(_rightCtrl.position);
        if (_leftCtrl != null) near |= b.Contains(_leftCtrl.position);
        if (near) _nearCloseAccum += Time.deltaTime;
        else _nearCloseAccum = 0f;

        if (_nearCloseAccum >= nearCloseDismissHoldSeconds)
            Dismiss();
    }

    private void LateUpdate()
    {
        if (_cluePanel == null || !_cluePanel.activeSelf) return;
        if (Camera.main == null) return;
        Transform cam = Camera.main.transform;
        _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
        _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
    }

    // ── Grab callbacks ────────────────────────────────────────────────────
    private void OnGrabbed()
    {
        if (_progressSent)
        {
            RevealCluePanelAfterSolved();
            return;
        }

        if (_holdRevealCo != null) StopCoroutine(_holdRevealCo);
        if (_cluePanel != null && _cluePanel.activeSelf) return;

        _holdRevealCo = StartCoroutine(RevealWhileHeldAfterMinHold());
    }

    IEnumerator RevealWhileHeldAfterMinHold()
    {
        try
        {
            yield return new WaitForSeconds(minHoldSecondsForClueReveal);
            if (_progressSent) yield break;
            if (_grab == null || !_grab.isSelected) yield break;
            if (_cluePanel != null && _cluePanel.activeSelf) yield break;
            RevealCluePanelFirstTime();
        }
        finally
        {
            _holdRevealCo = null;
        }
    }

    void HidePanelOnly()
    {
        if (_fading) return;
        StartCoroutine(FadeOutPanelCoroutine(false));
    }

    private void OnReleased()
    {
        if (_holdRevealCo != null)
        {
            StopCoroutine(_holdRevealCo);
            _holdRevealCo = null;
        }

        // Panel stays visible after release until GOT IT / proximity toward close.
    }

    IEnumerator FadeOutPanelCoroutine(bool fireSolvedEvent)
    {
        _fading = true;
        _nearCloseAccum = 0f;
        var grip = _gotItBtn != null ? _gotItBtn.GetComponent<ClueGotItGripConfirm>() : null;
        if (grip != null) grip.enabled = false;

        var cg = _cluePanel != null ? _cluePanel.GetComponent<CanvasGroup>() : null;
        if (cg == null || _cluePanel == null)
        {
            if (_cluePanel != null) _cluePanel.SetActive(false);
            if (grip != null) grip.enabled = true;
            _fading = false;
            yield break;
        }

        cg.interactable = false;
        cg.blocksRaycasts = false;
        float dur = Mathf.Max(0.12f, panelFadeOutSeconds);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            u = u * u * (3f - 2f * u);
            cg.alpha = 1f - u;
            yield return null;
        }

        cg.alpha = 0f;
        _cluePanel.SetActive(false);
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
        if (grip != null) grip.enabled = true;
        _fading = false;
        if (fireSolvedEvent)
            PuzzleManager.TryHandleClueSolved(clueIndex, this);
    }

    void RevealCluePanelAfterSolved()
    {
        _cluePanel.SetActive(true);
        ResetPanelCanvasGroup();
        StartCoroutine(FadeInPanelCoroutine());

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        if (_gotItBtn != null && _gotItLabel != null)
        {
            _gotItBtn.onClick.RemoveAllListeners();
            _gotItLabel.text = "CLOSE";
            _gotItBtn.onClick.AddListener(HidePanelOnly);
            _gotItBtn.interactable = true;
        }

        XrHaptics.PulseRight();
    }

    void RevealCluePanelFirstTime()
    {
        _cluePanel.SetActive(true);
        ResetPanelCanvasGroup();
        StartCoroutine(FadeInPanelCoroutine());

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        if (_gotItBtn != null && _gotItLabel != null)
        {
            _gotItBtn.onClick.RemoveAllListeners();
            _gotItLabel.text = "GOT IT";
            _gotItBtn.onClick.AddListener(Dismiss);
            _gotItBtn.interactable = false;
            StartCoroutine(EnableGotItAfterDelay(0.75f));
        }

        XrHaptics.PulseRight();
    }

    private IEnumerator EnableGotItAfterDelay(float delay)
    {
        _gotItBtn.interactable = false;
        yield return new WaitForSeconds(delay);
        if (!_progressSent) _gotItBtn.interactable = true;
    }

    void ResetPanelCanvasGroup()
    {
        var cg = _cluePanel != null ? _cluePanel.GetComponent<CanvasGroup>() : null;
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    private void Dismiss()
    {
        if (_progressSent || _fading) return;
        _progressSent = true;
        StartCoroutine(FadeOutPanelCoroutine(true));
    }

    IEnumerator FadeInPanelCoroutine()
    {
        var cg = _cluePanel != null ? _cluePanel.GetComponent<CanvasGroup>() : null;
        if (cg == null || _cluePanel == null) yield break;

        float dur = Mathf.Max(0.05f, panelFadeInSeconds);
        float t = 0f;
        cg.alpha = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            u = u * u * (3f - 2f * u);
            cg.alpha = u;
            yield return null;
        }
        cg.alpha = 1f;
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
        canvasRT.sizeDelta  = new Vector2(ClueUiLayout.PanelWidthMeters, ClueUiLayout.PanelHeightMeters);
        canvasRT.localScale = Vector3.one;
        _clueCanvas = canvas;

        // ── Paper panel ────────────────────────────────────────────────
        float PW = ClueUiLayout.PanelWidthMeters - 0.04f;
        float PH = ClueUiLayout.PanelHeightMeters - 0.04f;
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

        // GOT IT button — wide, tall target for controller ray
        float btnH = Mathf.Max(PH * 0.18f, 0.10f);
        float btnW = Mathf.Max(PW * 0.78f, 0.26f);
        float btnY = -(PH * 0.5f) + btnH * 0.5f + PH * 0.03f;
        _gotItBtn = MakeButton("GotItBtn", "GOT IT",
            panel.transform, new Vector2(0f, btnY), btnW, btnH);
        _gotItLabel = _gotItBtn.GetComponentInChildren<TextMeshProUGUI>();
        _gotItBtn.interactable = false;
        ClueUiLayout.WireGotItButtonForXrDirectSelect(_gotItBtn);
        var gotItGrip = _gotItBtn.GetComponent<ClueGotItGripConfirm>();
        if (gotItGrip != null)
            gotItGrip.SetGripThresholds(0.84f, 0.2f, 0.38f);
        var gotItBox = _gotItBtn.GetComponent<BoxCollider>();
        if (gotItBox != null)
        {
            var s = gotItBox.size;
            gotItBox.size = new Vector3(s.x * 1.12f, s.y * 1.18f, Mathf.Max(0.042f, s.z * 1.4f));
        }

        var panelGroup = panel.AddComponent<CanvasGroup>();
        panelGroup.alpha = 1f;

        // Body text — fills space between header and button
        float bodyGap   = PH * 0.03f;
        float headerBot = headerY - headerH * 0.5f;
        float btnTop    = btnY    + btnH   * 0.5f;
        float bodyH     = (headerBot - bodyGap) - (btnTop + bodyGap);
        float bodyY     = (headerBot - bodyGap + btnTop + bodyGap) * 0.5f;
        var bodyTmp = MakeLabel("BodyTxt", panel.transform,
            clueText, PH * 0.042f, FontStyles.Italic, Ink,
            TextAlignmentOptions.Center,
            new Vector2(PW - 0.08f * 2f, Mathf.Max(bodyH, 0.01f)),
            new Vector2(0f, bodyY));
        bodyTmp.enableAutoSizing = false;
        bodyTmp.textWrappingMode = TextWrappingModes.Normal;

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
        tmp.textWrappingMode = TextWrappingModes.Normal;
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

        return btn;
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
}
