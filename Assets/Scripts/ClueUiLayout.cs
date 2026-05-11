using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared layout for headset-facing clue cards so distance and scale stay comfortable in VR.
/// </summary>
public static class ClueUiLayout
{
    public const float PanelForwardMeters = 0.30f;
    public const float PanelHeightMeters  = 0.44f;
    public const float PanelWidthMeters   = 0.36f;

    /// <summary>
    /// GOT IT / CLOSE: physical confirm only — strong grip squeeze while the controller is inside
    /// the button's box. Disables UI raycasts on this row so the XR UI ray cannot fire the button
    /// when the controller only moves near it.
    /// </summary>
    public static void WireGotItButtonForXrDirectSelect(Button gotItButton)
    {
        if (gotItButton == null) return;
        var go = gotItButton.gameObject;

        var legacy = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        if (legacy != null)
            Object.Destroy(legacy);

        if (go.GetComponent<ClueGotItGripConfirm>() != null)
        {
            DisableUiRaycastsOnButtonRow(gotItButton);
            return;
        }

        DisableUiRaycastsOnButtonRow(gotItButton);

        var rt = gotItButton.GetComponent<RectTransform>();
        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = go.AddComponent<BoxCollider>();
        var r = rt.rect;
        const float zDepth = 0.024f;
        box.size = new Vector3(Mathf.Abs(r.width), Mathf.Abs(r.height), zDepth);
        box.center = Vector3.zero;
        box.isTrigger = true;

        var grip = go.AddComponent<ClueGotItGripConfirm>();
        var btnRef = gotItButton;
        grip.Initialize(
            () => btnRef != null && btnRef.interactable,
            () =>
            {
                if (btnRef != null && btnRef.interactable)
                    btnRef.onClick.Invoke();
            });
    }

    static void DisableUiRaycastsOnButtonRow(Button btn)
    {
        var img = btn.GetComponent<Image>();
        if (img != null)
            img.raycastTarget = false;

        foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
            tmp.raycastTarget = false;

        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
    }
}
