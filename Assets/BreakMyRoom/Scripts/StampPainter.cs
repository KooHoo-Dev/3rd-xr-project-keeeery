using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BreakMyRoom
{
    // 검지 Trigger 시 밑면이 향한 면에 도장 찍기
    // 모서리·구석에서는 면을 따라 꺾인 자국 (PaintMarkPool.AddStamp)
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class StampPainter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform stampFace;
        [SerializeField] private PaintMarkPool markPool;

        [Header("Stamping")]
        [SerializeField] private float maximumDistance = 1.5f;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.3f;
        [SerializeField] private float hapticDuration = 0.05f;

        private XRGrabInteractable grabInteractable;
        private XRBaseInputInteractor holdingHand;
        private Rigidbody stampBody;

        public void Configure(Transform face, PaintMarkPool pool)
        {
            stampFace = face;
            markPool = pool;
        }

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            stampBody = GetComponent<Rigidbody>();
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
            holdingHand = null;
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            holdingHand = args.interactorObject as XRBaseInputInteractor;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            holdingHand = null;
        }

        private void Update()
        {
            if (grabInteractable.isSelected == false || holdingHand == null) return;

            // 누른 순간에만 찍기 (누르고 있어도 반복 없음)
            if (holdingHand.activateInput.ReadWasPerformedThisFrame()) Stamp();
        }

        public bool Stamp()
        {
            bool placed = markPool.AddStamp(stampFace.position, stampFace.forward, stampFace.up, maximumDistance, stampBody);
            if (placed) holdingHand?.SendHapticImpulse(hapticAmplitude, hapticDuration);
            return placed;
        }

        public void ClearStampMarks()
        {
            markPool.ClearMarks();
        }
    }
}
