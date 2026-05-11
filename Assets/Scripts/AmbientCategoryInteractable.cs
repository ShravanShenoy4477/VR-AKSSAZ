using System.Collections;
using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class AmbientCategoryInteractable : MonoBehaviour
{
    static float s_GlobalInteractCooldownUntil;

    public enum MotionKind { Slide, Spin, Tilt, Nudge }

    [SerializeField] MotionKind motionKind = MotionKind.Nudge;
    [SerializeField] float interactRadius = 0.38f;
    [SerializeField] float motionDuration = 0.28f;
    [SerializeField] float slideDistance = 0.28f;
    [SerializeField] float angleDegrees = 24f;
    [SerializeField] Vector3 localAxis = Vector3.up;

    Transform _right;
    Transform _left;
    bool _prevR;
    bool _prevL;
    bool _busy;
    Vector3 _basePos;
    Quaternion _baseRot;
    Coroutine _routine;

    void Awake()
    {
        _basePos = transform.localPosition;
        _baseRot = transform.localRotation;

        var r = GameObject.Find("RightHandController");
        if (r != null) _right = r.transform;
        var l = GameObject.Find("LeftHandController");
        if (l != null) _left = l.transform;

        EnsureCollider();
    }

    void EnsureCollider()
    {
        if (GetComponent<Collider>() != null) return;
        var rend = GetComponentInChildren<Renderer>();
        if (rend == null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        var b = rend.bounds;
        var c = transform.InverseTransformPoint(b.center);
        box.center = c;
        box.size = new Vector3(
            Mathf.Max(0.06f, b.size.x),
            Mathf.Max(0.06f, b.size.y),
            Mathf.Max(0.06f, b.size.z));
    }

    void Update()
    {
        if (_busy) return;
        if (Time.unscaledTime < s_GlobalInteractCooldownUntil) return;
        bool edgeR = ReadEdge(XRNode.RightHand, ref _prevR);
        bool edgeL = ReadEdge(XRNode.LeftHand, ref _prevL);
        if (!edgeR && !edgeL) return;

        bool nearR = _right != null && Vector3.Distance(_right.position, transform.position) <= interactRadius;
        bool nearL = _left != null && Vector3.Distance(_left.position, transform.position) <= interactRadius;
        bool tryR = edgeR && nearR;
        bool tryL = edgeL && nearL;
        if (!(tryR || tryL)) return;
        if (tryR && _right != null && !IsNearestForPoint(_right.position)) return;
        if (tryL && _left != null && !IsNearestForPoint(_left.position)) return;
        s_GlobalInteractCooldownUntil = Time.unscaledTime + 0.08f;

        if (tryR) XrHaptics.PulseRight(0.16f, 0.03f);
        if (tryL) XrHaptics.PulseLeft(0.16f, 0.03f);
        InteractableHapticFeedback.ShowWrongOrderCue(transform);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(AnimatePreview());
    }

    bool IsNearestForPoint(Vector3 handPos)
    {
        float mine = Vector3.Distance(handPos, transform.position);
        if (mine > interactRadius) return false;

        foreach (var other in FindObjectsByType<AmbientCategoryInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (other == null || other == this || !other.isActiveAndEnabled) continue;
            float d = Vector3.Distance(handPos, other.transform.position);
            if (d + 0.01f < mine && d <= interactRadius)
                return false;
        }
        return true;
    }

    bool ReadEdge(XRNode node, ref bool prev)
    {
        var d = InputDevices.GetDeviceAtXRNode(node);
        if (!d.isValid)
        {
            prev = false;
            return false;
        }

        bool pressed = false;
        d.TryGetFeatureValue(CommonUsages.gripButton, out pressed);
        bool edge = pressed && !prev;
        prev = pressed;
        return edge;
    }

    IEnumerator AnimatePreview()
    {
        _busy = true;
        float dur = Mathf.Max(0.12f, motionDuration);

        Vector3 fromPos = transform.localPosition;
        Quaternion fromRot = transform.localRotation;
        Vector3 peakPos = _basePos;
        Quaternion peakRot = _baseRot;

        switch (motionKind)
        {
            case MotionKind.Slide:
                peakPos = _basePos + localAxis.normalized * slideDistance;
                break;
            case MotionKind.Spin:
                peakRot = _baseRot * Quaternion.AngleAxis(angleDegrees, localAxis.normalized);
                break;
            case MotionKind.Tilt:
                peakRot = _baseRot * Quaternion.AngleAxis(angleDegrees, localAxis.normalized);
                break;
            default:
                peakRot = _baseRot * Quaternion.AngleAxis(10f, localAxis.normalized);
                break;
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            u = u * u * (3f - 2f * u);
            transform.localPosition = Vector3.Lerp(fromPos, peakPos, u);
            transform.localRotation = Quaternion.Slerp(fromRot, peakRot, u);
            yield return null;
        }

        t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            u = u * u * (3f - 2f * u);
            transform.localPosition = Vector3.Lerp(peakPos, _basePos, u);
            transform.localRotation = Quaternion.Slerp(peakRot, _baseRot, u);
            yield return null;
        }

        transform.localPosition = _basePos;
        transform.localRotation = _baseRot;
        _busy = false;
        _routine = null;
    }

    public void Configure(MotionKind kind, Vector3 axis, float angle, float distance)
    {
        motionKind = kind;
        localAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
        angleDegrees = angle;
        slideDistance = distance;
    }
}
