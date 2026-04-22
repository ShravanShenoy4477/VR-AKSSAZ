using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Attach to the Globe prop on the corner table.
///
/// Behaviour:
///   - Right hand enters the zone + grip pressed → globe spins.
///   - After the spin completes → clue popup appears in front of the headset.
///   - "GOT IT" dismisses the popup and fires OnClueSolved(clueIndex).
///
/// Setup:
///   1. Attach this script to the Globe GameObject.
///   2. Assign Right Controller (or name it "RightHandController").
///   3. Optionally assign Globe Sphere — the child mesh to spin.
///      Leave blank to spin the whole Globe object.
///   4. Resize the Zone collider in the Inspector so it covers arm-reach.
/// </summary>
[DisallowMultipleComponent]
public class GlobeClueReveal : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Proximity zone collider. Defaults to this GameObject's collider.")]
    [SerializeField] Collider   m_Zone;

    [Tooltip("Right hand controller. Auto-finds 'RightHandController' if blank.")]
    [SerializeField] Transform  m_RightController;

    [Tooltip("The sphere child to spin. Leave blank to spin the whole Globe.")]
    [SerializeField] Transform  m_GlobeSphere;

    [Header("Spin")]
    [Tooltip("How long the spin animation lasts (seconds).")]
    [SerializeField] float m_SpinDuration  = 1.8f;

    [Tooltip("How many full rotations the globe makes.")]
    [SerializeField] float m_SpinRotations = 3f;

    [SerializeField] AnimationCurve m_SpinEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Clue")]
    [TextArea(2, 5)]
    [SerializeField] string m_ClueText =
        "Time stands still for those who look closely —\n" +
        "the face on the wall knows what lies behind it.";

    public int clueIndex = 3;

    /// <summary>Fired once when the player dismisses this clue.</summary>
    public static event System.Action<int> OnClueSolved;

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BgPaper   = new Color(0.96f, 0.93f, 0.84f, 0.98f);
    static readonly Color HeaderCol = new Color(0.20f, 0.14f, 0.08f, 1.00f);
    static readonly Color InkFaded  = new Color(0.45f, 0.32f, 0.14f, 0.80f);
    static readonly Color Ink       = new Color(0.12f, 0.10f, 0.08f, 1.00f);
    static readonly Color BtnCol    = new Color(0.25f, 0.18f, 0.10f, 1.00f);
    static readonly Color BtnText   = new Color(0.92f, 0.87f, 0.75f, 1.00f);
    static readonly Color PromptCol = new Color(1.00f, 0.92f, 0.40f, 1.00f);

    // ── Runtime ───────────────────────────────────────────────────────────────
    private Canvas     _clueCanvas;
    private GameObject _cluePanel;
    private Button     _gotItBtn;
    private Transform  _promptRoot;
    private bool       _triggered = false;
    private bool       _prevGrip  = false;
    private Coroutine  _spinRoutine;

    // ── Awake ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (m_GlobeSphere == null)
            m_GlobeSphere = transform;

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
                Debug.LogWarning("GlobeClueReveal: assign Right Controller in the Inspector.");
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
        // Show / hide SPIN prompt based on proximity
        if (_promptRoot != null && !_triggered)
        {
            bool panelOpen = _cluePanel != null && _cluePanel.activeSelf;
            bool inRange   = m_Zone != null && m_RightController != null &&
                             IsInsideZone(m_Zone, m_RightController.position);
            _promptRoot.gameObject.SetActive(inRange && !panelOpen);
        }

        if (_triggered || m_Zone == null || m_RightController == null) return;

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return;
        if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip)) return;

        bool pressedEdge = grip && !_prevGrip;
        _prevGrip = grip;

        if (!pressedEdge) return;
        if (!IsInsideZone(m_Zone, m_RightController.position)) return;

        if (_spinRoutine != null) StopCoroutine(_spinRoutine);
        _spinRoutine = StartCoroutine(SpinAndReveal());
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
            _clueCanvas.transform.position = cam.position + cam.forward * 0.6f;
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
        Quaternion startRot   = m_GlobeSphere.localRotation;

        while (elapsed < m_SpinDuration)
        {
            elapsed += Time.deltaTime;
            float t      = Mathf.Clamp01(elapsed / m_SpinDuration);
            float smooth = m_SpinEase.Evaluate(t);
            m_GlobeSphere.localRotation =
                startRot * Quaternion.AngleAxis(smooth * totalAngle, Vector3.up);
            yield return null;
        }

        // Land on a clean final rotation
        m_GlobeSphere.localRotation =
            startRot * Quaternion.AngleAxis(totalAngle % 360f, Vector3.up);

        _spinRoutine = null;
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
        _gotItBtn.interactable = true;
    }

    void Dismiss()
    {
        _cluePanel.SetActive(false);
        OnClueSolved?.Invoke(clueIndex);
    }

    // ── Build: floating SPIN prompt ───────────────────────────────────────────
    void BuildPrompt()
    {
        var root = new GameObject("GlobePrompt");
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
        var canvasObj = new GameObject("GlobeClueCanvas");
        canvasObj.transform.position  = new Vector3(0f, -1000f, 0f);
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

        // Body text — auto-sized to fit between header and button
        float bodyGap    = PH * 0.03f;
        float headerBotY = headerY - headerH * 0.5f;
        float btnTopY    = btnY    + btnH   * 0.5f;
        float bodyH      = (headerBotY - bodyGap) - (btnTopY + bodyGap);
        float bodyY      = (headerBotY - bodyGap + btnTopY + bodyGap) * 0.5f;
        var bodyTmp = MakeLabel("BodyTxt", panel.transform,
            m_ClueText, PH * 0.065f, FontStyles.Italic, Ink,
            TextAlignmentOptions.Center,
            new Vector2(PW - 0.08f * 2f, Mathf.Max(bodyH, 0.01f)),
            new Vector2(0f, bodyY));
        bodyTmp.enableAutoSizing = true;
        bodyTmp.fontSizeMin      = PH * 0.035f;
        bodyTmp.fontSizeMax      = PH * 0.065f;

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
