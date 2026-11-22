using System;
using System.Collections.Generic;
using UnityEngine;

namespace BreakMyRoom
{
    // 망치로 부술 수 있는 벽 칸·가구
    // 첫 타격에 벽은 균열선, 가구는 조각 틈, 이후 맞은 곳에 가까운 조각부터 낙하
    // 온전한 표면·붙어 있는 조각은 모두 칠하기 가능한 면
    public sealed class Breakable : MonoBehaviour
    {
        public enum BreakableKind
        {
            // 벽·바닥·천장 칸
            Wall,
            Furniture
        }

        private static readonly Dictionary<Collider, Breakable> SurfaceLookup = new();
        private static readonly Dictionary<Collider, BreakablePiece> PieceLookup = new();
        private static readonly List<Breakable> ActiveBreakables = new();

        // 표면 → 조각 전환 (자국을 아래 조각에 다시 붙이기)
        public static event Action<Breakable> SurfaceRemoved;
        // 조각 낙하 (자국도 함께 이동)
        public static event Action<BreakablePiece> PieceDetached;
        // 온전한 표면 복구 (조각의 자국을 표면으로 복귀)
        public static event Action<Breakable> Restored;

        [Header("Kind")]
        [SerializeField] private BreakableKind kind;

        [Header("References")]
        [SerializeField] private Renderer[] intactRenderers;
        [SerializeField] private Collider intactCollider;
        [SerializeField] private GameObject cracks;
        [SerializeField] private BreakablePiece[] pieces;

        [Header("Destruction")]
        [SerializeField, Min(1)] private int piecesPerHit = 2;

        private Bounds intactBounds;
        private bool hasSurface = true;

        public static IReadOnlyList<Breakable> All => ActiveBreakables;
        public BreakableKind Kind => kind;
        public int HitCount { get; private set; }
        public int RemainingPieces { get; private set; }
        public bool IsDestroyed => RemainingPieces == 0;
        public bool HasSurface => hasSurface;
        public Bounds Bounds => intactBounds;
        public IReadOnlyList<BreakablePiece> Pieces => pieces;

        public void Configure(BreakableKind breakableKind, Renderer[] renderers, Collider surface, GameObject crackVisual, BreakablePiece[] fragments, int fragmentsPerHit)
        {
            kind = breakableKind;
            intactRenderers = renderers;
            intactCollider = surface;
            cracks = crackVisual;
            pieces = fragments;
            piecesPerHit = fragmentsPerHit;
        }

        // 칸 장식(걸레받이·형광등) 표시 전환
        public void AddIntactRenderer(Renderer decoration)
        {
            Renderer[] renderers = new Renderer[intactRenderers.Length + 1];
            intactRenderers.CopyTo(renderers, 0);
            renderers[intactRenderers.Length] = decoration;
            intactRenderers = renderers;
        }

        // 온전한 표면만 true
        public static bool TryGetSurface(Collider collider, out Breakable breakable)
        {
            return SurfaceLookup.TryGetValue(collider, out breakable) && breakable.hasSurface;
        }

        // 온전한 표면이면 piece null, 붙어 있는 조각이면 그 조각, 떨어진 조각은 false
        public static bool TryGetTarget(Collider collider, out Breakable owner, out BreakablePiece piece)
        {
            piece = null;

            if (TryGetSurface(collider, out owner)) return true;

            if (PieceLookup.TryGetValue(collider, out piece) && piece.IsAttachedSurface)
            {
                owner = piece.Owner;
                return true;
            }

            piece = null;
            owner = null;
            return false;
        }

        private void Awake()
        {
            RemainingPieces = pieces.Length;
            intactBounds = intactCollider.bounds;
        }

        private void OnEnable()
        {
            SurfaceLookup[intactCollider] = this;

            foreach (BreakablePiece piece in pieces)
                PieceLookup[piece.SurfaceCollider] = piece;

            ActiveBreakables.Add(this);
        }

        private void OnDisable()
        {
            SurfaceLookup.Remove(intactCollider);

            foreach (BreakablePiece piece in pieces)
                PieceLookup.Remove(piece.SurfaceCollider);

            ActiveBreakables.Remove(this);
        }

        public bool TryHit(Vector3 hitPoint, Vector3 velocity)
        {
            if (IsDestroyed) return false;

            HitCount++;

            if (HitCount == 1)
            {
                if (cracks != null) cracks.SetActive(true);
                return true;
            }

            if (hasSurface) RemoveSurface();
            // 균열선 없는 가구는 첫 타격에 조각 틈만 표시
            if (HitCount == 1) return true;

            for (int count = 0; count < piecesPerHit && RemainingPieces > 0; count++)
            {
                BreakablePiece nearest = null;
                float nearestDistance = float.PositiveInfinity;

                foreach (BreakablePiece piece in pieces)
                {
                    if (piece.IsDetached) continue;

                    float distance = (piece.WorldCenter - hitPoint).sqrMagnitude;

                    if (distance < nearestDistance)
                    {
                        nearest = piece;
                        nearestDistance = distance;
                    }
                }

                // 낙하 전 알림 (자국을 조각의 현재 위치 기준으로 이동)
                PieceDetached?.Invoke(nearest);
                nearest.Detach(hitPoint, LaunchDirection(nearest, velocity));
                IgnoreSiblings(nearest);
                RemainingPieces--;
            }
            return true;
        }

        public bool IsNear(Vector3 position, float distance)
        {
            return intactBounds.SqrDistance(position) <= distance * distance;
        }

        public void HideDetachedPieces()
        {
            foreach (BreakablePiece piece in pieces)
                if (piece.IsDetached) piece.gameObject.SetActive(false);
        }

        public void ResetBreakable()
        {
            foreach (BreakablePiece piece in pieces)
                piece.Restore();

            HitCount = 0;
            RemainingPieces = pieces.Length;
            SetIntactVisible(true);

            if (cracks != null) cracks.SetActive(false);
            Restored?.Invoke(this);
        }

        private void RemoveSurface()
        {
            SetIntactVisible(false);
            if (cracks != null) cracks.SetActive(false);

            foreach (BreakablePiece piece in pieces)
                piece.ShowAttached();

            SurfaceRemoved?.Invoke(this);
        }

        // 벽 칸은 방 안쪽(-Z), 가구는 바깥쪽 + 망치 방향
        // 망치 방향만 쓰면 벽 조각이 벽 뒤 틈으로 빠짐
        private Vector3 LaunchDirection(BreakablePiece piece, Vector3 velocity)
        {
            Vector3 outward = kind == BreakableKind.Wall ? -transform.forward : (piece.WorldCenter - intactBounds.center).normalized;
            return (outward + velocity.normalized * 0.5f).normalized;
        }

        // 겹친 볼록 Collider에 끼지 않도록 같은 대상의 조각끼리 충돌 무시
        // 두 Collider가 모두 켜져 있어야 지정 가능, 떨어진 직후 켜진 것끼리만
        private void IgnoreSiblings(BreakablePiece detached)
        {
            foreach (BreakablePiece other in pieces)
            {
                if (other == detached) continue;

                if (other.SurfaceCollider.enabled) Physics.IgnoreCollision(detached.PhysicsCollider, other.SurfaceCollider);
                if (other.IsDetached && other.PhysicsCollider.enabled) Physics.IgnoreCollision(detached.PhysicsCollider, other.PhysicsCollider);
            }
        }

        private void SetIntactVisible(bool visible)
        {
            hasSurface = visible;
            intactCollider.enabled = visible;
            foreach (Renderer intactRenderer in intactRenderers)
                intactRenderer.enabled = visible;
        }
    }
}
