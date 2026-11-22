using System;
using System.Collections.Generic;
using UnityEngine;

namespace BreakMyRoom
{
    // 스프레이·도장·그을음 자국을 생성 순서대로 쌓는 묶음 메시(Mark Layer) 풀
    // 묶음 안은 나중 사각형, 묶음끼리는 나중 묶음이 위 (최근 자국이 항상 위)
    // 재사용 없음, 물총·자국 지우기·새 판에서만 삭제, 조각 낙하 시 함께 이동
    public sealed class PaintMarkPool : MonoBehaviour
    {
        private sealed class Chunk
        {
            public GameObject Owner;
            public Mesh Mesh;
            public Vector3[] Vertices;
            public Vector2[] Uvs;
            public Color32[] Colors;
            public int UsedQuads;
            public bool Dirty;
        }

        public enum MarkStyle
        {
            Spray,
            Scorch
        }

        private const int MaximumOwners = 4;
        // 안내 UI·분사 입자보다 먼저 그리기
        private const int FirstSortingOrder = -1000;

        [Header("References")]
        [SerializeField] private Material markMaterial;

        [Header("Capacity")]
        [SerializeField, Min(16)] private int quadsPerChunk = 256;
        [SerializeField, Min(1)] private int maximumChunks = 32;

        [Header("Spray")]
        [SerializeField] private float spraySize = 0.09f;
        [SerializeField] private Color32 sprayColor = new Color32(255, 26, 92, 217);
        [SerializeField] private Rect sprayUv = new Rect(0f, 0f, 1f / 3f, 1f);

        [Header("Stamp")]
        [SerializeField] private float stampSize = 0.08f;
        [SerializeField, Range(2, 12)] private int stampGrid = 6;
        [SerializeField] private Color32 stampColor = new Color32(31, 43, 133, 255);
        [SerializeField] private Rect stampUv = new Rect(1f / 3f, 0f, 1f / 3f, 1f);

        [Header("Scorch")]
        [SerializeField] private float scorchSize = 0.16f;
        // 겹칠수록 진해지는 반투명
        [SerializeField] private Color32 scorchColor = new Color32(28, 18, 10, 110);
        [SerializeField] private Rect scorchUv = new Rect(2f / 3f, 0f, 1f / 3f, 1f);

        [Header("Placement")]
        [SerializeField] private float surfaceOffset = 0.002f;

        private Chunk[] chunks;
        private int currentChunk;

        // 메시 안 위치 순서와 같은 생성 순서의 자국 기록
        private int[] markChunk;
        private int[] markFirstQuad;
        private int[] markQuadCount;
        private Vector3[] markCenters;
        private Vector3[] markNormals;
        // 자국이 걸친 면(최대 4개), 온전한 표면이면 조각 null, 부서진 뒤에는 아래 조각
        private Breakable[] markOwners;
        private BreakablePiece[] markPieces;
        private bool[] markActive;
        private int markCount;

        private Vector3[] gridPoints;
        private bool[] gridValid;
        private readonly Breakable[] ownerBuffer = new Breakable[MaximumOwners];
        private readonly BreakablePiece[] pieceBuffer = new BreakablePiece[MaximumOwners];
        private readonly List<Vector3> carriedVertices = new List<Vector3>();
        private readonly List<Vector2> carriedUvs = new List<Vector2>();
        private readonly List<Color32> carriedColors = new List<Color32>();
        private readonly RaycastHit[] rayHits = new RaycastHit[8];

        public int ActiveMarkCount { get; private set; }
        public int QuadCapacity => chunks.Length * quadsPerChunk;

        public void Configure(Material material, Rect spray, Rect stamp, Rect scorch)
        {
            markMaterial = material;
            sprayUv = spray;
            stampUv = stamp;
            scorchUv = scorch;
        }

