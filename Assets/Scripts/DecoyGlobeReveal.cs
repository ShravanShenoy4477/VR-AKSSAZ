using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Decoy globe branch:
/// - Interacting with the wrong globe shows a decoy clue.
/// - That clue nudges toward the telescope and unlocks the telescope decoy path.
/// </summary>
[DisallowMultipleComponent]
public class DecoyGlobeReveal : MonoBehaviour
{
    [Header("Instance lock")]
    [SerializeField] string requiredObjectName = "globe";

    [Header("References")]
    [SerializeField] Collider m_Zone;
    [SerializeField] Transform m_RightController;
    [SerializeField] Transform m_LeftController;
    [SerializeField] Transform m_GlobeSphere;

    [Header("Spin")]
    [SerializeField] float m_SpinDuration = 1.4f;
    [SerializeField] float m_SpinRotations = 2.2f;
    [SerializeField] AnimationCurve m_SpinEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Dismiss")]
    [SerializeField] float panelFadeInSeconds = 0.25f;
    [SerializeField] float panelFadeOutSeconds = 0.55f;
    [SerializeField] float nearCloseDismissHoldSeconds = 0.72f;
    [SerializeField] float nearCloseBoundsExpand = 0.12f;
    [SerializeField] float lockedPulseAmplitude = 0.16f;
    [SerializeField] float lockedPulseDuration = 0.03f;
    [SerializeField] float zoneEdgeSlack = 0.14f;

    static readonly Color BgPaper = MenuThemes.Clue.Background;
    static readonly Color HeaderCol = MenuThemes.Clue.Header;
    static readonly Color InkFaded = MenuThemes.Clue.InkMuted;
    static readonly Color Ink = MenuThemes.Clue.Ink;
    static readonly Color BtnCol = MenuThemes.Clue.Button;
    static readonly Color BtnText = MenuThemes.Clue.ButtonText;
    static readonly Color PromptCol = MenuThemes.Clue.Prompt;

    Canvas _clueCanvas;
    GameObject _cluePanel;
    Button _gotItBtn;
    Transform _promptRoot;
    bool _triggered;
    bool _prevGrip;
    bool _prevGripL;
    bool _reportedDecoy;
    Coroutine _spinRoutine;
    bool _fading;
    float _nearCloseAccum;

