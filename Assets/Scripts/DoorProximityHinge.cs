using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Door that opens/closes by proximity:
/// when the given key object enters the trigger collider on this GameObject,
/// rotate the hinge pivot by <see cref="m_OpenAngleDegrees"/> around <see cref="m_LocalSwingAxis"/>.
/// </summary>
[DisallowMultipleComponent]
public class DoorProximityHinge : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform that will be rotated (hinge pivot). Defaults to this.transform.")]
    [SerializeField] Transform m_HingePivot;

    [Tooltip("Keys object root. If any collider belongs to this object (or its children), the door will open. If null, any collider can trigger (not recommended).")]
    [SerializeField] GameObject m_KeyObject;

    [Tooltip("If true and Key Object is empty, the first XRGrabInteractable collider that enters will be wired as the key object.")]
    [SerializeField] bool m_AutoWireKeyObject = true;

    [Header("Animation")]
    [SerializeField] Vector3 m_LocalSwingAxis = Vector3.up;
    [Tooltip("Positive opens counter-clockwise. Negative opens clockwise. Use -90 for a clockwise swing.")]
    [SerializeField] float m_OpenAngleDegrees = -90f;
    [SerializeField] float m_AnimationDuration = 0.35f;
    [SerializeField] AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Behavior")]
    [Tooltip("Open when keys enter the trigger, close when keys exit.")]
    [SerializeField] bool m_OpenOnEnter = true;

    [Tooltip("Close when keys exit the trigger. If false, door stays open until scene reload.")]
    [SerializeField] bool m_CloseOnExit = true;

    [Tooltip("If true, the first successful key trigger permanently unlocks the door and keeps it open.")]
    [SerializeField] bool m_StayOpenAfterUnlock = false;

    /// <summary>Fired once the first time this door swings open.</summary>
    public static event System.Action OnDoorOpened;

    int m_InsideKeyCount;
    bool m_IsOpen;
    bool m_IsAnimating;
    bool m_IsUnlocked;
    bool m_HasFiredOpenEvent;
    bool m_KeyAttachedToHinge;
    Coroutine m_Routine;
    Quaternion m_ClosedLocalRotation;

    void Awake()
    {
        if (m_HingePivot == null)
            m_HingePivot = transform;

        m_ClosedLocalRotation = m_HingePivot.localRotation;

        if (m_Ease == null || m_Ease.length == 0)
            m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            col.isTrigger = true; // Ensure this object is acting as the proximity trigger.

        Debug.Log($"[DoorProximityHinge] Awake on {gameObject.name}, hinge='{m_HingePivot.name}', key='{(m_KeyObject != null ? m_KeyObject.name : "<unassigned>")}', openAngle={m_OpenAngleDegrees}");
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsKeyCollider(other))
            return;

        if (m_IsUnlocked && m_StayOpenAfterUnlock)
            return;

        m_InsideKeyCount++;
        if (m_InsideKeyCount == 1 && m_OpenOnEnter)
        {
            if (m_StayOpenAfterUnlock)
                m_IsUnlocked = true;

            AttachKeyToHinge(other);
            StartAnimate(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsKeyCollider(other))
            return;

        if (m_IsUnlocked && m_StayOpenAfterUnlock)
            return;

        m_InsideKeyCount = Mathf.Max(0, m_InsideKeyCount - 1);
        if (m_InsideKeyCount == 0 && m_CloseOnExit)
            StartAnimate(false);
    }

    void AttachKeyToHinge(Collider other)
    {
        if (m_KeyAttachedToHinge)
            return;

        if (m_KeyObject == null || other == null)
            return;

        Transform keyRoot = m_KeyObject.transform;
        Transform attachRoot = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform.root
            : other.transform.root;

        if (attachRoot != keyRoot && !other.transform.IsChildOf(keyRoot))
            return;

        m_KeyAttachedToHinge = true;

        m_KeyObject.transform.SetParent(m_HingePivot, true);
        m_KeyObject.transform.localPosition = Vector3.zero;
        m_KeyObject.transform.localRotation = Quaternion.identity;

        var grab = m_KeyObject.GetComponentInChildren<XRGrabInteractable>();
        if (grab != null)
        {
            grab.selectEntered.RemoveAllListeners();
            grab.selectExited.RemoveAllListeners();
            grab.enabled = false;
            Debug.Log($"[DoorProximityHinge] Disabled grab on attached key '{m_KeyObject.name}'.");
        }

        var rb = m_KeyObject.GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
            Debug.Log($"[DoorProximityHinge] Made attached key '{m_KeyObject.name}' kinematic.");
        }

        Debug.Log($"[DoorProximityHinge] Key '{m_KeyObject.name}' attached to hinge '{m_HingePivot.name}'.");
    }

    bool IsKeyCollider(Collider other)
    {
        if (other == null)
            return false;

        if (m_KeyObject == null)
        {
            var grab = other.GetComponentInParent<XRGrabInteractable>();
            if (grab == null)
                return false;

            if (m_AutoWireKeyObject)
            {
                m_KeyObject = grab.gameObject;
                Debug.Log($"[DoorProximityHinge] Auto-wired key object '{m_KeyObject.name}' to door '{gameObject.name}'.");
            }

            return true;
        }

        Transform keyRoot = m_KeyObject.transform;
        Transform otherRoot = other.transform.root;

        // Most reliable when keys have a rigidbody: use rigidbody root.
        if (other.attachedRigidbody != null)
            otherRoot = other.attachedRigidbody.transform.root;

        return otherRoot == keyRoot || other.transform.IsChildOf(keyRoot);
    }

    void StartAnimate(bool open)
    {
        if (m_Routine != null)
            StopCoroutine(m_Routine);

        m_Routine = StartCoroutine(AnimateToState(open));
    }

    IEnumerator AnimateToState(bool open)
    {
        if (open == m_IsOpen && !m_IsAnimating)
        {
            m_Routine = null;
            yield break;
        }

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
        m_Routine = null;

        if (open && !m_HasFiredOpenEvent)
        {
            m_HasFiredOpenEvent = true;
            OnDoorOpened?.Invoke();
        }
    }

    void ApplyAngleT(float t)
    {
        float angle = Mathf.Lerp(0f, m_OpenAngleDegrees, t);
        m_HingePivot.localRotation = m_ClosedLocalRotation * Quaternion.AngleAxis(angle, m_LocalSwingAxis);
    }
}