        private void Awake()
        {
            int[] indices = new int[quadsPerChunk * 6];
            for (int quad = 0; quad < quadsPerChunk; quad++)
            {
                int vertex = quad * 4;
                int index = quad * 6;
                indices[index] = vertex;
                indices[index + 1] = vertex + 1;
                indices[index + 2] = vertex + 2;
                indices[index + 3] = vertex;
                indices[index + 4] = vertex + 2;
                indices[index + 5] = vertex + 3;
            }

            chunks = new Chunk[maximumChunks];
            Bounds roomBounds = new Bounds(Vector3.zero, Vector3.one * 50f);
            for (int i = 0; i < chunks.Length; i++)
            {
                GameObject layer = new GameObject("Mark Layer " + i, typeof(MeshFilter), typeof(MeshRenderer));
                layer.transform.SetParent(transform, false);
                Chunk chunk = new Chunk
                {
                    Owner = layer,
                    Mesh = new Mesh { name = "Mark Layer " + i },
                    Vertices = new Vector3[quadsPerChunk * 4],
                    Uvs = new Vector2[quadsPerChunk * 4],
                    Colors = new Color32[quadsPerChunk * 4]
                };
                chunk.Mesh.MarkDynamic();
                chunk.Mesh.SetVertices(chunk.Vertices);
                chunk.Mesh.SetUVs(0, chunk.Uvs);
                chunk.Mesh.SetColors(chunk.Colors);
                chunk.Mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
                chunk.Mesh.bounds = roomBounds;
                layer.GetComponent<MeshFilter>().sharedMesh = chunk.Mesh;
                MeshRenderer layerRenderer = layer.GetComponent<MeshRenderer>();
                layerRenderer.sharedMaterial = markMaterial;
                layerRenderer.sortingOrder = FirstSortingOrder + i;
                layerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                layerRenderer.receiveShadows = false;
                layerRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                layerRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                layer.SetActive(false);
                chunks[i] = chunk;
            }

            int capacity = QuadCapacity;
            markChunk = new int[capacity];
            markFirstQuad = new int[capacity];
            markQuadCount = new int[capacity];
            markCenters = new Vector3[capacity];
            markNormals = new Vector3[capacity];
            markOwners = new Breakable[capacity * MaximumOwners];
            markPieces = new BreakablePiece[capacity * MaximumOwners];
            markActive = new bool[capacity];
            gridPoints = new Vector3[(stampGrid + 1) * (stampGrid + 1)];
            gridValid = new bool[gridPoints.Length];
        }

        private void OnEnable()
        {
            Breakable.SurfaceRemoved += MoveMarksToPieces;
            Breakable.PieceDetached += CarryMarksAway;
            Breakable.Restored += MoveMarksToSurface;
        }

        private void OnDisable()
        {
            Breakable.SurfaceRemoved -= MoveMarksToPieces;
            Breakable.PieceDetached -= CarryMarksAway;
            Breakable.Restored -= MoveMarksToSurface;
        }

        private void LateUpdate()
        {
            foreach (Chunk chunk in chunks)
            {
                if (chunk.Dirty == false) continue;

                chunk.Mesh.SetVertices(chunk.Vertices);
                chunk.Mesh.SetUVs(0, chunk.Uvs);
                chunk.Mesh.SetColors(chunk.Colors);
                chunk.Dirty = false;
            }
        }

        // 온전한 표면·붙어 있는 조각만 칠하기 가능
        public bool IsPaintable(Collider surface)
        {
            return Breakable.TryGetTarget(surface, out _, out _);
        }

        // 스프레이 자국·그을음 하나, 반복 무늬 방지용 임의 회전
        public bool AddMark(Vector3 point, Vector3 normal, Collider surface, MarkStyle style = MarkStyle.Spray)
        {
            if (Breakable.TryGetTarget(surface, out Breakable owner, out BreakablePiece piece) == false) return false;
            if (TryReserve(1, out int chunkIndex, out int firstQuad) == false) return false;

            Quaternion rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            Vector3 center = point + normal * surfaceOffset;
            bool isScorch = style == MarkStyle.Scorch;
            float size = isScorch ? scorchSize : spraySize;
            Rect uv = isScorch ? scorchUv : sprayUv;
            Color32 color = isScorch ? scorchColor : sprayColor;
            Vector3 right = rotation * Vector3.right * (size * 0.5f);
            Vector3 up = rotation * Vector3.up * (size * 0.5f);
            Chunk chunk = chunks[chunkIndex];
            int vertex = firstQuad * 4;
            SetVertex(chunk, vertex, center - right - up, new Vector2(uv.xMin, uv.yMin), color);
            SetVertex(chunk, vertex + 1, center - right + up, new Vector2(uv.xMin, uv.yMax), color);
            SetVertex(chunk, vertex + 2, center + right + up, new Vector2(uv.xMax, uv.yMax), color);
            SetVertex(chunk, vertex + 3, center + right - up, new Vector2(uv.xMax, uv.yMin), color);
            ownerBuffer[0] = owner;
            pieceBuffer[0] = piece;
            RecordMark(chunkIndex, firstQuad, 1, point, normal, 1);
            return true;
        }