    void Awake()
    {
        if (m_GlobeSphere == null)
            m_GlobeSphere = transform;

        if (m_Zone == null)
        {
            m_Zone = GetComponent<Collider>();
            if (m_Zone == null || m_Zone is MeshCollider)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.7f, 0.7f, 0.7f);
                m_Zone = box;
            }
        }
        if (m_Zone is BoxCollider zoneBox)
            zoneBox.size = Vector3.Max(zoneBox.size, new Vector3(0.7f, 0.7f, 0.7f));
        FitZoneToRenderBounds();
        if (m_Zone != null && !m_Zone.isTrigger)
            m_Zone.isTrigger = true;

        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null) m_RightController = go.transform;
        }
        if (m_LeftController == null)
        {
            var go = GameObject.Find("LeftHandController");
            if (go != null) m_LeftController = go.transform;
        }
    }

    void Start()
    {
        if (GetComponent<GlobeClueReveal>() != null
            || GetComponentInParent<GlobeClueReveal>() != null
            || GetComponentInChildren<GlobeClueReveal>(true) != null)
        {
            enabled = false;
            return;
        }

        if (!string.IsNullOrEmpty(requiredObjectName) &&
            gameObject.name.IndexOf(requiredObjectName, System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            enabled = false;
            return;
        }

        BuildPrompt();
        BuildCluePanel();
        EnsureEventSystem();
    }

    void Update()
    {
        bool unlocked = PuzzleManager.IsClueUnlocked(3);
        if (_cluePanel != null && _cluePanel.activeSelf)
            UpdateNearCloseDismiss();

        if (m_Zone == null || (m_RightController == null && m_LeftController == null)) return;

        bool edgeR = ReadGripEdge(XRNode.RightHand, ref _prevGrip);
        bool edgeL = ReadGripEdge(XRNode.LeftHand, ref _prevGripL);
        bool inZoneR = IsHandInZone(m_RightController);
        bool inZoneL = IsHandInZone(m_LeftController);
        bool inZone = inZoneR || inZoneL;
        bool pressedEdge = (edgeR && inZoneR) || (edgeL && inZoneL);

        if (_promptRoot != null && !_triggered)
        {
            bool panelOpen = _cluePanel != null && _cluePanel.activeSelf;
            _promptRoot.gameObject.SetActive(unlocked && inZone && !panelOpen);
        }

        if (!pressedEdge || !inZone) return;
        if (!unlocked)
        {
            InteractableHapticFeedback.ShowWrongOrderCue(transform);
            if (edgeR && inZoneR) XrHaptics.PulseRight(lockedPulseAmplitude, lockedPulseDuration);
            if (edgeL && inZoneL) XrHaptics.PulseLeft(lockedPulseAmplitude, lockedPulseDuration);
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

    bool ReadGripEdge(XRNode node, ref bool prev)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
        {
            prev = false;
            return false;
        }
        if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip))
        {
            prev = false;
            return false;
        }
        bool edge = grip && !prev;
        prev = grip;
        return edge;
    }

    bool IsHandInZone(Transform hand)
    {
        if (hand == null || m_Zone == null) return false;
        if (IsInsideZone(m_Zone, hand.position)) return true;
        var closest = m_Zone.ClosestPoint(hand.position);
        return Vector3.Distance(closest, hand.position) <= zoneEdgeSlack;
    }

    void FitZoneToRenderBounds()
    {
        if (m_Zone == null) return;
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        bool hasBounds = false;
        Bounds worldBounds = default;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (!hasBounds) { worldBounds = r.bounds; hasBounds = true; }
            else worldBounds.Encapsulate(r.bounds);
        }
        if (!hasBounds) return;

        if (m_Zone is BoxCollider box)
        {
            Vector3 worldCenter = worldBounds.center + Vector3.up * (worldBounds.extents.y * 0.25f);
            Vector3 worldSize = worldBounds.size;
            worldSize.y *= 1.35f;
            worldSize.x *= 1.2f;
            worldSize.z *= 1.2f;
            Vector3 lossy = transform.lossyScale;
            float sx = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
            float sy = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
            float sz = Mathf.Max(0.0001f, Mathf.Abs(lossy.z));
            box.center = transform.InverseTransformPoint(worldCenter);
            box.size = new Vector3(worldSize.x / sx, worldSize.y / sy, worldSize.z / sz);
        }
        else if (m_Zone is SphereCollider sphere)
        {
            Vector3 worldCenter = worldBounds.center + Vector3.up * (worldBounds.extents.y * 0.25f);
            sphere.center = transform.InverseTransformPoint(worldCenter);
            float maxExtent = Mathf.Max(worldBounds.extents.x, Mathf.Max(worldBounds.extents.y, worldBounds.extents.z));
            float maxScale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(transform.lossyScale.x),
                Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z))));
            sphere.radius = Mathf.Max(sphere.radius, (maxExtent * 1.2f) / maxScale);
        }
    }

    IEnumerator SpinAndReveal()
    {
        _triggered = true;
        if (_promptRoot != null) _promptRoot.gameObject.SetActive(false);

        float elapsed = 0f;
        float totalAngle = m_SpinRotations * 360f;
        Quaternion startRot = m_GlobeSphere.localRotation;

        while (elapsed < m_SpinDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / m_SpinDuration);
            float smooth = m_SpinEase.Evaluate(t);
            m_GlobeSphere.localRotation = startRot * Quaternion.AngleAxis(smooth * totalAngle, Vector3.up);
            yield return null;
        }

        m_GlobeSphere.localRotation = startRot * Quaternion.AngleAxis(totalAngle % 360f, Vector3.up);
        _spinRoutine = null;
        ShowClue();
    }

    void ShowClue()
    {
        if (!_reportedDecoy)
        {
            PuzzleManager.UnlockDecoyPath("telescope", this);
            _reportedDecoy = true;
        }

        _cluePanel.SetActive(true);
        ResetPanelCanvasGroup();
        StartCoroutine(FadeInPanelCoroutine());
        GameAudioFeedback.PlayCorrectSelection();
        StartCoroutine(EnableGotItAfterDelay(0.75f));

        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _clueCanvas.transform.position = cam.position + cam.forward * ClueUiLayout.PanelForwardMeters;
            _clueCanvas.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }
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

    void BuildPrompt()
    {
        var root = new GameObject("DecoyGlobePrompt");
        root.transform.position = transform.position + Vector3.up * 0.25f;
        root.transform.localScale = Vector3.one;
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0.32f, 0.06f);

        var txtObj = new GameObject("Txt");
        txtObj.transform.SetParent(root.transform, false);
        var tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = "[ GRIP TO SPIN ]";
        tmp.fontSize = 0.035f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = PromptCol;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        var trt = txtObj.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;

        _promptRoot = root.transform;
        root.SetActive(false);
    }

    void BuildCluePanel()
    {
        var canvasObj = new GameObject("DecoyGlobeCanvas");
        canvasObj.transform.position = new Vector3(0f, -1000f, 0f);
        canvasObj.transform.localScale = Vector3.one;

        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        var xrRay = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRay != null) canvasObj.AddComponent(xrRay);

        var canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(ClueUiLayout.PanelWidthMeters, ClueUiLayout.PanelHeightMeters);
        _clueCanvas = canvas;

        float PW = ClueUiLayout.PanelWidthMeters - 0.04f;
        float PH = ClueUiLayout.PanelHeightMeters - 0.04f;
        var panel = MakeRect("DecoyPanel", canvasObj.transform, new Vector2(PW, PH), Vector2.zero, BgPaper);
        _cluePanel = panel;

        float headerH = PH * 0.13f;
        float headerY = PH * 0.5f - headerH * 0.5f;
        var header = MakeRect("Header", panel.transform, new Vector2(PW, headerH), new Vector2(0f, headerY), HeaderCol);
        MakeLabel("HeaderTxt", header.transform, "CLUE", headerH * 0.52f, FontStyles.Bold, InkFaded,
            TextAlignmentOptions.Center, new Vector2(PW * 0.85f, headerH), Vector2.zero);

        float btnH = PH * 0.13f;
        float btnY = -(PH * 0.5f) + btnH * 0.5f + PH * 0.04f;
        _gotItBtn = MakeButton("GotItBtn", "GOT IT", panel.transform, new Vector2(0f, btnY), 0.28f, btnH);
        _gotItBtn.onClick.AddListener(Dismiss);
        _gotItBtn.interactable = false;
        ClueUiLayout.WireGotItButtonForXrDirectSelect(_gotItBtn);

        MakeLabel("BodyTxt", panel.transform,
            "The world yields to careful eyes.\nFind the instrument that narrows your sight,\nalign the lens, and keep searching.",
            PH * 0.060f, FontStyles.Italic, Ink,
            TextAlignmentOptions.Center,
            new Vector2(PW - 0.10f, PH * 0.34f),
            new Vector2(0f, 0.00f));

        panel.AddComponent<CanvasGroup>();
        _cluePanel.SetActive(false);
    }

    void ResetPanelCanvasGroup()
    {
        var cg = _cluePanel != null ? _cluePanel.GetComponent<CanvasGroup>() : null;
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
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

    static bool IsInsideZone(Collider zone, Vector3 worldPoint) =>
        (zone.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude < 1e-6f;

    static GameObject MakeRect(string name, Transform parent, Vector2 size, Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<Image>().color = color;
        var rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return obj;
    }

    static void MakeLabel(string name, Transform parent, string text, float fontSize, FontStyles style, Color color,
        TextAlignmentOptions align, Vector2 size, Vector2 pos)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Truncate;
        var rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    Button MakeButton(string name, string label, Transform parent, Vector2 pos, float w, float h)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<Image>().color = BtnCol;
        var btn = obj.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = BtnCol;
        cb.highlightedColor = Color.Lerp(BtnCol, Color.white, 0.35f);
        cb.pressedColor = Color.Lerp(BtnCol, Color.black, 0.30f);
        cb.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
        btn.colors = cb;
        var rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(obj.transform, false);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.fontSize = h * 0.50f;
        tmp.color = BtnText;
        var trt = textObj.GetComponent<RectTransform>();
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
