using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Attach to the clue book placed under the couch.
/// No XRGrabInteractable or Rigidbody needed — the book stays still.
///
/// Behaviour:
///   - Player approaches within proximityRadius → "[ GRIP TO READ ]" floats above.
///   - Right hand enters zone + grip pressed    → clue card appears in front of headset.
///   - Player presses "GOT IT"                 → card dismissed, OnClueSolved fires.
/// </summary>
[DisallowMultipleComponent]
public class ProximityClueNote : MonoBehaviour
{
    [Header("Proximity")]
    [Tooltip("Distance at which the GRIP TO READ prompt appears (metres).")]
    public float proximityRadius = 1.2f;

    [Tooltip("How close the right hand must be to the book before grip triggers the clue (metres). Keep small so player must actually reach the book.")]
    public float interactRadius = 0.30f;

    [Header("References")]
    [Tooltip("Right hand controller. Auto-finds 'RightHandController' if blank.")]
    [SerializeField] Transform m_RightController;

    [Header("Clue")]
    public int clueIndex = 2;

    public static event System.Action<int> OnClueSolved;

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BgPaper   = new Color(0.96f, 0.93f, 0.84f, 0.98f);
    static readonly Color HeaderCol = new Color(0.20f, 0.14f, 0.08f, 1.00f);
    static readonly Color InkFaded  = new Color(0.45f, 0.32f, 0.14f, 0.80f);
    static readonly Color SketchCol = new Color(0.25f, 0.18f, 0.10f, 0.90f);
    static readonly Color GlobeCol  = new Color(0.22f, 0.45f, 0.70f, 0.85f);
    static readonly Color BtnCol    = new Color(0.25f, 0.18f, 0.10f, 1.00f);
    static readonly Color BtnText   = new Color(0.92f, 0.87f, 0.75f, 1.00f);
    static readonly Color PromptCol = new Color(1.00f, 0.92f, 0.40f, 1.00f);

    // ── Runtime ───────────────────────────────────────────────────────────────
    private Transform  _promptRoot;
    private Canvas     _clueCanvas;
    private GameObject _cluePanel;
    private Button     _gotItBtn;
    private bool       _solved   = false;
    private bool       _prevGrip = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null)
                m_RightController = go.transform;
            else
                Debug.LogWarning("ProximityClueNote: assign Right Controller in Inspector.");
        }
    }

    private void Start()
    {
        BuildPrompt();
        BuildCluePanel();
        EnsureEventSystem();
    }

    // ── Update ────────────────────────────────────────────────────────────────
    private void Update()
    {
        if (_solved || Camera.main == null) return;

        bool panelOpen = _cluePanel != null && _cluePanel.activeSelf;

        // Prompt visibility — distance from camera to book
        if (_promptRoot != null)
        {
            float dist = Vector3.Distance(Camera.main.transform.position, transform.position);
            _promptRoot.gameObject.SetActive(dist < proximityRadius && !panelOpen);
        }

        if (panelOpen || m_RightController == null) return;

        // Grip detection — same pattern as clock and sofa
        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return;
        if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip)) return;

        bool pressedEdge = grip && !_prevGrip;
        _prevGrip = grip;

        if (!pressedEdge) return;

        // Hand must be within interactRadius of the book centre — player must physically reach it
        float handDist = Vector3.Distance(m_RightController.position, transform.position);
        if (handDist > interactRadius) return;

        ShowClue();
    }

    private void LateUpdate()
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
            _clueCanvas.transform.position = cam.position + cam.forward * 0.6f;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }
    }

    // ── Interaction ───────────────────────────────────────────────────────────
    private void ShowClue()
    {
        if (_solved) return;
        _cluePanel.SetActive(true);
        if (_promptRoot != null) _promptRoot.gameObject.SetActive(false);

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * 0.6f;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        StartCoroutine(EnableGotItAfterDelay(5f));
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

    // ── Build: floating prompt ────────────────────────────────────────────────
    private void BuildPrompt()
    {
        var root = new GameObject("BookPrompt");
        root.transform.position   = transform.position + Vector3.up * 0.20f;
        root.transform.localScale = Vector3.one;

        root.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(0.36f, 0.06f);
        rt.localScale = Vector3.one;

        var txtObj = new GameObject("Txt");
        txtObj.transform.SetParent(root.transform, false);
        var tmp                = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text               = "[ GRIP TO READ ]";
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
    private void BuildCluePanel()
    {
        var canvasObj = new GameObject("ClueCanvas_Clue2");
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
        var panel = MakeRect("CluePanel", canvasObj.transform,
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

        // ── Sketch: CORNER → TABLE + GLOBE ────────────────────────────────
        const float SY = 0.03f;

        float cx = -0.17f, cy = SY - 0.06f;
        MakeRect("CornerH", panel.transform,
            new Vector2(0.16f, 0.011f), new Vector2(cx + 0.08f, cy), SketchCol);
        MakeRect("CornerV", panel.transform,
            new Vector2(0.011f, 0.16f), new Vector2(cx, cy + 0.08f), SketchCol);
        MakeLabel("CornerLbl", panel.transform,
            "CORNER", 0.017f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center,
            new Vector2(0.14f, 0.025f), new Vector2(cx + 0.07f, cy + 0.17f));

        MakeLabel("Arrow", panel.transform, "→",
            0.050f, FontStyles.Bold, SketchCol,
            TextAlignmentOptions.Center,
            new Vector2(0.07f, 0.06f), new Vector2(0.01f, SY));

        float tx = 0.11f;
        MakeRect("TableTop",  panel.transform,
            new Vector2(0.20f, 0.011f),  new Vector2(tx, SY - 0.01f),   SketchCol);
        MakeRect("TableLegL", panel.transform,
            new Vector2(0.011f, 0.075f), new Vector2(tx - 0.08f, SY - 0.053f), SketchCol);
        MakeRect("TableLegR", panel.transform,
            new Vector2(0.011f, 0.075f), new Vector2(tx + 0.08f, SY - 0.053f), SketchCol);
        MakeLabel("TableLbl", panel.transform,
            "TABLE", 0.017f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center,
            new Vector2(0.16f, 0.025f), new Vector2(tx, SY - 0.110f));

        MakeRect("GlobeBg", panel.transform,
            new Vector2(0.072f, 0.072f), new Vector2(tx, SY + 0.055f), GlobeCol);
        MakeLabel("GlobeDot", panel.transform, "●",
            0.050f, FontStyles.Normal, new Color(0.10f, 0.28f, 0.55f, 1f),
            TextAlignmentOptions.Center,
            new Vector2(0.072f, 0.072f), new Vector2(tx, SY + 0.055f));

        _cluePanel.SetActive(false);
    }

    // ── UI helpers ────────────────────────────────────────────────────────────
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

    static void MakeLabel(string name, Transform parent,
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
        tmp.enableWordWrapping = false;
        var rt                 = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
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
