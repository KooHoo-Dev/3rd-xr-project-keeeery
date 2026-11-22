using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BreakMyRoom
{
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public sealed class ToolDock : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform dockAnchor;

        [Header("Return")]
        [SerializeField] private float returnDelay = 0.35f;

        private XRGrabInteractable grabInteractable;
        private Rigidbody toolBody;
        private Vector3 dockPosition;
        private Quaternion dockRotation;
        private float returnAt = -1f;
        private bool isDocked = true;

        public Transform DockAnchor => dockAnchor;
        public bool IsDocked => isDocked;

        public void Configure(Transform anchor)
        {
            dockAnchor = anchor;
        }

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            toolBody = GetComponent<Rigidbody>();
            dockPosition = transform.position;
            dockRotation = transform.rotation;
        }

        private void OnEnable()
        {
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            returnAt = -1f;
            isDocked = false;
            if (dockAnchor != null && transform.parent != null) transform.SetParent(null, true);
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            returnAt = Time.time + returnDelay;
        }

        private void LateUpdate()
        {
            // XRI 놓기 처리 후 복귀 (같은 프레임 포즈 덮어쓰기 방지)
            if (returnAt >= 0f && Time.time >= returnAt && grabInteractable.isSelected == false) ReturnToDock();

            if (isDocked && dockAnchor != null && grabInteractable.isSelected == false) ApplyDockPose();
        }

        public void ReturnToDock()
        {
            if (grabInteractable.isSelected) return;

            returnAt = -1f;
            isDocked = true;
            ApplyDockPose();
        }

        private void ApplyDockPose()
        {
            Vector3 position = dockAnchor != null ? dockAnchor.position : dockPosition;
            Quaternion rotation = dockAnchor != null ? dockAnchor.rotation : dockRotation;
            toolBody.position = position;
            toolBody.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
