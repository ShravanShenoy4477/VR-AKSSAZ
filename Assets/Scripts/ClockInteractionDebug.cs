using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Runtime debug helper for XR interaction on the wall clock.
/// </summary>
[DisallowMultipleComponent]
public class ClockInteractionDebug : MonoBehaviour
{
    [SerializeField]
    string m_LogTag = "Clock_Wall12_Debug";

    [SerializeField]
    float m_TriggerLogInterval = 0.35f;

    XRBaseInteractable m_Interactable;
    Collider m_Collider;
    Rigidbody m_Rigidbody;
    float m_LastTriggerLogTime;

    void Awake()
    {
        m_Interactable = GetComponent<XRBaseInteractable>();
        m_Collider = GetComponent<Collider>();
        m_Rigidbody = GetComponent<Rigidbody>();

        Debug.Log($"{m_LogTag}: Awake on '{name}'. " +
                  $"interactable={(m_Interactable != null ? m_Interactable.GetType().Name : "none")}, " +
                  $"collider={(m_Collider != null ? m_Collider.GetType().Name : "none")} trigger={(m_Collider != null && m_Collider.isTrigger)}, " +
                  $"rb={(m_Rigidbody != null)} kinematic={(m_Rigidbody != null && m_Rigidbody.isKinematic)} gravity={(m_Rigidbody != null && m_Rigidbody.useGravity)}");
    }

    void OnEnable()
    {
        if (m_Interactable == null)
            return;

        m_Interactable.hoverEntered.AddListener(OnHoverEntered);
        m_Interactable.hoverExited.AddListener(OnHoverExited);
        m_Interactable.selectEntered.AddListener(OnSelectEntered);
        m_Interactable.selectExited.AddListener(OnSelectExited);
        m_Interactable.activated.AddListener(OnActivated);
        m_Interactable.deactivated.AddListener(OnDeactivated);
    }

    void OnDisable()
    {
        if (m_Interactable == null)
            return;

        m_Interactable.hoverEntered.RemoveListener(OnHoverEntered);
        m_Interactable.hoverExited.RemoveListener(OnHoverExited);
        m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
        m_Interactable.selectExited.RemoveListener(OnSelectExited);
        m_Interactable.activated.RemoveListener(OnActivated);
        m_Interactable.deactivated.RemoveListener(OnDeactivated);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        Debug.Log($"{m_LogTag}: hover entered by '{args.interactorObject?.transform?.name}'.");
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        Debug.Log($"{m_LogTag}: hover exited by '{args.interactorObject?.transform?.name}'.");
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        Debug.Log($"{m_LogTag}: SELECT ENTER by '{args.interactorObject?.transform?.name}'.");
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        Debug.Log($"{m_LogTag}: SELECT EXIT by '{args.interactorObject?.transform?.name}'.");
    }

    void OnActivated(ActivateEventArgs args)
    {
        Debug.Log($"{m_LogTag}: activated by '{args.interactorObject?.transform?.name}'.");
    }

    void OnDeactivated(DeactivateEventArgs args)
    {
        Debug.Log($"{m_LogTag}: deactivated by '{args.interactorObject?.transform?.name}'.");
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"{m_LogTag}: trigger enter from '{other.name}' layer={other.gameObject.layer}.");
    }

    void OnTriggerStay(Collider other)
    {
        if (Time.time - m_LastTriggerLogTime < m_TriggerLogInterval)
            return;

        m_LastTriggerLogTime = Time.time;
        Debug.Log($"{m_LogTag}: trigger stay with '{other.name}'.");
    }

    void OnTriggerExit(Collider other)
    {
        Debug.Log($"{m_LogTag}: trigger exit from '{other.name}'.");
    }
}
