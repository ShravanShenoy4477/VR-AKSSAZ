using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Wall-mounted clock that responds to XR selection (ray or direct grab).
/// Rotates a hinge pivot to swing the clock open/closed and can reveal optional clue objects.
/// Add to the same GameObject as an XR Interactable and a non-trigger <see cref="Collider"/>.
/// </summary>
/// <remarks>
/// <b>Hierarchy:</b> Put this component on the clock root (with collider + XR interactable).
/// Create an empty child <c>HingePivot</c> at the wall edge where the clock should swing from,
/// then parent the clock mesh/visual under <c>HingePivot</c> with a local offset so the face stays centered.
/// Assign that empty to <see cref="m_HingePivot"/>. Leave it unset to rotate the clock root (center tilt).
/// </remarks>
public class WallClockInteractable : MonoBehaviour
{
    [Header("Hinge")]
    [Tooltip("Transform that receives local rotation around LocalSwingAxis (e.g. empty at the left edge of the clock).")]
    [SerializeField]
    Transform m_HingePivot;

    [Tooltip("Axis to rotate around in the hinge's local space. Use (0,1,0) for a side hinge swing, (0,0,1) for roll around local Z, (1,0,0) to tilt the face away from the wall.")]
    [SerializeField]
    Vector3 m_LocalSwingAxis = Vector3.up;

    [Tooltip("Degrees from closed to fully open (use a small value for a subtle tilt reveal).")]
    [SerializeField]
    float m_OpenAngleDegrees = 55f;

    [SerializeField]
    float m_AnimationDuration = 0.35f;

    [Tooltip("Animation curve; 0 = start, 1 = end of motion.")]
    [SerializeField]
    AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Clue (optional)")]
    [SerializeField]
    GameObject m_ClueRoot;

    [Tooltip("If true, clue is disabled on load and only shown while open.")]
    [SerializeField]
    bool m_HideClueWhenClosed = true;

    [Header("Events")]
    [SerializeField]
    UnityEvent m_OnOpened;

    [SerializeField]
    UnityEvent m_OnClosed;

    [Header("Interaction Mode")]
    [SerializeField]
    [Tooltip("If true, clock opens when selected and closes on release (great for near-hand grab). If false, selection toggles open/close.")]
    bool m_HoldOpenWhileSelected = true;

    [SerializeField]
    [Tooltip("If enabled, Reset creates an edge HingePivot child (door swing). Leave off for center-pivot tilt on this transform.")]
    bool m_CreateEdgeHingePivotInEditor;

    XRBaseInteractable m_Interactable;
    Quaternion m_ClosedLocalRotation;
    bool m_IsOpen;
    bool m_IsAnimating;
    Coroutine m_AnimateRoutine;

    public bool IsOpen => m_IsOpen;

    void Awake()
    {
        m_Interactable = GetComponent<XRBaseInteractable>();
        if (m_HingePivot == null)
            m_HingePivot = transform;

        m_ClosedLocalRotation = m_HingePivot.localRotation;

        // This object is wall-mounted; if a Rigidbody exists, keep it fixed.
        if (TryGetComponent<Rigidbody>(out var rb))
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        if (m_Ease == null || m_Ease.length == 0)
            m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        if (m_ClueRoot != null && m_HideClueWhenClosed)
            m_ClueRoot.SetActive(false);
    }

    void OnEnable()
    {
        if (m_Interactable != null)
        {
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }
    }

    void OnDisable()
    {
        if (m_Interactable != null)
        {
            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
        }
    }

    void OnSelectEntered(SelectEnterEventArgs _)
    {
        if (m_IsAnimating)
            return;

        bool targetOpen = m_HoldOpenWhileSelected ? true : !m_IsOpen;
        if (m_AnimateRoutine != null)
            StopCoroutine(m_AnimateRoutine);
        m_AnimateRoutine = StartCoroutine(AnimateToState(targetOpen));
    }

    void OnSelectExited(SelectExitEventArgs _)
    {
        if (!m_HoldOpenWhileSelected || m_IsAnimating || !m_IsOpen)
            return;

        if (m_AnimateRoutine != null)
            StopCoroutine(m_AnimateRoutine);
        m_AnimateRoutine = StartCoroutine(AnimateToState(false));
    }

    IEnumerator AnimateToState(bool open)
    {
        m_IsAnimating = true;
        float fromT = m_IsOpen ? 1f : 0f;
        float toT = open ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < m_AnimationDuration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / m_AnimationDuration);
            float smooth = m_Ease.Evaluate(u);
            float t = Mathf.Lerp(fromT, toT, smooth);
            ApplyAngleT(t);
            yield return null;
        }

        ApplyAngleT(toT);
        m_IsOpen = open;
        m_IsAnimating = false;
        m_AnimateRoutine = null;

        if (m_ClueRoot != null && m_HideClueWhenClosed)
            m_ClueRoot.SetActive(open);

        if (open)
            m_OnOpened?.Invoke();
        else
            m_OnClosed?.Invoke();
    }

    /// <summary>t = 0 closed, 1 open.</summary>
    void ApplyAngleT(float t)
    {
        float angle = Mathf.Lerp(0f, m_OpenAngleDegrees, t);
        m_HingePivot.localRotation = m_ClosedLocalRotation * Quaternion.AngleAxis(angle, m_LocalSwingAxis);
    }

#if UNITY_EDITOR
    void Reset()
    {
        if (GetComponent<Collider>() == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.35f, 0.35f, 0.06f);
            box.center = Vector3.zero;
        }

        if (m_CreateEdgeHingePivotInEditor && m_HingePivot == null)
        {
            var hingeGo = new GameObject("HingePivot");
            hingeGo.transform.SetParent(transform, false);
            hingeGo.transform.localPosition = new Vector3(-0.12f, 0f, 0f);
            hingeGo.transform.localRotation = Quaternion.identity;
            m_HingePivot = hingeGo.transform;
        }
    }
#endif
}