        // 도장 밑면에서 direction 방향 격자 레이로 만든 자국
        // 모서리·구석은 면을 따라 꺾임, 중심에서 먼 면·칠할 수 없는 곳은 비움
        // ignoredBody: 도장 자신 (밑면이 Collider 경계에 있어 자기 자신 충돌 방지)
        public bool AddStamp(Vector3 origin, Vector3 direction, Vector3 upHint, float maximumDistance, Rigidbody ignoredBody)
        {
            if (CastSurface(origin, direction, maximumDistance, ignoredBody, out RaycastHit centerHit) == false ||
                Breakable.TryGetTarget(centerHit.collider, out Breakable centerOwner, out BreakablePiece centerPiece) == false) return false;

            Vector3 up = Vector3.ProjectOnPlane(upHint, direction);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.forward, direction);
            up.Normalize();
            // 도장을 든 쪽에서 볼 때 무늬가 뒤집히지 않는 오른쪽 축
            Vector3 right = Vector3.Cross(up, direction);

            int ownerCount = 1;
            ownerBuffer[0] = centerOwner;
            pieceBuffer[0] = centerPiece;
            int points = stampGrid + 1;
            float depthLimit = stampSize;
            for (int y = 0; y < points; y++)
            {
                for (int x = 0; x < points; x++)
                {
                    int index = y * points + x;
                    Vector3 offset = right * (((float)x / stampGrid - 0.5f) * stampSize) + up * (((float)y / stampGrid - 0.5f) * stampSize);
                    bool hasHit = CastSurface(origin + offset, direction, centerHit.distance + depthLimit, ignoredBody, out RaycastHit hit);
                    Breakable surfaceOwner = null;
                    BreakablePiece surfacePiece = null;
                    gridValid[index] = hasHit && Mathf.Abs(hit.distance - centerHit.distance) <= depthLimit && Breakable.TryGetTarget(hit.collider, out surfaceOwner, out surfacePiece);
                    if (gridValid[index] == false) continue;

                    gridPoints[index] = hit.point + hit.normal * surfaceOffset;
                    ownerCount = AddOwner(surfaceOwner, surfacePiece, ownerCount);
                }
            }

            int quadCount = stampGrid * stampGrid;
            if (TryReserve(quadCount, out int chunkIndex, out int firstQuad) == false) return false;

            Chunk chunk = chunks[chunkIndex];
            for (int y = 0; y < stampGrid; y++)
            {
                for (int x = 0; x < stampGrid; x++)
                {
                    int vertex = (firstQuad + y * stampGrid + x) * 4;
                    int bottomLeft = y * points + x;
                    int topLeft = bottomLeft + points;
                    if (gridValid[bottomLeft] == false || gridValid[bottomLeft + 1] == false || gridValid[topLeft] == false || gridValid[topLeft + 1] == false)
                    {
                        // 모서리 하나라도 비면 그 칸 생략
                        ClearQuad(chunk, vertex);
                        continue;
                    }

                    SetVertex(chunk, vertex, gridPoints[bottomLeft], StampUv(x, y), stampColor);
                    SetVertex(chunk, vertex + 1, gridPoints[topLeft], StampUv(x, y + 1), stampColor);
                    SetVertex(chunk, vertex + 2, gridPoints[topLeft + 1], StampUv(x + 1, y + 1), stampColor);
                    SetVertex(chunk, vertex + 3, gridPoints[bottomLeft + 1], StampUv(x + 1, y), stampColor);
                }
            }
            RecordMark(chunkIndex, firstQuad, quadCount, centerHit.point, centerHit.normal, ownerCount);
            return true;
        }

