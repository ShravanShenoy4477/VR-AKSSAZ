using System;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Grab-only confirm: analog <b>grip</b> must stay above a high threshold for a sustained time
/// while the controller stays inside the box. No grip-button bool edge (too easy to trip).
/// After a confirm, both hands must leave the zone before another confirm (re-arm).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class ClueGotItGripConfirm : MonoBehaviour
{
    Func<bool> _canInteract;
    Action _onConfirm;

    [SerializeField] Transform rightController;
    [SerializeField] Transform leftController;

    BoxCollider _zone;
    float _suppressGripsUntil;

    [Tooltip("Ignore grip confirms briefly after this row is enabled (panel just opened).")]
    [SerializeField] float m_OpenGraceSeconds = 0.45f;

    [Tooltip("Analog grip must stay at or above this value (0–1) for the sustain duration.")]
    [SerializeField] float m_GripSustainMin = 0.92f;

    [Tooltip("Seconds the grip must stay strong while inside the zone.")]
    [SerializeField] float m_GripSustainSeconds = 0.24f;

    [Tooltip("If analog grip is unavailable, refuse confirm (avoids bool gripButton flips).")]
    [SerializeField] bool m_AnalogGripOnly = true;

    [Tooltip("Log sustain progress / confirm / re-arm (paste if still mis-fires).")]
    [SerializeField] bool m_Debug;

    float _sustainR;
    float _sustainL;
    bool _armed = true;
    float _nextDebugLog;

    public void Initialize(Func<bool> canInteract, Action onConfirm)
    {
        _canInteract = canInteract;
        _onConfirm = onConfirm;
    }

    /// <summary>Optional runtime tuning (e.g. book clue after release).</summary>
    public void SetGripThresholds(float sustainMin, float sustainSeconds, float openGraceSeconds)
    {
        m_GripSustainMin = sustainMin;
        m_GripSustainSeconds = sustainSeconds;
        m_OpenGraceSeconds = openGraceSeconds;
        _suppressGripsUntil = Time.time + m_OpenGraceSeconds;
        _sustainR = _sustainL = 0f;
        _armed = true;
    }

    void Awake()
    {
        _zone = GetComponent<BoxCollider>();
        _zone.isTrigger = true;
    }

    void OnEnable()
    {
        _suppressGripsUntil = Time.time + m_OpenGraceSeconds;
        _sustainR = _sustainL = 0f;
        _armed = true;
    }

    void Start()
    {
        if (rightController == null)
        {
            var r = GameObject.Find("RightHandController");
            if (r != null) rightController = r.transform;
        }

        if (leftController == null)
        {
            var l = GameObject.Find("LeftHandController");
            if (l != null) leftController = l.transform;
        }
    }

    void Update()
    {
        bool inR = rightController != null && IsInsideZone(_zone, rightController.position);
        bool inL = leftController != null && IsInsideZone(_zone, leftController.position);

        if (!_armed)
        {
            if (!inR && !inL)
            {
                _armed = true;
                _sustainR = _sustainL = 0f;
                if (m_Debug)
                    Debug.Log("[ClueGotItGripConfirm] Re-armed — both hands left button zone.");
            }

            return;
        }

        if (_canInteract == null || !_canInteract()) return;
        if (Time.time < _suppressGripsUntil) return;

        bool fired = false;
        if (inR)
            fired |= AccumulateSustain(XRNode.RightHand, ref _sustainR, "R");
        if (inL)
            fired |= AccumulateSustain(XRNode.LeftHand, ref _sustainL, "L");

        if (!inR) _sustainR = 0f;
        if (!inL) _sustainL = 0f;

        if (fired)
        {
            _onConfirm?.Invoke();
            _armed = false;
            _sustainR = _sustainL = 0f;
            if (m_Debug)
                Debug.Log("[ClueGotItGripConfirm] Confirmed — disarm until hands leave zone.");
        }
    }

    bool AccumulateSustain(XRNode node, ref float sustain, string tag)
    {
        float g = ReadAnalogGrip(node);
        if (g < 0f)
            return false;

        if (g < m_GripSustainMin)
        {
            sustain = 0f;
            return false;
        }

        sustain += Time.deltaTime;
        if (m_Debug && Time.time >= _nextDebugLog)
        {
            _nextDebugLog = Time.time + 0.35f;
            Debug.Log($"[ClueGotItGripConfirm] {tag} sustain={sustain:0.00}s / {m_GripSustainSeconds:0.00}s grip={g:0.00}");
        }

        if (sustain < m_GripSustainSeconds)
            return false;

        sustain = 0f;
        return true;
    }

    /// <summary>Returns grip 0–1, or -1 if unavailable / disallowed.</summary>
    float ReadAnalogGrip(XRNode node)
    {
        var dev = InputDevices.GetDeviceAtXRNode(node);
        if (!dev.isValid) return -1f;

        if (dev.TryGetFeatureValue(CommonUsages.grip, out float axis))
            return Mathf.Clamp01(axis);

        if (m_AnalogGripOnly)
            return -1f;

        if (dev.TryGetFeatureValue(CommonUsages.gripButton, out bool pressed))
            return pressed ? 1f : 0f;

        return -1f;
    }

    static bool IsInsideZone(Collider zone, Vector3 worldPoint)
    {
        return (zone.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude < 1e-6f;
    }
}
