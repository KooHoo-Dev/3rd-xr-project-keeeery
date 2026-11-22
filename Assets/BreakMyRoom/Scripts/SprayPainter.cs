using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BreakMyRoom
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class SprayPainter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform nozzle;
        [SerializeField] private PaintMarkPool paintPool;

        [Header("Marks")]
        // 스프레이는 페인트, 화염방사기는 그을음 (분사·도색 방식 동일)
        [SerializeField] private PaintMarkPool.MarkStyle markStyle = PaintMarkPool.MarkStyle.Spray;

        [Header("Spray")]
        [SerializeField] private float maximumDistance = 3f;
        [SerializeField] private float markSpacing = 0.025f;
        [SerializeField] private float maximumStrokeJump = 0.35f;

        [Header("Mist")]
        [SerializeField] private ParticleSystem mist;
        [SerializeField] private float minimumMistLifetime = 0.05f;

        private XRGrabInteractable grabInteractable;
        private XRBaseInputInteractor holdingHand;
        private float mistSpeed;
        private float maximumMistLifetime;
        private bool hasStroke;
        private Vector3 lastMarkPoint;
        private Vector3 previousHitPoint;
        private Vector3 previousNormal;

        public void Configure(Transform nozzleTip, PaintMarkPool pool, ParticleSystem sprayMist)
        {
            nozzle = nozzleTip;
            paintPool = pool;
            mist = sprayMist;
        }

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            ParticleSystem.MainModule main = mist.main;
            mistSpeed = main.startSpeedMultiplier;
            maximumMistLifetime = main.startLifetimeMultiplier;
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
            ResetStroke();
            mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            holdingHand = args.interactorObject as XRBaseInputInteractor;
            ResetStroke();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            holdingHand = null;
            ResetStroke();
        }

        private void LateUpdate()
        {
            // 누름 이벤트 대신 현재 상태 확인 (트리거를 누른 채 잡아도 동작)
            bool spraying = grabInteractable.isSelected && holdingHand != null && holdingHand.activateInput.ReadIsPerformed();
            SetMistEmitting(spraying);
            if (spraying == false)
            {
                ResetStroke();
                return;
            }

            // 앞 장애물 검사 (물체 너머 뒤 벽 도색 방지)
            bool hasHit = Physics.Raycast(nozzle.position, nozzle.forward, out RaycastHit hit, maximumDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            SetMistLength(hasHit ? hit.distance : maximumDistance);
            if (hasHit == false || paintPool.IsPaintable(hit.collider) == false)
            {
                ResetStroke();
                return;
            }

            // Collider가 바뀌어도 같은 방향의 면이면 선 연결 (여러 칸으로 나뉜 벽)
            if (hasStroke == false || Vector3.Dot(previousNormal, hit.normal) < 0.98f ||
                (hit.point - previousHitPoint).sqrMagnitude > maximumStrokeJump * maximumStrokeJump)
            {
                paintPool.AddMark(hit.point, hit.normal, hit.collider, markStyle);
                lastMarkPoint = hit.point;
            }
            else
            {
                Vector3 displacement = hit.point - lastMarkPoint;
                float distance = displacement.magnitude;
                if (distance >= markSpacing)
                {
                    Vector3 direction = displacement / distance;
                    int count = Mathf.FloorToInt(distance / markSpacing);
                    for (int i = 0; i < count; i++)
                    {
                        lastMarkPoint += direction * markSpacing;
                        PaintVisibleSample(lastMarkPoint, hit.normal);
                    }
                }
            }
            hasStroke = true;
            previousHitPoint = hit.point;
            previousNormal = hit.normal;
        }

        private void PaintVisibleSample(Vector3 point, Vector3 normal)
        {
            Vector3 direction = point - nozzle.position;
            float distance = direction.magnitude;
            if (distance > maximumDistance) return;

            // 양 끝 사이의 좁은 장애물 뒤 도색 방지
            if (Physics.Raycast(nozzle.position, direction / distance, out RaycastHit hit, distance + 0.01f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                Vector3.Dot(hit.normal, normal) >= 0.98f && paintPool.IsPaintable(hit.collider) &&
                (hit.point - point).sqrMagnitude < 0.0001f)
                paintPool.AddMark(hit.point, hit.normal, hit.collider, markStyle);
        }

        public void ClearPaint()
        {
            paintPool.ClearMarks();
            ResetStroke();
        }

        private void ResetStroke()
        {
            hasStroke = false;
        }

        private void SetMistEmitting(bool emitting)
        {
            if (mist.isEmitting == emitting) return;

            if (emitting) mist.Play(true);
            else mist.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // 맞은 면을 뚫고 지나가지 않도록 거리에 맞춘 입자 수명
        private void SetMistLength(float distance)
        {
            ParticleSystem.MainModule main = mist.main;
            main.startLifetimeMultiplier = Mathf.Clamp(distance / mistSpeed, minimumMistLifetime, maximumMistLifetime);
        }
    }
}
