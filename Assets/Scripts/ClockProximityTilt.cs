using System.Collections;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Tilts the clock when the right controller is inside the zone collider and the grip
/// (side button) is pressed. Toggle each time. No XR Grab / InputAction assets required.
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

    Quaternion m_ClosedLocalRotation;
    bool m_IsTilted;
    bool m_IsAnimating;
    bool m_PrevGrip;
    Coroutine m_AnimateRoutine;
    float m_NextHeartbeatTime;
    bool m_LoggedMissingController;

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

        Log($"Awake: zone bounds (world) {m_Zone.bounds}, hinge='{m_HingePivot.name}' tiltDeg={m_TiltDegrees}");
    }

    void Update()
    {
        if (m_IsAnimating || m_Zone == null)
            return;

        if (m_RightController == null)
        {
            if (!m_LoggedMissingController)
            {
                m_LoggedMissingController = true;
                LogWarning("Update: m_RightController is null — assign Right Controller or add an object named RightHandController.");
            }
            return;
        }

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid)
        {
            HeartbeatMaybe(() => "XR RightHand device invalid (not tracking / not ready).");
            return;
        }

        if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip))
        {
            HeartbeatMaybe(() => "TryGetFeatureValue(gripButton) failed.");
            return;
        }

        bool pressedEdge = grip && !m_PrevGrip;
        m_PrevGrip = grip;

        Vector3 handPos = m_RightController.position;
        bool inside = IsInsideZone(m_Zone, handPos);

        HeartbeatMaybe(() =>
            $"hand='{m_RightController.name}' pos={handPos} inside={inside} grip={grip} tilted={m_IsTilted} device={device.name}");

        if (!pressedEdge)
            return;

        if (!inside)
        {
            Vector3 closest = m_Zone.ClosestPoint(handPos);
            float d2 = (closest - handPos).sqrMagnitude;
            Log($"Grip edge IGNORED: outside zone. handPos={handPos} closest={closest} distSqr={d2:E3} (need ~0 inside)");
            return;
        }

        Log($"Grip edge ACCEPTED: toggling tilt -> {!m_IsTilted}");
        if (m_AnimateRoutine != null)
            StopCoroutine(m_AnimateRoutine);
        m_AnimateRoutine = StartCoroutine(AnimateToTilted(!m_IsTilted));
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
}
