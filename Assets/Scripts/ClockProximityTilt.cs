using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Tilts the clock when the right controller is inside the zone collider and the select
/// (trigger button) is pressed. Toggle each time. No XR Grab / InputAction assets required.
/// </summary>
[DisallowMultipleComponent]
public class ClockProximityTilt : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField]
    [Tooltip("Prefix for adb: adb logcat -s Unity | rg \"YourTag\"")]
    string m_DebugLogTag = "ClockProximityTilt";

    [SerializeField]
    [Tooltip("If true, logs grip/zone/device state every interval while in Play Mode.")]
    bool m_DebugHeartbeat = true;

    [SerializeField]
    [Tooltip("Seconds between heartbeat logs (0 = disable heartbeat only).")]
    float m_DebugHeartbeatInterval = 1f;

    [Tooltip("Volume used for proximity. Defaults to the Collider on this object.")]
    [SerializeField]
    Collider m_Zone;

    [Tooltip("Optional. If unset, uses GameObject named RightHandController.")]
    [SerializeField]
    Transform m_RightController;
    [Tooltip("Optional. If unset, uses GameObject named LeftHandController.")]
    [SerializeField]
    Transform m_LeftController;

    [Header("Tilt")]
    [SerializeField]
    Transform m_HingePivot;

    [SerializeField]
    Vector3 m_LocalSwingAxis = new Vector3(0f, 0f, 1f);

    [SerializeField]
    float m_TiltDegrees = 35f;

    [SerializeField]
    float m_AnimationDuration = 0.25f;

    [SerializeField]
    AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Key reveal (after clue 3)")]
    [SerializeField] bool m_EnableKeyReveal = true;
    [Tooltip("Optional explicit key object. If empty, auto-uses PuzzleManager.keyObject.")]
    [SerializeField] GameObject m_KeyObject;
    [Tooltip("Position of key relative to clock center when clock is in CLOSED pose.")]
    [SerializeField] Vector3 m_KeyRevealClosedLocalOffset = new Vector3(0f, 0.00f, -0.06f);
    [SerializeField] Vector3 m_KeyRevealLocalEuler = new Vector3(0f, 0f, 0f);
    [SerializeField] Color m_KeySpotColor = new Color(1f, 0.93f, 0.82f, 1f);
    [SerializeField] float m_KeySpotIntensity = 2.4f;
    [SerializeField] float m_KeySpotRange = 1.4f;
    [SerializeField] float m_KeySpotAngle = 38f;
    [Tooltip("Spotlight position relative to clock center in CLOSED pose (up + right bias).")]
    [SerializeField] Vector3 m_KeySpotClosedLocalOffset = new Vector3(0.14f, 0.20f, 0.16f);
    [SerializeField] bool m_ForceEnableKeyRenderersOnReveal = true;

    Quaternion m_ClosedLocalRotation;
    bool m_IsTilted;
    bool m_IsAnimating;
    bool m_PrevSelectR;
    bool m_PrevSelectL;
    Coroutine m_AnimateRoutine;
    float m_NextHeartbeatTime;
    bool m_LoggedMissingController;
    Light m_KeySpot;
    bool m_KeyRevealedFromClock;
    Vector3 m_KeyRevealWorldAnchor;
    Vector3 m_KeySpotWorldAnchor;

    void Awake()
    {
        if (m_Zone == null)
        {
            m_Zone = GetComponent<Collider>();
            if (m_Zone == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.42f, 0.42f, 0.08f);
                box.center = new Vector3(0f, 0f, 0.05f);
                m_Zone = box;
                Log($"Awake: added BoxCollider size={box.size} center={box.center} (local)");
            }
            else
                Log($"Awake: using existing collider {m_Zone.GetType().Name} on '{name}'");
        }
        else
            Log($"Awake: Zone assigned: {m_Zone.GetType().Name}");

        if (m_HingePivot == null)
            m_HingePivot = transform;

        m_ClosedLocalRotation = m_HingePivot.localRotation;
        m_KeyRevealWorldAnchor = transform.TransformPoint(m_KeyRevealClosedLocalOffset);
        m_KeySpotWorldAnchor = transform.TransformPoint(m_KeySpotClosedLocalOffset);

        if (m_Ease == null || m_Ease.length == 0)
            m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null)
            {
                m_RightController = go.transform;
                Log($"Awake: found RightHandController '{go.name}' (verify in Hierarchy / assign if wrong).");
            }
            else
                LogWarning("Awake: GameObject.Find(\"RightHandController\") returned null. Assign Right Controller in the Inspector.");
        }
        else
            Log($"Awake: Right Controller assigned: '{m_RightController.name}'");

        if (m_KeyObject == null)
        {
            var pm = FindFirstObjectByType<PuzzleManager>();
            if (pm != null)
                m_KeyObject = pm.keyObject;
        }

        if (m_LeftController == null)
        {
            var go = GameObject.Find("LeftHandController");
            if (go != null)
                m_LeftController = go.transform;
        }

        Log($"Awake: zone bounds (world) {m_Zone.bounds}, hinge='{m_HingePivot.name}' tiltDeg={m_TiltDegrees}");
    }

    void Update()
    {
        if (m_IsAnimating || m_Zone == null)
            return;

        if (m_RightController == null && m_LeftController == null)
        {
            if (!m_LoggedMissingController)
            {
                m_LoggedMissingController = true;
                LogWarning("Update: controller references are null — assign hand controllers in Inspector.");
            }
            return;
        }

        bool edgeR = ReadSelectEdge(XRNode.RightHand, ref m_PrevSelectR);
        bool edgeL = ReadSelectEdge(XRNode.LeftHand, ref m_PrevSelectL);
        bool inR = m_RightController != null && IsInsideZone(m_Zone, m_RightController.position);
        bool inL = m_LeftController != null && IsInsideZone(m_Zone, m_LeftController.position);

        if (!(inR || inL))
        {
            HeartbeatMaybe(() => "No hand in zone.");
        }

        bool pressedEdge = (edgeR && inR) || (edgeL && inL);
        if (!pressedEdge)
            return;

        bool puzzleReadyForClockReveal = PuzzleManager.SolvedClueCount >= 3;
        if (!puzzleReadyForClockReveal)
            InteractableHapticFeedback.ShowWrongOrderCue(transform);

        Log($"Select edge ACCEPTED: toggling tilt -> {!m_IsTilted}");
        if (m_AnimateRoutine != null)
            StopCoroutine(m_AnimateRoutine);
        m_AnimateRoutine = StartCoroutine(AnimateToTilted(!m_IsTilted));
    }

    bool ReadSelectEdge(XRNode hand, ref bool prevPressed)
    {
        var device = InputDevices.GetDeviceAtXRNode(hand);
        if (!device.isValid)
        {
            prevPressed = false;
            return false;
        }

        if (!device.TryGetFeatureValue(CommonUsages.triggerButton, out bool selectPressed))
        {
            prevPressed = false;
            return false;
        }

        bool edge = selectPressed && !prevPressed;
        prevPressed = selectPressed;
        return edge;
    }

    public void ConfigureAsNonKeyClock()
    {
        m_EnableKeyReveal = false;
    }

    void HeartbeatMaybe(System.Func<string> message)
    {
        if (!m_DebugHeartbeat || m_DebugHeartbeatInterval <= 0f)
            return;
        if (Time.unscaledTime < m_NextHeartbeatTime)
            return;
        m_NextHeartbeatTime = Time.unscaledTime + m_DebugHeartbeatInterval;
        Log(message());
    }

    static bool IsInsideZone(Collider zone, Vector3 worldPoint)
    {
        Vector3 closest = zone.ClosestPoint(worldPoint);
        return (closest - worldPoint).sqrMagnitude < 1e-6f;
    }

    IEnumerator AnimateToTilted(bool tilted)
    {
        Log($"AnimateToTilted start -> open={tilted}");
        m_IsAnimating = true;
        float fromT = m_IsTilted ? 1f : 0f;
        float toT = tilted ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < m_AnimationDuration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / m_AnimationDuration);
            float smooth = m_Ease.Evaluate(u);
            float t = Mathf.Lerp(fromT, toT, smooth);
            ApplyTiltT(t);
            yield return null;
        }

        ApplyTiltT(toT);
        m_IsTilted = tilted;
        m_IsAnimating = false;
        m_AnimateRoutine = null;
        Log($"AnimateToTilted done. m_IsTilted={m_IsTilted}");

        if (m_IsTilted)
            TryRevealKeyBehindClock();
        else
            SetKeySpotlightEnabled(false);
    }

    void ApplyTiltT(float t)
    {
        float angle = Mathf.Lerp(0f, m_TiltDegrees, t);
        m_HingePivot.localRotation = m_ClosedLocalRotation * Quaternion.AngleAxis(angle, m_LocalSwingAxis);
    }

    void Log(string msg)
    {
        Debug.Log($"{m_DebugLogTag}: {msg}");
    }

    void LogWarning(string msg)
    {
        Debug.LogWarning($"{m_DebugLogTag}: {msg}");
    }

    void TryRevealKeyBehindClock()
    {
        if (!m_EnableKeyReveal)
            return;
        if (PuzzleManager.SolvedClueCount < 3)
            return;
        if (m_KeyObject == null)
        {
            var pm = FindFirstObjectByType<PuzzleManager>();
            if (pm != null)
                m_KeyObject = pm.keyObject;
        }
        if (m_KeyObject == null)
        {
            var grabs = FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var grab in grabs)
            {
                if (grab != null && grab.name.ToLowerInvariant().Contains("key"))
                {
                    m_KeyObject = grab.gameObject;
                    break;
                }
            }
        }
        if (m_KeyObject == null)
        {
            LogWarning("TryRevealKeyBehindClock: key object not found.");
            return;
        }

        var keyTx = m_KeyObject.transform;
        keyTx.SetParent(null, true);
        keyTx.position = m_KeyRevealWorldAnchor;
        keyTx.rotation = Quaternion.Euler(m_KeyRevealLocalEuler);
        m_KeyObject.SetActive(true);
        if (m_ForceEnableKeyRenderersOnReveal)
        {
            var renderers = m_KeyObject.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
                renderer.enabled = true;
        }

        var puzzleManager = FindFirstObjectByType<PuzzleManager>();
        if (puzzleManager != null)
            puzzleManager.NotifyKeyRevealedFromClock(m_KeyObject);

        m_KeyRevealedFromClock = true;
        SetKeySpotlightEnabled(true);
        Log($"Key revealed at worldPos={m_KeyRevealWorldAnchor} and spotlight enabled.");
    }

    void SetKeySpotlightEnabled(bool on)
    {
        if (!m_KeyRevealedFromClock && on) return;

        if (m_KeySpot == null)
        {
            var go = new GameObject("ClockKeySpot");
            go.transform.SetParent(null, true);
            go.transform.position = m_KeySpotWorldAnchor;
            go.transform.localRotation = Quaternion.identity;
            m_KeySpot = go.AddComponent<Light>();
            m_KeySpot.type = LightType.Spot;
            m_KeySpot.color = m_KeySpotColor;
            m_KeySpot.intensity = m_KeySpotIntensity;
            m_KeySpot.range = m_KeySpotRange;
            m_KeySpot.spotAngle = m_KeySpotAngle;
            m_KeySpot.shadows = LightShadows.None;
        }

        m_KeySpot.transform.position = m_KeySpotWorldAnchor;

        if (m_KeyObject != null)
        {
            Vector3 target = m_KeyObject.transform.position;
            m_KeySpot.transform.rotation = Quaternion.LookRotation(target - m_KeySpot.transform.position, Vector3.up);
        }

        m_KeySpot.enabled = on;
    }
}
