using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Attach to the clue book placed under the couch.
/// This object is grabbable/liftable via XRGrabInteractable + Rigidbody.
///
/// Behaviour:
///   - Player approaches within proximityRadius → "[ GRIP TO READ ]" floats above.
///   - Right hand enters zone + grip pressed    → clue card appears in front of headset.
///   - Player presses "GOT IT"                 → card dismissed, PuzzleManager is notified.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
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
    [Tooltip("Left hand controller. Auto-finds 'LeftHandController' if blank.")]
    [SerializeField] Transform m_LeftController;

    [Header("Clue")]
    public int clueIndex = 2;

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color BgPaper   = MenuThemes.Clue.Background;
    static readonly Color HeaderCol = MenuThemes.Clue.Header;
    static readonly Color InkFaded  = MenuThemes.Clue.InkMuted;
    static readonly Color SketchCol = MenuThemes.Clue.Sketch;
    static readonly Color GlobeCol  = MenuThemes.Clue.Globe;
    static readonly Color BtnCol    = MenuThemes.Clue.Button;
    static readonly Color BtnText   = MenuThemes.Clue.ButtonText;
    static readonly Color PromptCol = MenuThemes.Clue.Prompt;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private Transform  _promptRoot;
    private Canvas     _clueCanvas;
    private GameObject _cluePanel;
    private Button     _gotItBtn;
    private XRGrabInteractable _grab;
    private Rigidbody _rb;
    private Collider[] _bookColliders;
    private bool[]     _bookColliderTriggerDefaults;
    private bool       _rbKinematicDefault;
    private bool       _rbDetectCollisionsDefault;
    private bool       _progressSent;
    private Coroutine  _holdRevealCo;
    private bool       _fading;
    private float      _nearCloseAccum;
    private bool       _gotItDelayElapsed;

    [Header("Read timing")]
    [SerializeField] float minHoldSecondsForClueReveal = 0.55f;

    [Header("Dismiss")]
    [SerializeField] float panelFadeInSeconds = 0.25f;
    [SerializeField] float panelFadeOutSeconds = 0.55f;
    [SerializeField] float nearCloseDismissHoldSeconds = 0.72f;
    [SerializeField] float nearCloseBoundsExpand = 0.12f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        ConfigureGrabForLift();

        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null)
                m_RightController = go.transform;
            else
                Debug.LogWarning("ProximityClueNote: assign Right Controller in Inspector.");
        }

        if (m_LeftController == null)
        {
            var go = GameObject.Find("LeftHandController");
            if (go != null)
                m_LeftController = go.transform;
        }
    }

    void ConfigureGrabForLift()
    {
        _grab = GetComponent<XRGrabInteractable>();

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
        _grab.enabled = true;
        _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        _grab.trackPosition = true;
        _grab.trackRotation = false;
        _grab.throwOnDetach = false;
        _grab.forceGravityOnDetach = true;
        _grab.retainTransformParent = true;
        _grab.selectEntered.AddListener(OnBookSelectEntered);
        _grab.selectExited.AddListener(OnBookSelectExited);

        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.constraints = RigidbodyConstraints.None;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.linearDamping = 2.5f;
        _rb.angularDamping = 2.5f;
        _rbKinematicDefault = _rb.isKinematic;
        _rbDetectCollisionsDefault = _rb.detectCollisions;

        EnsureGrabCollider();
        CacheBookColliders();
    }

    void CacheBookColliders()
    {
        _bookColliders = GetComponentsInChildren<Collider>(true);
        _bookColliderTriggerDefaults = new bool[_bookColliders.Length];
        for (int i = 0; i < _bookColliders.Length; i++)
            _bookColliderTriggerDefaults[i] = _bookColliders[i] != null && _bookColliders[i].isTrigger;
    }

    void EnsureGrabCollider()
    {
        var cols = GetComponentsInChildren<Collider>(true);
        bool hasNonTrigger = false;
        foreach (var c in cols)
        {
            if (c == null) continue;
            if (!c.isTrigger)
            {
                hasNonTrigger = true;
                break;
            }
        }

        if (hasNonTrigger) return;

        // If authoring left only trigger colliders, add a solid grab collider automatically.
        var box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = Vector3.zero;
        box.size = new Vector3(0.16f, 0.05f, 0.22f);
    }

    void OnBookSelectEntered(SelectEnterEventArgs _)
    {
        // Prevent grabbed-book collision feedback from physically shoving the player rig.
        SetBookCollidersTriggerOnly(true);
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.detectCollisions = false;
        }

        if (_progressSent)
        {
            ShowClueAfterSolved();
            return;
        }

        if (_holdRevealCo != null) StopCoroutine(_holdRevealCo);
        if (_cluePanel != null && _cluePanel.activeSelf) return;
        _holdRevealCo = StartCoroutine(RevealWhileHeldAfterMinHold());
    }

    void OnBookSelectExited(SelectExitEventArgs _)
    {
        if (_rb != null)
        {
            _rb.isKinematic = _rbKinematicDefault;
            _rb.detectCollisions = _rbDetectCollisionsDefault;
        }
        SetBookCollidersTriggerOnly(false);

        if (_holdRevealCo != null)
        {
            StopCoroutine(_holdRevealCo);
            _holdRevealCo = null;
        }
    }

    IEnumerator RevealWhileHeldAfterMinHold()
    {
        try
        {
            yield return new WaitForSeconds(minHoldSecondsForClueReveal);
            if (_progressSent) yield break;
            if (!IsBookHeld()) yield break;
            if (_cluePanel != null && _cluePanel.activeSelf) yield break;
            ShowClueFirstTime();
        }
        finally
        {
            _holdRevealCo = null;
        }
    }

    void SetBookCollidersTriggerOnly(bool triggerOnly)
    {
        if (_bookColliders == null || _bookColliderTriggerDefaults == null) return;
        for (int i = 0; i < _bookColliders.Length; i++)
        {
            var c = _bookColliders[i];
            if (c == null) continue;
            c.isTrigger = triggerOnly ? true : _bookColliderTriggerDefaults[i];
        }
    }

    private void Start()
    {
        BuildPrompt();
        BuildCluePanel();
        EnsureEventSystem();
    }

    private void OnDestroy()
    {
        if (_holdRevealCo != null) StopCoroutine(_holdRevealCo);
        if (_grab == null) return;
        _grab.selectEntered.RemoveListener(OnBookSelectEntered);
        _grab.selectExited.RemoveListener(OnBookSelectExited);
    }

    // ── Update ────────────────────────────────────────────────────────────────
    private void Update()
    {
        if (Camera.main == null) return;
        bool unlocked = PuzzleManager.IsClueUnlocked(clueIndex);

        bool panelOpen = _cluePanel != null && _cluePanel.activeSelf;

        // Prompt visibility — distance from camera to book
        if (_promptRoot != null)
        {
            float dist = Vector3.Distance(Camera.main.transform.position, transform.position);
            _promptRoot.gameObject.SetActive(unlocked && dist < proximityRadius && !panelOpen);
        }

        if (panelOpen)
        {
            RefreshCloseButtonInteractable();
            UpdateNearCloseDismiss();
            return;
        }
    }

    void UpdateNearCloseDismiss()
    {
        if (IsBookHeld() || _fading || _gotItBtn == null || !_gotItBtn.interactable)
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
            HandleCloseIntent();
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
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }
    }

    // ── Interaction ───────────────────────────────────────────────────────────
    private void ShowClueFirstTime()
    {
        _cluePanel.SetActive(true);
        ResetPanelCanvasGroup();
        StartCoroutine(FadeInPanelCoroutine());
        if (_promptRoot != null) _promptRoot.gameObject.SetActive(false);

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        _gotItBtn.onClick.RemoveAllListeners();
        _gotItBtn.onClick.AddListener(Dismiss);
        _gotItDelayElapsed = false;
        StartCoroutine(EnableGotItAfterDelay(0.75f));
    }

    private void ShowClueAfterSolved()
    {
        _cluePanel.SetActive(true);
        ResetPanelCanvasGroup();
        StartCoroutine(FadeInPanelCoroutine());
        if (_promptRoot != null) _promptRoot.gameObject.SetActive(false);

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        _gotItBtn.onClick.RemoveAllListeners();
        _gotItBtn.onClick.AddListener(HidePanelOnly);
        _gotItDelayElapsed = true;
        RefreshCloseButtonInteractable();
    }

    private IEnumerator EnableGotItAfterDelay(float delay)
    {
        _gotItBtn.interactable = false;
        yield return new WaitForSeconds(delay);
        _gotItDelayElapsed = true;
        RefreshCloseButtonInteractable();
    }

    private void Dismiss()
    {
        if (IsBookHeld() || _progressSent || _fading) return;
        _progressSent = true;
        StartCoroutine(FadeOutPanelCoroutine(true));
    }

    void HidePanelOnly()
    {
        if (IsBookHeld() || _fading) return;
        StartCoroutine(FadeOutPanelCoroutine(false));
    }

    void HandleCloseIntent()
    {
        if (IsBookHeld()) return;
        if (_progressSent) HidePanelOnly();
        else Dismiss();
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

    void ResetPanelCanvasGroup()
    {
        var cg = _cluePanel != null ? _cluePanel.GetComponent<CanvasGroup>() : null;
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    bool IsBookHeld()
    {
        return _grab != null && _grab.isSelected;
    }

    void RefreshCloseButtonInteractable()
    {
        if (_gotItBtn == null) return;
        _gotItBtn.interactable = _gotItDelayElapsed && !IsBookHeld() && !_fading;
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
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
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
        canvasRT.sizeDelta  = new Vector2(ClueUiLayout.PanelWidthMeters, ClueUiLayout.PanelHeightMeters);
        canvasRT.localScale = Vector3.one;
        _clueCanvas = canvas;

        // ── Paper panel ────────────────────────────────────────────────────
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
            "CLUE", headerH * 0.52f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center, new Vector2(PW * 0.85f, headerH), Vector2.zero);

        float btnH = Mathf.Max(PH * 0.18f, 0.10f);
        float btnW = Mathf.Max(PW * 0.78f, 0.26f);
        float btnY = -(PH * 0.5f) + btnH * 0.5f + PH * 0.03f;
        _gotItBtn = MakeButton("GotItBtn", "GOT IT",
            panel.transform, new Vector2(0f, btnY), btnW, btnH);
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

        panel.AddComponent<CanvasGroup>();
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
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
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
