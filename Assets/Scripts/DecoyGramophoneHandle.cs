using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Attach to the gramophone handle (crank) GameObject.
///
/// Behaviour:
///   - Right hand enters the zone + grip pressed → handle cranks/rotates.
///   - After the crank completes → decoy popup appears in front of the headset.
///   - Popup looks like a real clue but the message is a dead end.
///   - "GOT IT" dismisses it. No OnClueSolved fired.
///
/// Setup:
///   1. Attach to the gramophone handle/crank GameObject.
///   2. Assign Right Controller (or name it "RightHandController").
///   3. Assign Handle Transform — the child mesh to rotate.
///      Leave blank to rotate the whole object.
///   4. Set Crank Axis to match the handle's local rotation axis.
///      Default (1,0,0) = rotates around local X, like turning a side crank.
///   5. Add a Sphere or Box Collider for the zone (arm-reach radius ~0.5m).
/// </summary>
[DisallowMultipleComponent]
public class DecoyGramophoneHandle : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Right hand controller. Auto-finds 'RightHandController' if blank.")]
    [SerializeField] Transform m_RightController;
    [Tooltip("Left hand controller. Auto-finds 'LeftHandController' if blank.")]
    [SerializeField] Transform m_LeftController;

    [Tooltip("The handle/crank mesh to rotate. Leave blank to rotate the whole object.")]
    [SerializeField] Transform m_Handle;

    [Header("Proximity")]
    [Tooltip("Distance at which the GRIP TO CRANK prompt appears (metres).")]
    public float proximityRadius = 1.2f;

    [Tooltip("How close the right hand must be to the handle before grip triggers it (metres). Keep small so only the gramophone area triggers it.")]
    public float interactRadius = 0.35f;

    [Header("Crank")]
    [Tooltip("Local axis the handle spins around. (1,0,0) = side crank, (0,0,1) = front crank.")]
    [SerializeField] Vector3 m_CrankAxis = new Vector3(1f, 0f, 0f);

    [Tooltip("How long the crank animation lasts (seconds).")]
    [SerializeField] float m_CrankDuration  = 1.6f;

    [Tooltip("How many full rotations the handle makes.")]
    [SerializeField] float m_CrankRotations = 3f;

    [SerializeField] AnimationCurve m_CrankEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── Colours — warm cream, identical to real clues ─────────────────────────
    static readonly Color BgPaper   = MenuThemes.Clue.Background;
    static readonly Color HeaderCol = MenuThemes.Clue.Header;
    static readonly Color InkFaded  = MenuThemes.Clue.InkMuted;
    static readonly Color Ink       = MenuThemes.Clue.Ink;
    static readonly Color SketchCol = MenuThemes.Clue.Sketch;
    static readonly Color BtnCol    = MenuThemes.Clue.Button;
    static readonly Color BtnText   = MenuThemes.Clue.ButtonText;
    static readonly Color PromptCol = MenuThemes.Clue.Prompt;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private Canvas     _clueCanvas;
    private GameObject _cluePanel;
    private Button     _gotItBtn;
    private Transform  _promptRoot;
    private bool       _triggered  = false;
    private bool       _prevGrip   = false;
    private bool       _prevGripL  = false;
    private bool       _reportedDecoy;
    private Coroutine  _crankRoutine;
    private bool       _fading;
    private float      _nearCloseAccum;

    [Header("Dismiss")]
    [SerializeField] float panelFadeInSeconds = 0.25f;
    [SerializeField] float panelFadeOutSeconds = 0.55f;
    [SerializeField] float nearCloseDismissHoldSeconds = 0.72f;
    [SerializeField] float nearCloseBoundsExpand = 0.12f;
    [SerializeField] float lockedPulseAmplitude = 0.16f;
    [SerializeField] float lockedPulseDuration = 0.03f;

    // ── Awake ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (m_Handle == null)
            m_Handle = transform;

        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null)
                m_RightController = go.transform;
            else
                Debug.LogWarning("DecoyGramophoneHandle: assign Right Controller in the Inspector.");
        }

        if (m_LeftController == null)
        {
            var go = GameObject.Find("LeftHandController");
            if (go != null)
                m_LeftController = go.transform;
        }
    }

    void Start()
    {
        BuildPrompt();
        BuildCluePanel();
        EnsureEventSystem();
    }

    // ── Update ────────────────────────────────────────────────────────────────
    void Update()
    {
        bool unlocked = PuzzleManager.IsDecoyPathUnlocked("gramophone");
        if (_cluePanel != null && _cluePanel.activeSelf)
            UpdateNearCloseDismiss();
        if (Camera.main == null || (m_RightController == null && m_LeftController == null)) return;

        bool panelOpen = _cluePanel != null && _cluePanel.activeSelf;

        // Prompt: show when camera is within proximityRadius of the handle
        if (_promptRoot != null)
        {
            float camDist = Vector3.Distance(Camera.main.transform.position, transform.position);
            _promptRoot.gameObject.SetActive(camDist < proximityRadius && !panelOpen);
        }

        bool edgeR = ReadInteractEdge(XRNode.RightHand, ref _prevGrip);
        bool edgeL = ReadInteractEdge(XRNode.LeftHand, ref _prevGripL);
        if (panelOpen) return;

        bool nearR = m_RightController != null && Vector3.Distance(m_RightController.position, transform.position) <= interactRadius;
        bool nearL = m_LeftController != null && Vector3.Distance(m_LeftController.position, transform.position) <= interactRadius;
        bool pressedNear = (edgeR && nearR) || (edgeL && nearL);
        if (!pressedNear) return;
        if (!unlocked)
        {
            InteractableHapticFeedback.ShowWrongOrderCue(transform);
            if (edgeR && nearR)
            {
                XrHaptics.PulseRight(lockedPulseAmplitude, lockedPulseDuration);
            }
            if (edgeL && nearL)
            {
                XrHaptics.PulseLeft(lockedPulseAmplitude, lockedPulseDuration);
            }
            if (_crankRoutine != null) StopCoroutine(_crankRoutine);
            _crankRoutine = StartCoroutine(CrankOnlyPreview());
            return;
        }

        InteractableHapticFeedback.ShowTargetFlashCue(transform, true);
        if (_triggered)
        {
            ShowClue();
            return;
        }

        if (_crankRoutine != null) StopCoroutine(_crankRoutine);
        _crankRoutine = StartCoroutine(CrankAndReveal());
    }

    bool ReadInteractEdge(XRNode node, ref bool prev)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
        {
            prev = false;
            return false;
        }
        bool grip = false;
        bool trigger = false;
        device.TryGetFeatureValue(CommonUsages.gripButton, out grip);
        device.TryGetFeatureValue(CommonUsages.triggerButton, out trigger);
        bool pressed = grip || trigger;
        if (!grip && !trigger)
        {
            prev = false;
            return false;
        }

        bool edge = pressed && !prev;
        prev = pressed;
        return edge;
    }

    void UpdateNearCloseDismiss()
    {
        if (_fading || _gotItBtn == null || !_gotItBtn.interactable)
        {
            _nearCloseAccum = 0f;
            return;
        }

        var box = _gotItBtn.GetComponent<BoxCollider>();
        if (box == null)
        {
            _nearCloseAccum = 0f;
            return;
        }

        var b = box.bounds;
        b.Expand(nearCloseBoundsExpand);
        bool near = false;
        if (m_RightController != null) near |= b.Contains(m_RightController.position);
        if (m_LeftController != null) near |= b.Contains(m_LeftController.position);
        if (near) _nearCloseAccum += Time.deltaTime;
        else _nearCloseAccum = 0f;

        if (_nearCloseAccum >= nearCloseDismissHoldSeconds)
            Dismiss();
    }

    void LateUpdate()
    {
        if (Camera.main == null) return;
        Transform cam = Camera.main.transform;

        if (_promptRoot != null && _promptRoot.gameObject.activeSelf)
        {
            Vector3 toCam = cam.position - _promptRoot.position;
            if (toCam.sqrMagnitude > 0.0001f)
                _promptRoot.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
        }

        if (_cluePanel != null && _cluePanel.activeSelf)
        {
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }
    }

    // ── Crank coroutine ───────────────────────────────────────────────────────
    IEnumerator CrankAndReveal()
    {
        _triggered = true;
        if (_promptRoot != null) _promptRoot.gameObject.SetActive(false);

        float      elapsed    = 0f;
        float      totalAngle = m_CrankRotations * 360f;
        Quaternion startRot   = m_Handle.localRotation;
        Vector3    axis       = m_CrankAxis.normalized;

        while (elapsed < m_CrankDuration)
        {
            elapsed += Time.deltaTime;
            float t      = Mathf.Clamp01(elapsed / m_CrankDuration);
            float smooth = m_CrankEase.Evaluate(t);
            m_Handle.localRotation =
                startRot * Quaternion.AngleAxis(smooth * totalAngle, axis);
            yield return null;
        }

        m_Handle.localRotation =
            startRot * Quaternion.AngleAxis(totalAngle % 360f, axis);

        _crankRoutine = null;
        ShowClue();
    }

    IEnumerator CrankOnlyPreview()
    {
        float elapsed = 0f;
        float totalAngle = Mathf.Max(1f, m_CrankRotations) * 180f;
        Quaternion startRot = m_Handle.localRotation;
        Vector3 axis = m_CrankAxis.normalized;

        while (elapsed < m_CrankDuration * 0.8f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / (m_CrankDuration * 0.8f));
            float smooth = m_CrankEase.Evaluate(t);
            m_Handle.localRotation = startRot * Quaternion.AngleAxis(smooth * totalAngle, axis);
            yield return null;
        }

        m_Handle.localRotation = startRot * Quaternion.AngleAxis(totalAngle % 360f, axis);
        _crankRoutine = null;
    }

    // ── Clue popup ────────────────────────────────────────────────────────────
    void ShowClue()
    {
        if (!_reportedDecoy)
        {
            PuzzleManager.ReportDecoyInteraction("decoy_gramophone_handle", this);
            _reportedDecoy = true;
        }
        _cluePanel.SetActive(true);
        ResetPanelCanvasGroup();
        StartCoroutine(FadeInPanelCoroutine());

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        StartCoroutine(EnableGotItAfterDelay(5f));
    }

    IEnumerator EnableGotItAfterDelay(float delay)
    {
        _gotItBtn.interactable = false;
        yield return new WaitForSeconds(delay);
        if (_cluePanel.activeSelf) _gotItBtn.interactable = true;
    }

    void Dismiss()
    {
        if (_fading) return;
        StartCoroutine(FadeOutPanelCoroutine());
        // Decoy — never fires OnClueSolved
    }

    IEnumerator FadeOutPanelCoroutine()
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

    void ResetPanelCanvasGroup()
    {
        var cg = _cluePanel != null ? _cluePanel.GetComponent<CanvasGroup>() : null;
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    // ── Build: floating CRANK prompt ──────────────────────────────────────────
    void BuildPrompt()
    {
        var root = new GameObject("GramophoneHandlePrompt");
        root.transform.position   = transform.position + Vector3.up * 0.30f;
        root.transform.localScale = Vector3.one;

        root.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(0.38f, 0.06f);
        rt.localScale = Vector3.one;

        var txtObj = new GameObject("Txt");
        txtObj.transform.SetParent(root.transform, false);
        var tmp                = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text               = "[ GRIP TO CRANK ]";
        tmp.fontSize           = 0.035f;
        tmp.fontStyle          = FontStyles.Bold;
        tmp.color              = PromptCol;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        var trt      = txtObj.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;

        _promptRoot = root.transform;
        root.SetActive(false);
    }

    // ── Build: clue panel ─────────────────────────────────────────────────────
    void BuildCluePanel()
    {
        var canvasObj = new GameObject("GramophoneDecoyCanvas");
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

        // ── Paper panel ────────────────────────────────────────────────────
        float PW = ClueUiLayout.PanelWidthMeters - 0.04f;
        float PH = ClueUiLayout.PanelHeightMeters - 0.04f;
        var panel = MakeRect("DecoyPanel", canvasObj.transform,
            new Vector2(PW, PH), Vector2.zero, BgPaper);
        _cluePanel = panel;

        // Header
        float headerH = PH * 0.13f;
        float headerY = PH * 0.5f - headerH * 0.5f;
        var header = MakeRect("Header", panel.transform,
            new Vector2(PW, headerH), new Vector2(0f, headerY), HeaderCol);
        MakeLabel("HeaderTxt", header.transform,
            "CLUE", headerH * 0.52f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center, new Vector2(PW * 0.85f, headerH), Vector2.zero);

        // GOT IT button
        float btnH = PH * 0.13f;
        float btnY = -(PH * 0.5f) + btnH * 0.5f + PH * 0.04f;
        _gotItBtn = MakeButton("GotItBtn", "GOT IT",
            panel.transform, new Vector2(0f, btnY), 0.28f, btnH);
        _gotItBtn.onClick.AddListener(Dismiss);
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

        // ── Sketch: gramophone horn ────────────────────────────────────────
        // Stand — vertical post
        const float SY = 0.04f;
        float postX = 0f;
        MakeRect("Post", panel.transform,
            new Vector2(0.010f, 0.090f), new Vector2(postX, SY - 0.035f), SketchCol);

        // Base — horizontal feet
        MakeRect("Base", panel.transform,
            new Vector2(0.090f, 0.010f), new Vector2(postX, SY - 0.080f), SketchCol);

        // Horn — wide flared bell shape drawn as three widening rectangles
        MakeRect("HornNeck", panel.transform,
            new Vector2(0.018f, 0.018f), new Vector2(postX + 0.020f, SY + 0.015f), SketchCol);
        MakeRect("HornMid", panel.transform,
            new Vector2(0.036f, 0.020f), new Vector2(postX + 0.048f, SY + 0.018f), SketchCol);
        MakeRect("HornBell", panel.transform,
            new Vector2(0.052f, 0.040f), new Vector2(postX + 0.085f, SY + 0.022f), SketchCol);

        // Crank handle — small L-shape on the right side of the post
        MakeRect("CrankArm", panel.transform,
            new Vector2(0.036f, 0.008f), new Vector2(postX + 0.022f, SY - 0.010f), SketchCol);
        MakeRect("CrankKnob", panel.transform,
            new Vector2(0.010f, 0.022f), new Vector2(postX + 0.040f, SY - 0.021f), SketchCol);

        // Label
        MakeLabel("GramLbl", panel.transform,
            "GRAMOPHONE", 0.015f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center,
            new Vector2(0.24f, 0.022f), new Vector2(postX, SY - 0.105f));

        // ── Decoy body text ────────────────────────────────────────────────
        float sketchBotY = SY - 0.105f - 0.011f;
        float btnTopY2   = btnY + btnH * 0.5f;
        float bodyH      = (sketchBotY - PH * 0.025f) - (btnTopY2 + PH * 0.025f);
        float bodyY      = (sketchBotY - PH * 0.025f + btnTopY2 + PH * 0.025f) * 0.5f;

        var bodyTmp = MakeLabel("BodyTxt", panel.transform,
            "The music stopped.\nThis is a dead end.\nLook elsewhere for the truth.",
            PH * 0.060f, FontStyles.Italic, Ink,
            TextAlignmentOptions.Center,
            new Vector2(PW - 0.10f, Mathf.Max(bodyH, 0.01f)),
            new Vector2(0f, bodyY));
        bodyTmp.enableAutoSizing = true;
        bodyTmp.fontSizeMin      = PH * 0.030f;
        bodyTmp.fontSizeMax      = PH * 0.060f;

        panel.AddComponent<CanvasGroup>();
        _cluePanel.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static GameObject MakeRect(string name, Transform parent, Vector2 size, Vector2 pos, Color color)
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

    static TextMeshProUGUI MakeLabel(string name, Transform parent,
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

    Button MakeButton(string name, string label, Transform parent, Vector2 pos, float w, float h)
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
        btn.colors = cb;
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

    static void EnsureEventSystem()
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
