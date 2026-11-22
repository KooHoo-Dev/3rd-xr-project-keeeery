using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BreakMyRoom
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class WatergunCleaner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform nozzle;
        [SerializeField] private LineRenderer waterJet;
        [SerializeField] private PaintMarkPool[] markPools;

        [Header("Cleaning")]
        [SerializeField] private float maximumDistance = 3f;
        [SerializeField] private float cleaningRadius = 0.10f;

        private XRGrabInteractable grabInteractable;
        private XRBaseInputInteractor holdingHand;

        public void Configure(Transform nozzleTip, LineRenderer jet, PaintMarkPool[] pools)
        {
            nozzle = nozzleTip;
            waterJet = jet;
            markPools = pools;
        }

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
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
            waterJet.enabled = false;
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            holdingHand = args.interactorObject as XRBaseInputInteractor;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            holdingHand = null;
            waterJet.enabled = false;
        }

        private void LateUpdate()
        {
            if (grabInteractable.isSelected == false || holdingHand == null || holdingHand.activateInput.ReadIsPerformed() == false)
            {
                waterJet.enabled = false;
                return;
            }

            bool hasHit = Physics.Raycast(nozzle.position, nozzle.forward, out RaycastHit hit, maximumDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            waterJet.enabled = true;
            waterJet.SetPosition(0, nozzle.position);
            waterJet.SetPosition(1, hasHit ? hit.point : nozzle.position + nozzle.forward * maximumDistance);
            if (hasHit == false) return;

            foreach (PaintMarkPool pool in markPools)
            {
                if (pool.IsPaintable(hit.collider)) pool.EraseMarks(hit.point, cleaningRadius, nozzle.position);
            }
        }
    }
}
