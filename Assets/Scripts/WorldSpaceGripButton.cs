using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// HUD / world-space buttons: grip must stay strong inside a small box for a short time, then fires.
/// Leaves <see cref="Image.raycastTarget"/> on so XR UI ray + trigger still works as a second path.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class WorldSpaceGripButton : MonoBehaviour
{
    Action _onPressed;
    Func<bool> _canPress;

    [SerializeField] Transform rightController;
    [SerializeField] Transform leftController;

    BoxCollider _zone;
    float _suppressUntil;
    [SerializeField] float openGraceSeconds = 0.2f;
    [SerializeField] float gripSustainMin = 0.78f;
    [SerializeField] float gripSustainSeconds = 0.14f;
    [SerializeField] bool analogGripOnly;

    float _sustainR;
    float _sustainL;
    bool _armed = true;

    public static void Attach(Button button, Action onPressed)
    {
        if (button == null || onPressed == null) return;
        var go = button.gameObject;
        if (go.GetComponent<WorldSpaceGripButton>() != null) return;

        var img = button.GetComponent<Image>();
        if (img != null) img.raycastTarget = true;

        var rt = button.GetComponent<RectTransform>();
        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = go.AddComponent<BoxCollider>();
        var r = rt.rect;
        box.size = new Vector3(Mathf.Abs(r.width), Mathf.Abs(r.height), 0.04f);
        box.center = Vector3.zero;
        box.isTrigger = true;

        var g = go.AddComponent<WorldSpaceGripButton>();
        g.Initialize(button, onPressed);
    }

    public void Initialize(Button button, Action onPressed)
    {
        _canPress = () => button != null && button.interactable;
        _onPressed = () =>
        {
            if (button != null && button.interactable)
                onPressed();
        };
        _suppressUntil = Time.time + openGraceSeconds;
        _armed = true;
        _sustainR = _sustainL = 0f;
    }

    void Awake()
    {
        _zone = GetComponent<BoxCollider>();
        _zone.isTrigger = true;
    }

    void OnEnable()
    {
        if (_canPress == null) return;
        _suppressUntil = Time.time + openGraceSeconds;
        _armed = true;
        _sustainR = _sustainL = 0f;
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
        bool inR = rightController != null && IsInside(_zone, rightController.position);
        bool inL = leftController != null && IsInside(_zone, leftController.position);

        if (!_armed)
        {
            if (!inR && !inL)
            {
                _armed = true;
                _sustainR = _sustainL = 0f;
            }

            return;
        }

        if (_canPress == null || !_canPress()) return;
        if (Time.time < _suppressUntil) return;

        bool fired = false;
        if (inR) fired |= StepSustain(XRNode.RightHand, ref _sustainR);
        if (inL) fired |= StepSustain(XRNode.LeftHand, ref _sustainL);
        if (!inR) _sustainR = 0f;
        if (!inL) _sustainL = 0f;

        if (fired)
        {
            _onPressed?.Invoke();
            _armed = false;
            _sustainR = _sustainL = 0f;
        }
    }

    bool StepSustain(XRNode node, ref float acc)
    {
        float g = ReadGrip(node);
        if (g < gripSustainMin)
        {
            acc = 0f;
            return false;
        }

        acc += Time.deltaTime;
        if (acc < gripSustainSeconds) return false;
        acc = 0f;
        return true;
    }

    float ReadGrip(XRNode node)
    {
        var dev = InputDevices.GetDeviceAtXRNode(node);
        if (!dev.isValid) return -1f;
        if (dev.TryGetFeatureValue(CommonUsages.grip, out float axis))
            return Mathf.Clamp01(axis);
        if (analogGripOnly) return -1f;
        return dev.TryGetFeatureValue(CommonUsages.gripButton, out bool p) && p ? 1f : 0f;
    }

    static bool IsInside(Collider zone, Vector3 p)
    {
        return (zone.ClosestPoint(p) - p).sqrMagnitude < 1e-6f;
    }
}
