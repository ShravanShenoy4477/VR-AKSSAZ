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
    static readonly Color BgPaper   = new Color(0.95f, 0.91f, 0.80f, 0.98f);
    static readonly Color HeaderCol = new Color(0.20f, 0.14f, 0.08f, 1.00f);
    static readonly Color InkFaded  = new Color(0.45f, 0.32f, 0.14f, 0.80f);
    static readonly Color Ink       = new Color(0.12f, 0.10f, 0.08f, 1.00f);
    static readonly Color SketchCol = new Color(0.25f, 0.18f, 0.10f, 0.90f);
    static readonly Color BtnCol    = new Color(0.25f, 0.18f, 0.10f, 1.00f);
    static readonly Color BtnText   = new Color(0.92f, 0.87f, 0.75f, 1.00f);
    static readonly Color PromptCol = new Color(1.00f, 0.92f, 0.40f, 1.00f);

    // ── Runtime ───────────────────────────────────────────────────────────────
    private Canvas     _clueCanvas;
    private GameObject _cluePanel;
    private Button     _gotItBtn;
    private Transform  _promptRoot;
    private bool       _triggered  = false;
    private bool       _prevGrip   = false;
    private Coroutine  _crankRoutine;

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
        if (_triggered || Camera.main == null) return;

        bool panelOpen = _cluePanel != null && _cluePanel.activeSelf;

        // Prompt: show when camera is within proximityRadius of the handle
        if (_promptRoot != null)
        {
            float camDist = Vector3.Distance(Camera.main.transform.position, transform.position);
            _promptRoot.gameObject.SetActive(camDist < proximityRadius && !panelOpen);
        }

        if (panelOpen || m_RightController == null) return;

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return;
        if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip)) return;

        bool pressedEdge = grip && !_prevGrip;
        _prevGrip = grip;

        if (!pressedEdge) return;

        // Only trigger when hand is physically close to the handle
        float handDist = Vector3.Distance(m_RightController.position, transform.position);
        if (handDist > interactRadius) return;

        if (_crankRoutine != null) StopCoroutine(_crankRoutine);
        _crankRoutine = StartCoroutine(CrankAndReveal());
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
            _clueCanvas.transform.position = cam.position + cam.forward * 0.6f;
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

    // ── Clue popup ────────────────────────────────────────────────────────────
    void ShowClue()
    {
        _cluePanel.SetActive(true);

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * 0.6f;
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
        _cluePanel.SetActive(false);
        // Decoy — never fires OnClueSolved
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
        tmp.enableWordWrapping = false;
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
        canvasRT.sizeDelta  = new Vector2(0.60f, 0.80f);
        canvasRT.localScale = Vector3.one;
        _clueCanvas = canvas;

        // ── Paper panel ────────────────────────────────────────────────────
        const float PW = 0.54f, PH = 0.74f;
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
        tmp.enableWordWrapping = true;
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

        var box       = obj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size      = new Vector3(w, h, 0.05f);

        return btn;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
