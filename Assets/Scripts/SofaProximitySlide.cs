using System.Collections;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Slides the sofa when the right controller enters the zone and the grip is pressed.
/// Toggles each press: first grip slides it open, next grip slides it back.
/// No Rigidbody, no XRGrabInteractable, no collider changes needed.
///
/// Setup:
///   1. Attach this script to the sofa GameObject.
///   2. Assign Right Controller in the Inspector (or name it "RightHandController").
///   3. Tune Slide Axis and Slide Distance so the sofa reveals the clue underneath.
///      - Slide Axis (1,0,0)  = slides along sofa's local X (left / right)
///      - Slide Axis (0,0,1)  = slides along sofa's local Z (forward / back)
///   4. The Zone collider defaults to the collider already on this GameObject.
///      Resize it in the Inspector so it covers roughly arm-reach around the sofa.
/// </summary>
[DisallowMultipleComponent]
public class SofaProximitySlide : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Proximity zone collider. Defaults to the Collider on this GameObject.")]
    [SerializeField] Collider m_Zone;

    [Tooltip("Right hand controller transform. Leave blank to auto-find 'RightHandController'.")]
    [SerializeField] Transform m_RightController;

    [Header("Slide")]
    [Tooltip("Local-space axis to slide along. (1,0,0)=right  (0,0,1)=forward  etc.")]
    [SerializeField] Vector3 m_SlideAxis = new Vector3(1f, 0f, 0f);

    [Tooltip("How far the sofa slides (metres).")]
    [SerializeField] float m_SlideDistance = 1.2f;

    [Tooltip("Duration of the slide animation (seconds).")]
    [SerializeField] float m_AnimationDuration = 0.5f;

    [SerializeField] AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── Runtime ───────────────────────────────────────────────────────────────
    Vector3   m_ClosedPosition;
    Vector3   m_OpenPosition;
    bool      m_IsOpen;
    bool      m_IsAnimating;
    bool      m_PrevGrip;
    Coroutine m_Routine;

    // ── Awake ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        // Zone collider
        if (m_Zone == null)
        {
            m_Zone = GetComponent<Collider>();
            if (m_Zone == null)
            {
                // Add a generous trigger zone if none exists
                var box       = gameObject.AddComponent<BoxCollider>();
                box.size      = new Vector3(2.0f, 1.2f, 2.0f);
                box.isTrigger = true;
                m_Zone        = box;
                Debug.Log("SofaProximitySlide: added default BoxCollider zone.");
            }
        }
        // Do NOT set isTrigger — ClosestPoint works on solid colliders too,
        // and making it a trigger would remove floor collision causing the sofa to fall.

        // Right controller
        if (m_RightController == null)
        {
            var go = GameObject.Find("RightHandController");
            if (go != null)
                m_RightController = go.transform;
            else
                Debug.LogWarning("SofaProximitySlide: assign Right Controller in the Inspector.");
        }

        if (m_Ease == null || m_Ease.length == 0)
            m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // Record rest position; compute open position in world space
        m_ClosedPosition = transform.position;
        Vector3 worldAxis = transform.TransformDirection(m_SlideAxis.normalized);
        m_OpenPosition   = m_ClosedPosition + worldAxis * m_SlideDistance;
    }

    // ── Update ────────────────────────────────────────────────────────────────
    void Update()
    {
        if (m_IsAnimating || m_Zone == null || m_RightController == null) return;

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return;
        if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip)) return;

        bool pressedEdge = grip && !m_PrevGrip;
        m_PrevGrip = grip;

        if (!pressedEdge) return;

        // Only act if the right hand is inside the zone
        if (!IsInsideZone(m_Zone, m_RightController.position)) return;

        // Toggle slide
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
    static bool IsInsideZone(Collider zone, Vector3 worldPoint)
    {
        return (zone.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude < 1e-6f;
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
