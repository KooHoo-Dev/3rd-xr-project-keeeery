using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BreakMyRoom
{
    // Grip 시 잡히는 도구의 흰 외곽선 표시, 잡으면 숨김
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class ToolHighlight : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Renderer[] outlines;

        private XRGrabInteractable grabInteractable;
        private bool visible;

        public bool IsOutlined => visible;

        public void Configure(Renderer[] outlineRenderers)
        {
            outlines = outlineRenderers;
        }

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            grabInteractable.hoverEntered.AddListener(OnHoverEntered);
            grabInteractable.hoverExited.AddListener(OnHoverExited);
            grabInteractable.selectEntered.AddListener(OnSelectEntered);
            grabInteractable.selectExited.AddListener(OnSelectExited);
            Refresh();
        }

        private void OnDisable()
        {
            grabInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            grabInteractable.hoverExited.RemoveListener(OnHoverExited);
            grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
            grabInteractable.selectExited.RemoveListener(OnSelectExited);
            SetVisible(false);
        }

        private void OnHoverEntered(HoverEnterEventArgs args) => Refresh();
        private void OnHoverExited(HoverExitEventArgs args) => Refresh();
        private void OnSelectEntered(SelectEnterEventArgs args) => Refresh();
        private void OnSelectExited(SelectExitEventArgs args) => Refresh();

        private void Refresh()
        {
            SetVisible(grabInteractable.isHovered && grabInteractable.isSelected == false);
        }

        private void SetVisible(bool show)
        {
            if (visible == show) return;

            visible = show;
            foreach (Renderer outline in outlines)
                outline.enabled = show;
        }
    }
}