        private bool CastSurface(Vector3 origin, Vector3 direction, float distance, Rigidbody ignoredBody, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(origin, direction, rayHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (ignoredBody != null && rayHits[i].collider.attachedRigidbody == ignoredBody) continue;

                if (found == false || rayHits[i].distance < nearest.distance)
                {
                    nearest = rayHits[i];
                    found = true;
                }
            }
            return found;
        }

        private Vector2 StampUv(int x, int y)
        {
            return new Vector2(stampUv.xMin + stampUv.width * x / stampGrid, stampUv.yMin + stampUv.height * y / stampGrid);
        }

        private int AddOwner(Breakable owner, BreakablePiece piece, int ownerCount)
        {
            for (int i = 0; i < ownerCount; i++)
            {
                if (ownerBuffer[i] == owner && pieceBuffer[i] == piece) return ownerCount;
            }
            if (ownerCount == MaximumOwners) return ownerCount;

            ownerBuffer[ownerCount] = owner;
            pieceBuffer[ownerCount] = piece;
            return ownerCount + 1;
        }

        // 자리가 없으면 다음 묶음, 모두 차면 지운 자리 압축
        private bool TryReserve(int quadCount, out int chunkIndex, out int firstQuad)
        {
            chunkIndex = -1;
            firstQuad = -1;
            if (quadCount > quadsPerChunk) return false;

            if (HasRoom(quadCount) == false) Compact();
            if (HasRoom(quadCount) == false) return false;

            if (chunks[currentChunk].UsedQuads + quadCount > quadsPerChunk) currentChunk++;
            Chunk chunk = chunks[currentChunk];
            chunkIndex = currentChunk;
            firstQuad = chunk.UsedQuads;
            chunk.UsedQuads += quadCount;
            chunk.Dirty = true;
            if (chunk.Owner.activeSelf == false) chunk.Owner.SetActive(true);
            return true;
        }

        private bool HasRoom(int quadCount)
        {
            if (markCount >= markActive.Length) return false;

            return chunks[currentChunk].UsedQuads + quadCount <= quadsPerChunk || currentChunk + 1 < chunks.Length;
        }

        private void RecordMark(int chunkIndex, int firstQuad, int quadCount, Vector3 center, Vector3 normal, int ownerCount)
        {
            int mark = markCount++;
            markChunk[mark] = chunkIndex;
            markFirstQuad[mark] = firstQuad;
            markQuadCount[mark] = quadCount;
            markCenters[mark] = center;
            markNormals[mark] = normal;
            markActive[mark] = true;
            for (int i = 0; i < MaximumOwners; i++)
            {
                markOwners[mark * MaximumOwners + i] = i < ownerCount ? ownerBuffer[i] : null;
                markPieces[mark * MaximumOwners + i] = i < ownerCount ? pieceBuffer[i] : null;
            }
            ActiveMarkCount++;
        }

        // 남은 자국을 생성 순서대로 앞 묶음으로 압축 (앞으로만 이동하므로 덮어쓰기 전 읽기)
        private void Compact()
        {
            int destinationChunk = 0;
            int destinationQuad = 0;
            int destinationMark = 0;
            for (int mark = 0; mark < markCount; mark++)
            {
                if (markActive[mark] == false) continue;

                int quadCount = markQuadCount[mark];
                if (destinationQuad + quadCount > quadsPerChunk)
                {
                    chunks[destinationChunk].UsedQuads = destinationQuad;
                    destinationChunk++;
                    destinationQuad = 0;
                }
                CopyQuads(markChunk[mark], markFirstQuad[mark], destinationChunk, destinationQuad, quadCount);
                markChunk[destinationMark] = destinationChunk;
                markFirstQuad[destinationMark] = destinationQuad;
                markQuadCount[destinationMark] = quadCount;
                markCenters[destinationMark] = markCenters[mark];
                markNormals[destinationMark] = markNormals[mark];
                markActive[destinationMark] = true;
                for (int i = 0; i < MaximumOwners; i++)
                {
                    markOwners[destinationMark * MaximumOwners + i] = markOwners[mark * MaximumOwners + i];
                    markPieces[destinationMark * MaximumOwners + i] = markPieces[mark * MaximumOwners + i];
                }
                destinationQuad += quadCount;
                destinationMark++;
            }
            chunks[destinationChunk].UsedQuads = destinationQuad;

            for (int i = 0; i < chunks.Length; i++)
            {
                Chunk chunk = chunks[i];
                if (i > destinationChunk) chunk.UsedQuads = 0;
                int firstUnused = chunk.UsedQuads * 4;
                Array.Clear(chunk.Vertices, firstUnused, chunk.Vertices.Length - firstUnused);
                chunk.Dirty = true;
                if (chunk.UsedQuads == 0 && i > 0) chunk.Owner.SetActive(false);
            }
            for (int mark = destinationMark; mark < markCount; mark++)
            {
                markActive[mark] = false;
                ClearOwners(mark);
            }
            markCount = destinationMark;
            currentChunk = destinationChunk;
        }

