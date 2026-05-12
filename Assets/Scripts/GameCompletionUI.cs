using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// Auto-spawns at runtime and listens for DoorProximityHinge.OnDoorOpened.
/// When the door opens it shows a congratulations panel in front of the player
/// with a Play Again button that restarts the scene.
/// Interaction uses XR UI ray (TrackedDeviceGraphicRaycaster + XRUIInputModule).
/// </summary>
public class GameCompletionUI : MonoBehaviour
{
    // ── Colours (matching FloatingAutoTimer theme) ────────────────────────
    static readonly Color BgDark       = MenuThemes.Completion.Background;
    static readonly Color HeaderColor  = MenuThemes.Completion.Header;
    static readonly Color AccentPurple = MenuThemes.Completion.AccentPrimary;
    static readonly Color AccentTeal   = MenuThemes.Completion.Button;
    static readonly Color GoldText     = MenuThemes.Completion.AccentPrimary;
    static readonly Color TextSub      = MenuThemes.Completion.TextSecondary;

    private Canvas           _canvas;
    private GameObject       _panel;
    private TextMeshProUGUI  _titleLine;
    private TextMeshProUGUI  _subtitleLine;
    private TextMeshProUGUI  _scoreLine;
    private TextMeshProUGUI  _breakdownLine;
    private bool             _restartInProgress;
    private bool             _capturedInitialPose;
    private Vector3          _initialRigPos;
    private Quaternion       _initialRigRot;

    [Header("Placement")]
    [SerializeField] private bool useFixedWorldTransform = false;
    [SerializeField] private Vector3 fixedWorldPosition = new Vector3(-5.857f, 1.468f, -3.285f);
    [SerializeField] private Vector3 fixedWorldRotation = new Vector3(0f, 0f, -90f);
    [SerializeField] private bool anchorNearDoor = true;
    [SerializeField] private Vector3 doorMenuOffset = new Vector3(0.34f, 0.02f, 0.06f);

