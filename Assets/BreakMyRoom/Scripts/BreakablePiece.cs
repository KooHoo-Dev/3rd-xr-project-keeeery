using System.Collections.Generic;
using UnityEngine;

namespace BreakMyRoom
{
    [RequireComponent(typeof(Rigidbody), typeof(MeshRenderer))]
    public sealed class BreakablePiece : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Breakable owner;
        // 붙어 있는 동안의 칠하기·망치 판정용 실제 모양 Collider (벽 조각은 물리 Collider와 동일)
        [SerializeField] private Collider surfaceCollider;
        // 떨어진 뒤의 충돌용 Collider (속이 빈 가구 조각은 경계 상자)
        [SerializeField] private Collider physicsCollider;

        [Header("Break")]
        [SerializeField] private Vector3 crackOffset;
        [SerializeField] private float settleTimeout = 12f;
        [SerializeField] private float launchSpeed = 1.5f;

        private Rigidbody pieceBody;
        private MeshRenderer pieceRenderer;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 homeScale;
        private Vector3 localCenter;
        private float settleDeadline;
        private MeshRenderer markRenderer;
        private Mesh markMesh;

        public Breakable Owner => owner;
        public Collider SurfaceCollider => surfaceCollider;
        public Collider PhysicsCollider => physicsCollider;
        public bool IsDetached { get; private set; }
        public bool IsAttachedSurface => IsDetached == false && surfaceCollider.enabled;
        public Vector3 WorldCenter => transform.TransformPoint(localCenter);

        public void Configure(Breakable breakable, Vector3 openOffset, Collider surface, Collider physics)
        {
            owner = breakable;
            crackOffset = openOffset;
            surfaceCollider = surface;
            physicsCollider = physics;
        }

        private void Awake()
        {
            pieceBody = GetComponent<Rigidbody>();
            pieceRenderer = GetComponent<MeshRenderer>();
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            homeScale = transform.localScale;
            localCenter = GetComponent<MeshFilter>().sharedMesh.bounds.center;
        }

        public void ShowAttached()
        {
            transform.localPosition = homePosition + crackOffset;
            pieceRenderer.enabled = true;
            surfaceCollider.enabled = true;
        }

        // direction: 튀어 나갈 방향
        public void Detach(Vector3 hitPoint, Vector3 direction)
        {
            IsDetached = true;
            transform.SetParent(null, true);
            // 오목한 MeshCollider는 움직이는 Rigidbody 불가, 물리 Collider로 전환 후 이동
            if (surfaceCollider != physicsCollider) surfaceCollider.enabled = false;
            physicsCollider.enabled = true;
            pieceBody.isKinematic = false;
            pieceBody.useGravity = true;
            // 질량과 무관하게 비슷한 속도를 내는 질량 비례 충격량
            Vector3 launch = direction.normalized * launchSpeed + Vector3.up * (launchSpeed * 0.33f);
            pieceBody.AddForceAtPosition(launch * pieceBody.mass, hitPoint, ForceMode.Impulse);
            settleDeadline = Time.time + settleTimeout;
        }

        // 월드 좌표 자국 사각형을 조각 기준 메시로 옮겨 함께 이동
        public void CarryMarks(List<Vector3> worldVertices, List<Vector2> uvs, List<Color32> colors, Material material)
        {
            if (markRenderer == null)
            {
                GameObject marks = new GameObject("Carried Marks", typeof(MeshFilter), typeof(MeshRenderer));
                marks.transform.SetParent(transform, false);
                markMesh = new Mesh { name = name + " Marks" };
                markMesh.MarkDynamic();
                marks.GetComponent<MeshFilter>().sharedMesh = markMesh;
                markRenderer = marks.GetComponent<MeshRenderer>();
                markRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                markRenderer.receiveShadows = false;
                markRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                markRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            for (int i = 0; i < worldVertices.Count; i++)
                worldVertices[i] = markRenderer.transform.InverseTransformPoint(worldVertices[i]);
            List<int> indices = new List<int>(worldVertices.Count / 4 * 6);
            for (int vertex = 0; vertex < worldVertices.Count; vertex += 4)
            {
                indices.Add(vertex);
                indices.Add(vertex + 1);
                indices.Add(vertex + 2);
                indices.Add(vertex);
                indices.Add(vertex + 2);
                indices.Add(vertex + 3);
            }
            markMesh.Clear();
            markMesh.SetVertices(worldVertices);
            markMesh.SetUVs(0, uvs);
            markMesh.SetColors(colors);
            markMesh.SetTriangles(indices, 0);
            markMesh.RecalculateBounds();
            markRenderer.sharedMaterial = material;
            markRenderer.enabled = true;
        }

        private void FixedUpdate()
        {
            // 떨어진 조각은 바닥에 유지, 잠들면 물리 계산 제외 후 고정
            if (IsDetached && pieceBody.isKinematic == false && (pieceBody.IsSleeping() || Time.time >= settleDeadline))
                StopPhysics();
        }

        public void Restore()
        {
            StopPhysics();
            transform.SetParent(owner.transform, false);
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            transform.localScale = homeScale;
            IsDetached = false;
            pieceRenderer.enabled = false;
            surfaceCollider.enabled = false;
            physicsCollider.enabled = false;
            ClearCarriedMarks();
            gameObject.SetActive(true);
        }

        // 조각에 붙어 떨어진 자국 삭제
        public void ClearCarriedMarks()
        {
            if (markRenderer == null) return;

            markMesh.Clear();
            markRenderer.enabled = false;
        }

        private void StopPhysics()
        {
            if (pieceBody.isKinematic == false)
            {
                pieceBody.linearVelocity = Vector3.zero;
                pieceBody.angularVelocity = Vector3.zero;
            }
            pieceBody.isKinematic = true;
            pieceBody.useGravity = false;
        }
    }
}