        private void CopyQuads(int sourceChunk, int sourceQuad, int destinationChunk, int destinationQuad, int quadCount)
        {
            if (sourceChunk == destinationChunk && sourceQuad == destinationQuad) return;

            Chunk source = chunks[sourceChunk];
            Chunk destination = chunks[destinationChunk];
            int count = quadCount * 4;
            Array.Copy(source.Vertices, sourceQuad * 4, destination.Vertices, destinationQuad * 4, count);
            Array.Copy(source.Uvs, sourceQuad * 4, destination.Uvs, destinationQuad * 4, count);
            Array.Copy(source.Colors, sourceQuad * 4, destination.Colors, destinationQuad * 4, count);
            destination.Dirty = true;
            if (destination.Owner.activeSelf == false) destination.Owner.SetActive(true);
        }

        private static void SetVertex(Chunk chunk, int vertex, Vector3 position, Vector2 uv, Color32 color)
        {
            chunk.Vertices[vertex] = position;
            chunk.Uvs[vertex] = uv;
            chunk.Colors[vertex] = color;
        }

        // 네 꼭짓점을 한 점에 모은 면적 0 사각형
        private static void ClearQuad(Chunk chunk, int vertex)
        {
            for (int i = 0; i < 4; i++)
                chunk.Vertices[vertex + i] = Vector3.zero;
        }

        private void RemoveMark(int mark)
        {
            Chunk chunk = chunks[markChunk[mark]];
            int vertex = markFirstQuad[mark] * 4;
            for (int i = 0; i < markQuadCount[mark]; i++)
                ClearQuad(chunk, vertex + i * 4);
            chunk.Dirty = true;
            markActive[mark] = false;
            ClearOwners(mark);
            ActiveMarkCount--;
        }

        private void ClearOwners(int mark)
        {
            for (int i = 0; i < MaximumOwners; i++)
            {
                markOwners[mark * MaximumOwners + i] = null;
                markPieces[mark * MaximumOwners + i] = null;
            }
        }

        // 물줄기 반경 안에 중심이 있는 자국 삭제
        // 꺾인 도장도 지우도록 면 방향 대신 물총 방향·가림 여부로 판단
        public int EraseMarks(Vector3 point, float radius, Vector3 nozzlePosition)
        {
            int erased = 0;
            float radiusSquared = radius * radius;
            for (int mark = 0; mark < markCount; mark++)
            {
                if (markActive[mark] == false || (markCenters[mark] - point).sqrMagnitude > radiusSquared) continue;

                Vector3 direction = markCenters[mark] - nozzlePosition;
                float distance = direction.magnitude;
                // 얇은 벽·가구의 반대 면 (물총을 등진 자국)
                if (distance <= 0.0001f || Vector3.Dot(markNormals[mark], direction) >= 0f) continue;

                // 가구·벽 모서리에 가려진 자국은 유지
                if (CastSurface(nozzlePosition, direction / distance, distance + 0.02f, null, out RaycastHit hit) == false ||
                    hit.distance < distance - 0.02f) continue;

                RemoveMark(mark);
                erased++;
            }
            return erased;
        }

