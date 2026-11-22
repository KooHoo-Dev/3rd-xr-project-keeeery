using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BreakMyRoom
{
    // 한 번 휘두를 때 휘두른 방향의 가장 가까운 대상 하나 파괴
    // 머리 진행 방향 Reach 안의 목표 탐색, Contact Distance 안이면 즉시, 아니면 휘두르기 끝에 파괴
    // 닿지 않은 목표는 시선 안의 대상만
    // 렌더 프레임 기준 속도 측정 (50Hz 물리 주기는 72·90Hz 화면과 어긋남)
    [RequireComponent(typeof(XRGrabInteractable), typeof(AudioSource))]
    public sealed class HammerImpact : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform head;
        // 플레이어 머리(XR 카메라), 비면 잡을 때 Main Camera
        [SerializeField] private Transform viewer;

        [Header("Swing")]
        [SerializeField] private float headRadius = 0.06f;
        // 머리 진행 방향의 목표 탐색 거리
        [SerializeField] private float reach = 0.5f;
        // 휘두르기 도중 즉시 파괴 거리
        [SerializeField] private float contactDistance = 0.08f;
        [SerializeField] private float minimumImpactSpeed = 1.2f;
        // 휘두르기 종료 기준 (최소 타격 속도 대비 비율)
        [SerializeField, Range(0.1f, 1f)] private float releaseRatio = 0.5f;
        // 닿지 않은 휘두르기의 시선 허용 각도
        // 망치를 머리 위로 들거나 뒤로 젖힐 때의 천장·뒤쪽 파괴 방지
        [SerializeField, Range(10f, 180f)] private float aimConeAngle = 60f;

        [Header("Audio")]
        [SerializeField] private AudioClip crackSound;
        [SerializeField] private AudioClip breakSound;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.5f;
        [SerializeField] private float hapticDuration = 0.08f;

        private readonly RaycastHit[] sweepHits = new RaycastHit[64];
        private XRGrabInteractable grabInteractable;
        private XRBaseInputInteractor holdingHand;
        private AudioSource impactAudio;
        private Vector3 previousHeadPosition;
        private int warmupFrames;
        private bool tracking;
        private bool swinging;
        private bool struckThisSwing;
        private Breakable lockedTarget;
        private Vector3 lockedPoint;
        private Vector3 lockedVelocity;

        public float MinimumImpactSpeed => minimumImpactSpeed;

        public void Configure(Transform hammerHead, AudioClip firstHit, AudioClip subsequentHit)
        {
            head = hammerHead;
            crackSound = firstHit;
            breakSound = subsequentHit;
        }

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            impactAudio = GetComponent<AudioSource>();
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
            tracking = false;
            holdingHand = null;
            EndSwing();
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            holdingHand = args.interactorObject as XRBaseInputInteractor;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            previousHeadPosition = head.position;
            warmupFrames = 2;
            tracking = true;
            EndSwing();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            tracking = false;
            holdingHand = null;
            EndSwing();
        }

        // XRI가 Update에서 도구를 옮긴 뒤 읽기
        private void LateUpdate()
        {
            if (tracking == false || grabInteractable.isSelected == false) return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            Vector3 position = head.position;
            Vector3 displacement = position - previousHeadPosition;
            previousHeadPosition = position;
            // 잡을 때 스냅·추적 끊김의 순간 이동 무시
            if (warmupFrames > 0)
            {
                warmupFrames--;
                return;
            }
            if (displacement.sqrMagnitude > 1f)
            {
                EndSwing();
                return;
            }

            Vector3 velocity = displacement / deltaTime;
            float speed = velocity.magnitude;
            if (swinging == false)
            {
                if (speed < minimumImpactSpeed) return;

                swinging = true;
            }

            if (speed < minimumImpactSpeed * releaseRatio)
            {
                // 닿지 않고 끝난 휘두르기는 마지막 목표 파괴
                if (struckThisSwing == false && lockedTarget != null) Strike(lockedTarget, lockedPoint, lockedVelocity);
                EndSwing();
                return;
            }
            if (struckThisSwing || speed < minimumImpactSpeed) return;

            if (FindTarget(position, velocity / speed, out Breakable target, out Vector3 point, out float distance) == false) return;

            if (distance <= contactDistance)
            {
                Strike(target, point, velocity);
                return;
            }
            // 닿지 않은 대상은 시선 안일 때만 목표로 지정
            if (IsInView(point) == false) return;

            lockedTarget = target;
            lockedPoint = point;
            lockedVelocity = velocity;
        }

        private bool IsInView(Vector3 point)
        {
            if (viewer == null) return true;

            Vector3 direction = point - viewer.position;
            if (direction.sqrMagnitude < 0.0001f) return true;

            return Vector3.Angle(viewer.forward, direction) <= aimConeAngle;
        }

        // 머리 진행 방향 구 스윕으로 부술 수 있는 가장 가까운 대상 선택
        // 다른 도구·바깥 껍데기 등 부서지지 않는 물체 제외
        private bool FindTarget(Vector3 position, Vector3 direction, out Breakable target, out Vector3 point, out float distance)
        {
            target = null;
            point = Vector3.zero;
            distance = float.PositiveInfinity;
            int count = Physics.SphereCastNonAlloc(position, headRadius, direction, sweepHits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweepHits[i];
                if (hit.distance >= distance || Breakable.TryGetTarget(hit.collider, out Breakable owner, out _) == false || owner.IsDestroyed) continue;

                target = owner;
                distance = hit.distance;
                // 처음부터 겹치면 point가 원점이므로 머리 기준 최근접점 사용
                point = hit.distance > 0f ? hit.point : ClosestSurfacePoint(hit.collider, position);
            }
            return target != null;
        }

        private void Strike(Breakable target, Vector3 point, Vector3 velocity)
        {
            struckThisSwing = true;
            if (target.TryHit(point, velocity) == false) return;

            impactAudio.PlayOneShot(target.HitCount > 1 ? breakSound : crackSound);
            if (holdingHand != null) holdingHand.SendHapticImpulse(hapticAmplitude, hapticDuration);
        }

        private void EndSwing()
        {
            swinging = false;
            struckThisSwing = false;
            lockedTarget = null;
        }

        // 오목한 MeshCollider는 ClosestPoint 미지원, 경계 상자로 대체
        private static Vector3 ClosestSurfacePoint(Collider collider, Vector3 position)
        {
            if (collider is MeshCollider meshCollider && meshCollider.convex == false) return collider.ClosestPointOnBounds(position);

            return collider.ClosestPoint(position);
        }
    }
}
