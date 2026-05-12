using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Slides the sofa when a controller is inside the grip zone and that hand's grip is pressed.
/// Toggles each press: first grip slides it open, next grip slides it back.
///
/// <b>How to play:</b> Hand in the cushion-side <c>SlideGripZone</c>, then <b>grip</b> once to slide.
/// Enable <see cref="m_AutoFitGripZoneToRenderers"/> to cover the full sofa mesh instead.
/// </summary>
[DisallowMultipleComponent]
public class SofaProximitySlide : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Proximity zone collider. Defaults to the Collider on this GameObject.")]
    [SerializeField] Collider m_Zone;

    [Tooltip("Right hand controller. Leave blank to auto-find 'RightHandController'.")]
    [SerializeField] Transform m_RightController;

    [Tooltip("Left hand controller. Leave blank to auto-find 'LeftHandController'.")]
    [SerializeField] Transform m_LeftController;

    [Header("Slide")]
    [Tooltip("Local-space axis to slide along. (1,0,0)=right  (0,0,1)=forward  etc.")]
    [SerializeField] Vector3 m_SlideAxis = new Vector3(1f, 0f, 0f);

    [Tooltip("How far the sofa slides (metres).")]
    [SerializeField] float m_SlideDistance = 1.2f;

    [Tooltip("Duration of the slide animation (seconds).")]
    [SerializeField] float m_AnimationDuration = 0.5f;

    [SerializeField] AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Grip zone")]
    [Tooltip("If true, resize SlideGripZone to wrap sofa renderers. Default off — uses small cushion-side box.")]
    [SerializeField] bool m_AutoFitGripZoneToRenderers = true;

    [Tooltip("Extra metres around the mesh bounds (world axes before mapping to sofa local).")]
    [SerializeField] float m_GripZonePadding = 0.22f;

    [Tooltip("SlideGripZone box size (local metres) when auto-fit is off.")]
    [SerializeField] Vector3 m_LegacyGripZoneLocalSize = new Vector3(0.86f, 0.40f, 0.52f);

    [Tooltip("SlideGripZone local position when auto-fit is off (cushion / knee area).")]
    [SerializeField] Vector3 m_LegacyGripZoneLocalPos = new Vector3(0f, 0.39f, 0.24f);

    [Tooltip("Used when auto-fit runs but no Renderer is found.")]
    [SerializeField] Vector3 m_FallbackGripZoneLocalSize = new Vector3(2.5f, 1.15f, 1.05f);

    [SerializeField] Vector3 m_FallbackGripZoneLocalCenter = new Vector3(0f, 0.38f, 0f);

    [Header("Grip input")]
    [Tooltip("Fallback to analog trigger axis when triggerButton is unavailable on a runtime/device.")]
    [SerializeField] bool m_AllowAnalogSelectAxisFallback = true;
    [SerializeField, Range(0.2f, 0.98f)] float m_SelectAxisPressedThreshold = 0.62f;
    [Tooltip("Allow a small outside distance from the zone to still count as in-zone (meters).")]
    [SerializeField, Range(0f, 0.15f)] float m_ZoneDistanceTolerance = 0.03f;
    [Tooltip("Secondary safety gate: hand must also be within this distance of sofa root (meters).")]
    [SerializeField, Range(0.3f, 2.5f)] float m_MaxHandDistanceFromSofa = 1.8f;
    [Tooltip("Ignore sofa slide press when that hand is near another grabbable object (e.g. clue book).")]
    [SerializeField] bool m_BlockSlideWhenHandNearGrabbable = true;
    [SerializeField, Range(0.05f, 0.35f)] float m_GrabbableBlockRadius = 0.16f;
    [Header("Neighbor sofa spacing")]
    [Tooltip("Extra minimum spacing (meters) kept between sofas after slide.")]
    [SerializeField, Range(0f, 0.35f)] float m_MinNeighborGap = 0.06f;
    [Tooltip("If true, opening distance is clamped to avoid overlap with nearby sofas.")]
    [SerializeField] bool m_AvoidNeighborOverlap = true;

    [Header("Debug")]
    [Tooltip("Logs grip edges, in-zone checks, and slide triggers.")]
    [SerializeField] bool m_DebugLogs;
    [SerializeField] float m_DebugLogInterval = 0.35f;
    [Header("Out-of-turn feedback")]
    [SerializeField] float m_LockedPulseAmplitude = 0.16f;
    [SerializeField] float m_LockedPulseDuration = 0.03f;

    // ── Runtime ───────────────────────────────────────────────────────────────
    Vector3   m_ClosedPosition;
    Vector3   m_OpenPosition;
    bool      m_IsOpen;
    bool      m_IsAnimating;
    bool      m_PrevGripR;
    bool      m_PrevGripL;
    Coroutine m_Routine;
    float     m_NextDebugLogAt;
    readonly Collider[] m_NearbyColliders = new Collider[24];

    // ── Awake ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        // Use a small trigger child for grip detection so the sofa's main mesh collider
        // can stay solid for the floor without treating the whole volume as "inside" for hands.
        if (m_Zone == null || m_Zone.gameObject == gameObject || !m_Zone.transform.IsChildOf(transform))
            EnsureSlideGripZone();

        if (m_AutoFitGripZoneToRenderers && ShouldAutoResizeGripZone())
            ApplyGripZoneFromRenderers();
        else
            ApplyLegacyCushionGripZone();

        if (m_DebugLogs && m_Zone is BoxCollider dbgBox)
        {
            Debug.Log(
                $"[SofaProximitySlide] Grip zone='{dbgBox.name}' localPos={dbgBox.transform.localPosition} " +
                $"size={dbgBox.size} autoFit={m_AutoFitGripZoneToRenderers}");
        }

        // Right controller
        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null)
                m_RightController = go.transform;
            else
                Debug.LogWarning("SofaProximitySlide: assign Right Controller in the Inspector.");
        }

        if (m_LeftController == null)
        {
            var go = GameObject.Find("LeftHandController");
            if (go != null)
                m_LeftController = go.transform;
        }

        if (m_Ease == null || m_Ease.length == 0)
            m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // Record rest position; compute open position in world space
        m_ClosedPosition = transform.position;
        Vector3 worldAxis = transform.TransformDirection(m_SlideAxis.normalized);
        m_OpenPosition   = m_ClosedPosition + worldAxis * m_SlideDistance;
    }

    void EnsureSlideGripZone()
    {
        var t = transform.Find("SlideGripZone");
        if (t == null)
        {
            var child = new GameObject("SlideGripZone");
            child.transform.SetParent(transform, false);
            child.layer = gameObject.layer;
            child.transform.localPosition = m_LegacyGripZoneLocalPos;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            var box = child.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = m_LegacyGripZoneLocalSize;
            m_Zone = box;
        }
        else if (m_Zone == null)
        {
            m_Zone = t.GetComponent<Collider>();
        }

        // Always prefer the dedicated child trigger zone, even if inspector points elsewhere.
        if (t != null)
        {
            var c = t.GetComponent<Collider>();
            if (c != null) m_Zone = c;
        }
    }

    bool ShouldAutoResizeGripZone()
    {
        if (!m_AutoFitGripZoneToRenderers || m_Zone == null) return false;
        if (m_Zone.gameObject == gameObject) return false;
        if (!m_Zone.transform.IsChildOf(transform)) return false;
        return m_Zone is BoxCollider;
    }

    void ApplyGripZoneFromRenderers()
    {
        var box = (BoxCollider)m_Zone;
        var zoneTx = box.transform;

        if (!TryComputeSofaLocalBoundsFromRenderers(zoneTx, out var localCenter, out var localSize))
        {
            zoneTx.localPosition = m_FallbackGripZoneLocalCenter;
            zoneTx.localRotation = Quaternion.identity;
            box.center = Vector3.zero;
            box.size = m_FallbackGripZoneLocalSize;
            return;
        }

        zoneTx.localRotation = Quaternion.identity;
        zoneTx.localPosition = localCenter;
        box.center = Vector3.zero;
        box.size = Vector3.Max(localSize, new Vector3(0.6f, 0.25f, 0.4f));
    }

    bool TryComputeSofaLocalBoundsFromRenderers(Transform excludeSubtree, out Vector3 localCenter, out Vector3 localSize)
    {
        localCenter = Vector3.zero;
        localSize = m_FallbackGripZoneLocalSize;

        bool any = false;
        Vector3 wmin = default, wmax = default;

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (excludeSubtree != null && (r.transform == excludeSubtree || r.transform.IsChildOf(excludeSubtree)))
                continue;

            var b = r.bounds;
            if (!any)
            {
                wmin = b.min;
                wmax = b.max;
                any = true;
            }
            else
            {
                wmin = Vector3.Min(wmin, b.min);
                wmax = Vector3.Max(wmax, b.max);
            }
        }

        if (!any) return false;

        var pad = Vector3.one * m_GripZonePadding;
        wmin -= pad;
        wmax += pad;

        var corners = new Vector3[8];
        int i = 0;
        for (int xi = 0; xi < 2; xi++)
        for (int yi = 0; yi < 2; yi++)
        for (int zi = 0; zi < 2; zi++)
        {
            corners[i++] = new Vector3(
                xi == 0 ? wmin.x : wmax.x,
                yi == 0 ? wmin.y : wmax.y,
                zi == 0 ? wmin.z : wmax.z);
        }

        Vector3 lmin = transform.InverseTransformPoint(corners[0]);
        Vector3 lmax = lmin;
        for (i = 1; i < 8; i++)
        {
            var p = transform.InverseTransformPoint(corners[i]);
            lmin = Vector3.Min(lmin, p);
            lmax = Vector3.Max(lmax, p);
        }

        localCenter = (lmin + lmax) * 0.5f;
        localSize = lmax - lmin;
        return true;
    }

    void ApplyLegacyCushionGripZone()
    {
        if (m_Zone == null) return;
        if (!(m_Zone is BoxCollider box)) return;
        var zoneTx = box.transform;
        if (zoneTx.parent != transform || zoneTx.name != "SlideGripZone") return;
        zoneTx.localPosition = m_LegacyGripZoneLocalPos;
        zoneTx.localRotation = Quaternion.identity;
        box.center = Vector3.zero;
        box.size = m_LegacyGripZoneLocalSize;
    }

    // ── Update ────────────────────────────────────────────────────────────────
    void Update()
    {
        if (m_IsAnimating || m_Zone == null) return;

        bool edgeR = ReadSelectEdge(XRNode.RightHand, ref m_PrevGripR);
        bool edgeL = ReadSelectEdge(XRNode.LeftHand, ref m_PrevGripL);

        bool inR = m_RightController != null && IsInsideZoneWithTolerance(m_Zone, m_RightController.position, m_ZoneDistanceTolerance);
        bool inL = m_LeftController != null && IsInsideZoneWithTolerance(m_Zone, m_LeftController.position, m_ZoneDistanceTolerance);
        bool nearSofaR = m_RightController != null && Vector3.Distance(m_RightController.position, transform.position) <= m_MaxHandDistanceFromSofa;
        bool nearSofaL = m_LeftController != null && Vector3.Distance(m_LeftController.position, transform.position) <= m_MaxHandDistanceFromSofa;
        bool blockR = m_BlockSlideWhenHandNearGrabbable && IsHandNearOtherGrabbable(m_RightController);
        bool blockL = m_BlockSlideWhenHandNearGrabbable && IsHandNearOtherGrabbable(m_LeftController);

        if (!PuzzleManager.CanUseSofa())
        {
            bool lockedTryR = edgeR && inR && nearSofaR;
            bool lockedTryL = edgeL && inL && nearSofaL;
            if (lockedTryR)
            {
                XrHaptics.PulseRight(m_LockedPulseAmplitude, m_LockedPulseDuration);
                InteractableHapticFeedback.ShowWrongOrderCue(transform);
            }
            if (lockedTryL)
            {
                XrHaptics.PulseLeft(m_LockedPulseAmplitude, m_LockedPulseDuration);
                InteractableHapticFeedback.ShowWrongOrderCue(transform);
            }
            return;
        }

        if (m_DebugLogs && Time.time >= m_NextDebugLogAt && (inR || inL || edgeR || edgeL))
        {
            m_NextDebugLogAt = Time.time + Mathf.Max(0.05f, m_DebugLogInterval);
            Debug.Log(
                $"[SofaProximitySlide] inR={inR} edgeR={edgeR} inL={inL} edgeL={edgeL} " +
                $"nearSofaR={nearSofaR} nearSofaL={nearSofaL} " +
                $"blockR={blockR} blockL={blockL} isOpen={m_IsOpen} anim={m_IsAnimating}");
        }

        if (m_DebugLogs && ((edgeR && !inR) || (edgeL && !inL)))
        {
            Debug.Log(
                $"[SofaProximitySlide] Grip edge outside zone. rightDist={DistanceToZone(m_RightController):0.000} " +
                $"leftDist={DistanceToZone(m_LeftController):0.000} tol={m_ZoneDistanceTolerance:0.000}");
        }

        bool allowR = edgeR && inR && nearSofaR && !blockR;
        bool allowL = edgeL && inL && nearSofaL && !blockL;
        if (!allowR && !allowL) return;

        // Toggle slide
        if (!m_IsOpen && m_AvoidNeighborOverlap)
            m_OpenPosition = ComputeSafeOpenPosition();

        if (m_DebugLogs)
            Debug.Log($"[SofaProximitySlide] Triggered slide toggle. NextOpen={!m_IsOpen}");
        if (m_Routine != null) StopCoroutine(m_Routine);
        m_Routine = StartCoroutine(AnimateSlide(!m_IsOpen));
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    IEnumerator AnimateSlide(bool open)
    {
        m_IsAnimating = true;

        Vector3 from = open ? m_ClosedPosition : m_OpenPosition;
        Vector3 to   = open ? m_OpenPosition   : m_ClosedPosition;
        float elapsed = 0f;

        while (elapsed < m_AnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = m_Ease.Evaluate(Mathf.Clamp01(elapsed / m_AnimationDuration));
            transform.position = Vector3.Lerp(from, to, t);
            yield return null;
        }

        transform.position = to;
        m_IsOpen      = open;
        m_IsAnimating = false;
        m_Routine     = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    bool ReadSelectEdge(XRNode node, ref bool prevPressed)
    {
        var dev = InputDevices.GetDeviceAtXRNode(node);
        if (!dev.isValid)
        {
            prevPressed = false;
            return false;
        }

        bool pressed;
        if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton))
        {
            pressed = triggerButton;
        }
        else if (m_AllowAnalogSelectAxisFallback &&
                 dev.TryGetFeatureValue(CommonUsages.trigger, out float triggerAxis))
        {
            pressed = triggerAxis >= m_SelectAxisPressedThreshold;
        }
        else
        {
            prevPressed = false;
            return false;
        }

        bool edge = pressed && !prevPressed;
        prevPressed = pressed;
        return edge;
    }

    float DistanceToZone(Transform hand)
    {
        if (m_Zone == null || hand == null) return -1f;
        return Vector3.Distance(m_Zone.ClosestPoint(hand.position), hand.position);
    }

    static bool IsInsideZoneWithTolerance(Collider zone, Vector3 worldPoint, float tolerance)
    {
        if (zone == null || !zone.enabled || !zone.gameObject.activeInHierarchy)
            return false;
        return (zone.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude <= tolerance * tolerance;
    }

    bool IsHandNearOtherGrabbable(Transform hand)
    {
        if (hand == null) return false;
        int count = Physics.OverlapSphereNonAlloc(hand.position, m_GrabbableBlockRadius, m_NearbyColliders, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var c = m_NearbyColliders[i];
            if (c == null) continue;
            if (m_Zone != null && c == m_Zone) continue;
            if (c.transform.IsChildOf(transform)) continue;
            var grabbable = c.GetComponentInParent<XRGrabInteractable>();
            if (grabbable != null && grabbable.isActiveAndEnabled)
                return true;
        }

        return false;
    }

    Vector3 ComputeSafeOpenPosition()
    {
        Vector3 desired = m_ClosedPosition + transform.TransformDirection(m_SlideAxis.normalized) * m_SlideDistance;
        var myBoundsNow = GetSofaBounds();
        if (myBoundsNow.size.sqrMagnitude <= 1e-6f)
            return desired;

        Vector3 axis = transform.TransformDirection(m_SlideAxis.normalized);
        float desiredMove = Vector3.Dot(desired - m_ClosedPosition, axis);
        float safeMove = desiredMove;
        float currentMove = Vector3.Dot(transform.position - m_ClosedPosition, axis);
        float epsilon = 0.005f;

        // Iterate a few times to settle against multiple nearby sofas.
        for (int iter = 0; iter < 6; iter++)
        {
            bool adjusted = false;
            Bounds candidate = ShiftBounds(myBoundsNow, axis * (safeMove - currentMove));
            foreach (var other in FindObjectsByType<SofaProximitySlide>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (other == null || other == this || !other.isActiveAndEnabled) continue;
                if (!other.m_AvoidNeighborOverlap) continue;

                var otherBounds = other.GetSofaBounds();
                if (otherBounds.size.sqrMagnitude <= 1e-6f) continue;

                float overlap = ComputeAxisOverlap(candidate, otherBounds, axis, m_MinNeighborGap);
                if (overlap <= 0f) continue;

                // Retract this sofa along its own travel direction to maintain spacing.
                float retract = overlap + epsilon;
                if (desiredMove >= 0f) safeMove -= retract;
                else safeMove += retract;
                adjusted = true;
            }

            if (!adjusted) break;
        }

        // Clamp to never exceed configured travel and never cross the closed anchor.
        if (desiredMove >= 0f)
            safeMove = Mathf.Clamp(safeMove, 0f, desiredMove);
        else
            safeMove = Mathf.Clamp(safeMove, desiredMove, 0f);

        Vector3 safe = m_ClosedPosition + axis * safeMove;
        if (m_DebugLogs)
            Debug.Log($"[SofaProximitySlide] Safe open computed. desiredMove={desiredMove:0.###} safeMove={safeMove:0.###}");
        return safe;
    }

    Bounds GetSofaBounds()
    {
        bool any = false;
        Bounds b = default;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!any)
            {
                b = r.bounds;
                any = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }
        return any ? b : new Bounds(transform.position, Vector3.zero);
    }

    static Bounds ShiftBounds(Bounds src, Vector3 delta)
    {
        src.center += delta;
        return src;
    }

    static float ComputeAxisOverlap(Bounds a, Bounds b, Vector3 axis, float minGap)
    {
        axis.Normalize();
        float ra = ProjectAabbRadius(a.extents, axis);
        float rb = ProjectAabbRadius(b.extents, axis);
        float ca = Vector3.Dot(a.center, axis);
        float cb = Vector3.Dot(b.center, axis);
        float required = ra + rb + Mathf.Max(0f, minGap);
        float actual = Mathf.Abs(ca - cb);
        return required - actual;
    }

    static float ProjectAabbRadius(Vector3 ext, Vector3 axis)
    {
        return Mathf.Abs(axis.x) * ext.x + Mathf.Abs(axis.y) * ext.y + Mathf.Abs(axis.z) * ext.z;
    }

    public void ApplyReferenceTemplate()
    {
        // Matches the in-scene feel of Sofa_Wall9_FacingSofa.
        m_SlideDistance = 1f;
        m_AnimationDuration = 0.5f;
        m_ZoneDistanceTolerance = 0.05f;
        m_MaxHandDistanceFromSofa = 1.8f;
        m_AvoidNeighborOverlap = false;
        m_AutoFitGripZoneToRenderers = true;
        m_GripZonePadding = 0.22f;
        m_LegacyGripZoneLocalSize = new Vector3(0.68f, 0.34f, 0.40f);
        m_LegacyGripZoneLocalPos = new Vector3(0f, 0.37f, 0.20f);
        m_FallbackGripZoneLocalSize = new Vector3(2.5f, 1.15f, 1.05f);
        m_FallbackGripZoneLocalCenter = new Vector3(0f, 0.38f, 0f);
        m_BlockSlideWhenHandNearGrabbable = true;
        m_GrabbableBlockRadius = 0.16f;
    }

    // Draw the slide path in the Scene view for easy tuning
    void OnDrawGizmosSelected()
    {
        Vector3 start = Application.isPlaying ? m_ClosedPosition : transform.position;
        Vector3 axis  = transform.TransformDirection(m_SlideAxis.normalized);
        Vector3 end   = start + axis * m_SlideDistance;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawSphere(end, 0.06f);
    }
}