        // 표면이 조각으로 바뀔 때 자국을 중심 아래 조각에 다시 붙이기
        private void MoveMarksToPieces(Breakable owner)
        {
            for (int mark = 0; mark < markCount; mark++)
            {
                if (markActive[mark] == false) continue;

                for (int i = 0; i < MaximumOwners; i++)
                {
                    int slot = mark * MaximumOwners + i;
                    if (markOwners[slot] != owner || markPieces[slot] != null) continue;

                    markPieces[slot] = FindPieceUnder(owner, markCenters[mark], markNormals[mark]);
                }
            }
        }

        private static BreakablePiece FindPieceUnder(Breakable owner, Vector3 center, Vector3 normal)
        {
            if (Physics.Raycast(center + normal * 0.03f, -normal, out RaycastHit hit, 0.08f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                Breakable.TryGetTarget(hit.collider, out Breakable hitOwner, out BreakablePiece hitPiece) && hitOwner == owner && hitPiece != null) return hitPiece;

            // 조각 사이 틈이면 중심이 가장 가까운 조각
            BreakablePiece nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (BreakablePiece piece in owner.Pieces)
            {
                float distance = (piece.WorldCenter - center).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = piece;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        // 떨어지는 조각의 자국을 조각으로 옮겨 함께 이동
        private void CarryMarksAway(BreakablePiece piece)
        {
            carriedVertices.Clear();
            carriedUvs.Clear();
            carriedColors.Clear();
            for (int mark = 0; mark < markCount; mark++)
            {
                if (markActive[mark] == false || IsAnchoredTo(mark, piece) == false) continue;

                Chunk chunk = chunks[markChunk[mark]];
                int firstVertex = markFirstQuad[mark] * 4;
                for (int vertex = firstVertex; vertex < firstVertex + markQuadCount[mark] * 4; vertex += 4)
                {
                    // 빈 칸 제외
                    if (chunk.Vertices[vertex] == Vector3.zero && chunk.Vertices[vertex + 2] == Vector3.zero) continue;

                    for (int corner = 0; corner < 4; corner++)
                    {
                        carriedVertices.Add(chunk.Vertices[vertex + corner]);
                        carriedUvs.Add(chunk.Uvs[vertex + corner]);
                        carriedColors.Add(chunk.Colors[vertex + corner]);
                    }
                }
                RemoveMark(mark);
            }
            if (carriedVertices.Count > 0) piece.CarryMarks(carriedVertices, carriedUvs, carriedColors, markMaterial);
        }

        private bool IsAnchoredTo(int mark, BreakablePiece piece)
        {
            for (int i = 0; i < MaximumOwners; i++)
            {
                if (markPieces[mark * MaximumOwners + i] == piece) return true;
            }
            return false;
        }

        // 방 복구 시 조각의 자국을 표면으로 복귀
        private void MoveMarksToSurface(Breakable owner)
        {
            for (int mark = 0; mark < markCount; mark++)
            {
                if (markActive[mark] == false) continue;

                for (int i = 0; i < MaximumOwners; i++)
                {
                    int slot = mark * MaximumOwners + i;
                    if (markOwners[slot] == owner) markPieces[slot] = null;
                }
            }
        }

        public void ClearMarks()
        {
            foreach (Chunk chunk in chunks)
            {
                Array.Clear(chunk.Vertices, 0, chunk.Vertices.Length);
                chunk.UsedQuads = 0;
                chunk.Dirty = true;
                chunk.Owner.SetActive(false);
            }
            Array.Clear(markActive, 0, markActive.Length);
            Array.Clear(markOwners, 0, markOwners.Length);
            Array.Clear(markPieces, 0, markPieces.Length);
            markCount = 0;
            currentChunk = 0;
            ActiveMarkCount = 0;
            // 떨어진 조각의 자국도 함께 삭제
            IReadOnlyList<Breakable> breakables = Breakable.All;
            for (int i = 0; i < breakables.Count; i++)
            {
                IReadOnlyList<BreakablePiece> pieces = breakables[i].Pieces;
                for (int j = 0; j < pieces.Count; j++)
                    pieces[j].ClearCarriedMarks();
            }
        }
    }
}