    // ── Bootstrap ─────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        var obj = new GameObject("[GameCompletionUI]");
        DontDestroyOnLoad(obj);
        obj.AddComponent<GameCompletionUI>();
    }

    private void Awake()
    {
        BuildUI();
        DoorProximityHinge.OnDoorOpened += ShowCompletionScreen;
        SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(CaptureInitialRigPoseWhenReady());
    }

    private void OnDestroy()
    {
        DoorProximityHinge.OnDoorOpened -= ShowCompletionScreen;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_capturedInitialPose)
            StartCoroutine(CaptureInitialRigPoseWhenReady());

        if (_restartInProgress)
            StartCoroutine(RestoreInitialRigPoseWhenReady());
    }

    // ── Show ──────────────────────────────────────────────────────────────
    private void ShowCompletionScreen()
    {
        // ── Score calculation ──────────────────────────────────────────
        int   cluesSolved  = PuzzleManager.SolvedClueCount;
        float minsLeft     = FloatingAutoTimer.TimeRemaining / 60f;
        int   hintsUsed    = FloatingAutoTimer.HintsUsed;
        float score        = cluesSolved + minsLeft - 0.25f * hintsUsed;
        score              = Mathf.Max(0f, score);

        if (_scoreLine != null)
            _scoreLine.text = $"SCORE   {score:0.00}";

        if (_breakdownLine != null)
            _breakdownLine.text =
                $"Clues: {cluesSolved}/3 (+{cluesSolved})\n" +
                $"Time Bonus: +{minsLeft:0.0} min\n" +
                $"Hint Penalty: -{0.25f * hintsUsed:0.00}";

        if (_titleLine != null)
            _titleLine.text = "YOU ESCAPED!";

        if (_subtitleLine != null)
            _subtitleLine.text = "Congratulations! You solved every clue and made it out.";

        _panel.SetActive(true);

        if (useFixedWorldTransform)
        {
            _canvas.transform.position = fixedWorldPosition;
            _canvas.transform.rotation = Quaternion.Euler(fixedWorldRotation);
        }
        else if (anchorNearDoor && TryGetDoorAnchor(out var doorAnchor))
        {
            Vector3 worldOffset = doorAnchor.right * doorMenuOffset.x
                                  + Vector3.up * doorMenuOffset.y
                                  + doorAnchor.forward * doorMenuOffset.z;
            _canvas.transform.position = doorAnchor.position + worldOffset;

            if (Camera.main != null)
            {
                Vector3 look = Camera.main.transform.position - _canvas.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude < 0.0001f)
                    look = doorAnchor.forward;
                _canvas.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
            else
            {
                _canvas.transform.rotation = Quaternion.LookRotation(-doorAnchor.forward, Vector3.up);
            }
        }
        else if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _canvas.transform.position =
                cam.position + cam.forward * 0.85f + cam.up * 0.01f;
            _canvas.transform.rotation =
                Quaternion.Euler(0f, cam.rotation.eulerAngles.y, 0f);
        }
    }

    // ── UI Construction ───────────────────────────────────────────────────
    private void BuildUI()
    {
        // Canvas
        var canvasObj = new GameObject("CompletionCanvas");
        canvasObj.transform.SetParent(transform);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        var xrRay = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRay != null) canvasObj.AddComponent(xrRay);

        var canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta  = new Vector2(0.62f, 0.56f);
        canvasRT.localScale = Vector3.one;

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

        // Panel card
        _panel = MakePanel("CompletionPanel", canvasObj.transform,
            new Vector2(0.59f, 0.53f), Vector2.zero, BgDark);
        MakeBorderGlow(_panel.transform, new Vector2(0.59f, 0.53f), AccentPurple);
        _panel.AddComponent<RectMask2D>();
        _panel.SetActive(false);

        // Header bar
        var header = MakePanel("Header", _panel.transform,
            new Vector2(0.59f, 0.095f), new Vector2(0f, 0.198f), HeaderColor);
        MakeLabel("HeaderLabel", header.transform,
            "ESCAPE ROOM", 0.040f, FontStyles.Bold,
            MenuThemes.Completion.AccentPrimary, TextAlignmentOptions.Center,
            new Vector2(0.53f, 0.09f), Vector2.zero);

        // Body title
        var titleObj = new GameObject("CongratTitle");
        titleObj.transform.SetParent(_panel.transform, false);
        _titleLine = titleObj.AddComponent<TextMeshProUGUI>();
        _titleLine.fontSize = 0.053f;
        _titleLine.fontStyle = MenuThemes.Typography.Header;
        _titleLine.alignment = TextAlignmentOptions.Center;
        _titleLine.color = GoldText;
        _titleLine.enableAutoSizing = true;
        _titleLine.fontSizeMin = 0.036f;
        _titleLine.fontSizeMax = 0.053f;
        _titleLine.textWrappingMode = TextWrappingModes.Normal;
        var trt = titleObj.GetComponent<RectTransform>();
        trt.sizeDelta = new Vector2(0.50f, 0.060f);
        trt.anchoredPosition = new Vector2(0f, 0.115f);

        // Subtitle
        var subtitleObj = new GameObject("Subtitle");
        subtitleObj.transform.SetParent(_panel.transform, false);
        _subtitleLine = subtitleObj.AddComponent<TextMeshProUGUI>();
        _subtitleLine.fontSize = 0.028f;
        _subtitleLine.fontStyle = MenuThemes.Typography.Body;
        _subtitleLine.alignment = TextAlignmentOptions.Center;
        _subtitleLine.color = TextSub;
        _subtitleLine.enableAutoSizing = true;
        _subtitleLine.fontSizeMin = 0.018f;
        _subtitleLine.fontSizeMax = 0.028f;
        _subtitleLine.textWrappingMode = TextWrappingModes.Normal;
        var srt = subtitleObj.GetComponent<RectTransform>();
        srt.sizeDelta = new Vector2(0.50f, 0.070f);
        srt.anchoredPosition = new Vector2(0f, 0.055f);

        // Divider line
        var divider = new GameObject("Divider");
        divider.transform.SetParent(_panel.transform, false);
        divider.AddComponent<Image>().color = new Color(
            MenuThemes.Completion.AccentPrimary.r,
            MenuThemes.Completion.AccentPrimary.g,
            MenuThemes.Completion.AccentPrimary.b, 0.45f);
        var drt = divider.GetComponent<RectTransform>();
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta        = new Vector2(0.50f, 0.003f);
        drt.anchoredPosition = new Vector2(0f, -0.002f);

        // Score total
        var scoreObj = new GameObject("ScoreLine");
        scoreObj.transform.SetParent(_panel.transform, false);
        _scoreLine = scoreObj.AddComponent<TextMeshProUGUI>();
        _scoreLine.text = "SCORE   0.00";
        _scoreLine.fontSize = 0.046f;
        _scoreLine.fontStyle = MenuThemes.Typography.Emphasis;
        _scoreLine.color = GoldText;
        _scoreLine.alignment = TextAlignmentOptions.Center;
        _scoreLine.enableAutoSizing = true;
        _scoreLine.fontSizeMin = 0.030f;
        _scoreLine.fontSizeMax = 0.046f;
        var srt2 = scoreObj.GetComponent<RectTransform>();
        srt2.anchorMin = srt2.anchorMax = srt2.pivot = new Vector2(0.5f, 0.5f);
        srt2.sizeDelta = new Vector2(0.50f, 0.055f);
        srt2.anchoredPosition = new Vector2(0f, -0.050f);

        // Breakdown block
        var bdObj = new GameObject("BreakdownLine");
        bdObj.transform.SetParent(_panel.transform, false);
        _breakdownLine = bdObj.AddComponent<TextMeshProUGUI>();
        _breakdownLine.text = "";
        _breakdownLine.fontSize = 0.024f;
        _breakdownLine.fontStyle = MenuThemes.Typography.Body;
        _breakdownLine.color = new Color(
            MenuThemes.Completion.TextSecondary.r,
            MenuThemes.Completion.TextSecondary.g,
            MenuThemes.Completion.TextSecondary.b, 0.90f);
        _breakdownLine.alignment = TextAlignmentOptions.Center;
        _breakdownLine.textWrappingMode = TextWrappingModes.Normal;
        _breakdownLine.enableAutoSizing = true;
        _breakdownLine.fontSizeMin = 0.017f;
        _breakdownLine.fontSizeMax = 0.024f;
        var brt = bdObj.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(0.50f, 0.100f);
        brt.anchoredPosition = new Vector2(0f, -0.120f);

        // Play again button
        var playAgainBtn = MakeButton("PlayAgainButton", "PLAY AGAIN",
            _panel.transform, new Vector2(0f, -0.206f), 0.20f, 0.052f, AccentTeal);
        ClueUiLayout.WireGotItButtonForXrDirectSelect(playAgainBtn);

        // Legacy layout kept below is intentionally removed in favor of compact card.
        playAgainBtn.onClick.AddListener(() =>
        {
            RestartFromBeginning();
        });
    }

    private void RestartFromBeginning()
    {
        if (_restartInProgress) return;
        _restartInProgress = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private IEnumerator CaptureInitialRigPoseWhenReady()
    {
        // Give XR rig/camera one frame to initialize after load.
        yield return null;
        if (TryGetRigTransform(out var rig))
        {
            _initialRigPos = rig.position;
            _initialRigRot = rig.rotation;
            _capturedInitialPose = true;
        }
    }

    private IEnumerator RestoreInitialRigPoseWhenReady()
    {
        // Wait until XR rig is present in the reloaded scene.
        yield return null;
        yield return null;

        if (_capturedInitialPose && TryGetRigTransform(out var rig))
        {
            rig.position = _initialRigPos;
            rig.rotation = _initialRigRot;
        }

        _restartInProgress = false;
    }

    private static bool TryGetRigTransform(out Transform rig)
    {
        // Prefer XR Origin if available.
        var xrOriginType = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
        if (xrOriginType != null)
        {
            var xrOriginObj = Object.FindFirstObjectByType(xrOriginType);
            if (xrOriginObj is Component comp && comp.transform != null)
            {
                rig = comp.transform;
                return true;
            }
        }

        // Fallback: use the main camera root.
        if (Camera.main != null)
        {
            rig = Camera.main.transform.root;
            return rig != null;
        }

        rig = null;
        return false;
    }

    private static bool TryGetDoorAnchor(out Transform anchor)
    {
        var hinge = Object.FindFirstObjectByType<DoorProximityHinge>();
        if (hinge != null)
        {
            var hingePivot = hinge.HingePivotTransform;
            anchor = hingePivot != null ? hingePivot : hinge.transform;
            return anchor != null;
        }

        anchor = null;
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static GameObject MakePanel(string name, Transform parent,
        Vector2 size, Vector2 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<Image>().color = color;
        var rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
        return obj;
    }

    private static void MakeBorderGlow(Transform parent, Vector2 innerSize, Color color)
    {
        const float b = 0.012f;
        var obj = new GameObject("BorderGlow");
        obj.transform.SetParent(parent, false);
        obj.transform.SetAsFirstSibling();
        obj.AddComponent<Image>().color = color;
        var rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta        = innerSize + new Vector2(b, b);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Creates a TextMeshPro label using simple centre-anchored sizing.
    /// sizeDelta = (width, height), pos = anchoredPosition from parent centre.
    /// </summary>
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
        tmp.textWrappingMode    = TextWrappingModes.Normal;
        var rt                 = obj.GetComponent<RectTransform>();
        rt.anchorMin           = new Vector2(0.5f, 0.5f);
        rt.anchorMax           = new Vector2(0.5f, 0.5f);
        rt.pivot               = new Vector2(0.5f, 0.5f);
        rt.sizeDelta           = sizeDelta;
        rt.anchoredPosition    = pos;
    }

    private static Button MakeButton(string name, string label,
        Transform parent, Vector2 pos, float w, float h, Color color)
    {
        var btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        var img   = btnObj.AddComponent<Image>();
        img.color = color;
        var btn   = btnObj.AddComponent<Button>();

        var cb              = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = Color.Lerp(color, Color.white, 0.30f);
        cb.pressedColor     = Color.Lerp(color, Color.black, 0.30f);
        cb.selectedColor    = cb.highlightedColor;
        btn.colors          = cb;

        var rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = pos;

        // Label
        var textObj = new GameObject("Label");
        textObj.transform.SetParent(btnObj.transform, false);
        var tmp       = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = MenuThemes.Typography.Emphasis;
        tmp.fontSize  = h * 0.42f;
        tmp.color     = Color.white;
        var trt       = textObj.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;

        return btn;
    }
}
