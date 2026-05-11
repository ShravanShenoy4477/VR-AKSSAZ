using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Attach to the CD / turntable prop on or near the gramophone.
///
/// Behaviour:
///   - Right hand enters the zone + grip pressed → CD spins.
///   - After the spin completes → decoy clue popup appears in front of the headset.
///   - Popup looks identical to a real clue but the message is a red herring.
///   - "GOT IT" dismisses the popup.
///
/// No OnClueSolved fired — decoys never count toward puzzle progress.
///
/// Setup:
///   1. Attach to the CD / disc GameObject near the gramophone.
///   2. Assign Right Controller (or name it "RightHandController").
///   3. Optionally assign CD Disc — the child mesh to spin.
///      Leave blank to spin the whole object.
///   4. Resize the Zone collider so it covers arm-reach.
/// </summary>
[DisallowMultipleComponent]
public class DecoyGramophoneCD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Proximity zone collider. Defaults to this GameObject's collider.")]
    [SerializeField] Collider  m_Zone;

    [Tooltip("Right hand controller. Auto-finds 'RightHandController' if blank.")]
    [SerializeField] Transform m_RightController;
    [Tooltip("Left hand controller. Auto-finds 'LeftHandController' if blank.")]
    [SerializeField] Transform m_LeftController;

    [Tooltip("The disc mesh to spin. Leave blank to spin the whole object.")]
    [SerializeField] Transform m_CDDisc;

    [Header("Spin")]
    [Tooltip("How long the spin animation lasts (seconds).")]
    [SerializeField] float m_SpinDuration  = 1.4f;

    [Tooltip("How many full rotations the disc makes.")]
    [SerializeField] float m_SpinRotations = 4f;

    [SerializeField] AnimationCurve m_SpinEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── Colours — warm cream, matches real clues so player trusts it ──────────
    static readonly Color BgPaper   = MenuThemes.Clue.Background;
    static readonly Color HeaderCol = MenuThemes.Clue.Header;
    static readonly Color InkFaded  = MenuThemes.Clue.InkMuted;
    static readonly Color Ink       = MenuThemes.Clue.Ink;
    static readonly Color CDCol     = MenuThemes.Clue.CdOuter;  // silver disc
    static readonly Color CDHole    = MenuThemes.Clue.CdHole;   // centre hole
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
    private Coroutine  _spinRoutine;
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
        if (m_CDDisc == null)
            m_CDDisc = transform;

        // Zone collider
        if (m_Zone == null)
        {
            m_Zone = GetComponent<Collider>();
            if (m_Zone == null)
            {
                var box  = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.7f, 0.7f, 0.7f);
                m_Zone   = box;
            }
        }

        // Right controller
        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null)
                m_RightController = go.transform;
            else
                Debug.LogWarning("DecoyGramophoneCD: assign Right Controller in the Inspector.");
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

        if (m_Zone == null || (m_RightController == null && m_LeftController == null)) return;

        bool edgeR = ReadInteractEdge(XRNode.RightHand, ref _prevGrip);
        bool edgeL = ReadInteractEdge(XRNode.LeftHand, ref _prevGripL);
        bool inZoneR = m_RightController != null && IsInsideZone(m_Zone, m_RightController.position);
        bool inZoneL = m_LeftController != null && IsInsideZone(m_Zone, m_LeftController.position);
        bool inZone = inZoneR || inZoneL;
        bool pressedEdge = (edgeR && inZoneR) || (edgeL && inZoneL);

        // Show / hide SPIN prompt based on proximity
        if (_promptRoot != null && !_triggered)
        {
            bool panelOpen = _cluePanel != null && _cluePanel.activeSelf;
            _promptRoot.gameObject.SetActive(inZone && !panelOpen);
        }

        if (!pressedEdge || !inZone) return;
        if (!unlocked)
        {
            InteractableHapticFeedback.ShowWrongOrderCue(transform);
            if (edgeR && inZoneR)
            {
                XrHaptics.PulseRight(lockedPulseAmplitude, lockedPulseDuration);
            }
            if (edgeL && inZoneL)
            {
                XrHaptics.PulseLeft(lockedPulseAmplitude, lockedPulseDuration);
            }
            if (_spinRoutine != null) StopCoroutine(_spinRoutine);
            _spinRoutine = StartCoroutine(SpinOnlyPreview());
            return;
        }
        InteractableHapticFeedback.ShowTargetFlashCue(transform, true);
        if (_triggered)
        {
            if (_cluePanel == null || !_cluePanel.activeSelf)
                ShowClue();
            return;
        }

        if (_spinRoutine != null) StopCoroutine(_spinRoutine);
        _spinRoutine = StartCoroutine(SpinAndReveal());
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

        // Prompt always faces camera
        if (_promptRoot != null && _promptRoot.gameObject.activeSelf)
        {
            Vector3 toCam = cam.position - _promptRoot.position;
            if (toCam.sqrMagnitude > 0.0001f)
                _promptRoot.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
        }

        // Clue panel follows headset
        if (_cluePanel != null && _cluePanel.activeSelf)
        {
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }
    }

    // ── Spin coroutine ────────────────────────────────────────────────────────
    IEnumerator SpinAndReveal()
    {
        _triggered = true;
        if (_promptRoot != null) _promptRoot.gameObject.SetActive(false);

        float      elapsed    = 0f;
        float      totalAngle = m_SpinRotations * 360f;
        Quaternion startRot   = m_CDDisc.localRotation;

        // CD spins on its local Y axis (flat disc spinning like a record)
        while (elapsed < m_SpinDuration)
        {
            elapsed += Time.deltaTime;
            float t      = Mathf.Clamp01(elapsed / m_SpinDuration);
            float smooth = m_SpinEase.Evaluate(t);
            m_CDDisc.localRotation =
                startRot * Quaternion.AngleAxis(smooth * totalAngle, Vector3.up);
            yield return null;
        }

        m_CDDisc.localRotation =
            startRot * Quaternion.AngleAxis(totalAngle % 360f, Vector3.up);

        _spinRoutine = null;
        ShowClue();
    }

    IEnumerator SpinOnlyPreview()
    {
        float elapsed = 0f;
        float totalAngle = Mathf.Max(1f, m_SpinRotations) * 180f;
        Quaternion startRot = m_CDDisc.localRotation;

        while (elapsed < m_SpinDuration * 0.8f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / (m_SpinDuration * 0.8f));
            float smooth = m_SpinEase.Evaluate(t);
            m_CDDisc.localRotation = startRot * Quaternion.AngleAxis(smooth * totalAngle, Vector3.up);
            yield return null;
        }

        m_CDDisc.localRotation = startRot * Quaternion.AngleAxis(totalAngle % 360f, Vector3.up);
        _spinRoutine = null;
    }

    // ── Clue popup ────────────────────────────────────────────────────────────
    void ShowClue()
    {
        if (!_reportedDecoy)
        {
            PuzzleManager.ReportDecoyInteraction("decoy_gramophone_cd", this);
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

        GameAudioFeedback.PlayDecoyReveal();
        StartCoroutine(EnableGotItAfterDelay(0.75f));
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
        // Decoys do NOT fire OnClueSolved — progress is never incremented
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

    // ── Build: floating SPIN prompt ───────────────────────────────────────────
    void BuildPrompt()
    {
        var root = new GameObject("GramophonePrompt");
        root.transform.position   = transform.position + Vector3.up * 0.25f;
        root.transform.localScale = Vector3.one;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(0.32f, 0.06f);
        rt.localScale = Vector3.one;

        var txtObj = new GameObject("Txt");
        txtObj.transform.SetParent(root.transform, false);
        var tmp                = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text               = "[ GRIP TO SPIN ]";
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

        // Header — says "CLUE" like the real ones
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

        // ── Sketch: CD disc ────────────────────────────────────────────────
        const float SY = 0.06f;
        float cx = 0f;  // centred horizontally

        // Outer silver disc
        MakeRect("CDOuter", panel.transform,
            new Vector2(0.100f, 0.100f), new Vector2(cx, SY), CDCol);
        // Inner groove ring
        MakeRect("CDMid", panel.transform,
            new Vector2(0.068f, 0.068f), new Vector2(cx, SY),
            MenuThemes.Clue.CdInner);
        // Centre hole
        MakeRect("CDHole", panel.transform,
            new Vector2(0.022f, 0.022f), new Vector2(cx, SY), CDHole);

        // Label below disc
        MakeLabel("CDLbl", panel.transform,
            "DISC", 0.018f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center,
            new Vector2(0.14f, 0.026f), new Vector2(cx, SY + 0.070f));

        // ── Decoy body text ────────────────────────────────────────────────
        // Positioned below the sketch, above the button
        float bodyGap    = PH * 0.03f;
        float sketchBotY = SY - 0.070f;           // approx bottom of sketch area
        float btnTopY    = btnY + btnH * 0.5f;
        float bodyH      = (sketchBotY - bodyGap) - (btnTopY + bodyGap);
        float bodyY      = (sketchBotY - bodyGap + btnTopY + bodyGap) * 0.5f;

        var bodyTmp = MakeLabel("BodyTxt", panel.transform,
            "The music stopped long ago.\nThe answer you seek\ndoes not spin here.",
            PH * 0.060f, FontStyles.Italic, Ink,
            TextAlignmentOptions.Center,
            new Vector2(PW - 0.08f * 2f, Mathf.Max(bodyH, 0.01f)),
            new Vector2(0f, bodyY));
        bodyTmp.enableAutoSizing = true;
        bodyTmp.fontSizeMin      = PH * 0.030f;
        bodyTmp.fontSizeMax      = PH * 0.060f;

        panel.AddComponent<CanvasGroup>();
        _cluePanel.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static bool IsInsideZone(Collider zone, Vector3 worldPoint) =>
        (zone.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude < 1e-6f;

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
        tmp.overflowMode = TextOverflowModes.Truncate;
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
