using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection.Editor
{
    internal enum SegmentType
    {
        OuterUnshared = 0,
        SharedBoundary = 1,
        Hole = 2
    }

    internal sealed class ExtractedShape
    {
        public int label;
        public int region;
        public Color32 color;
        public float avgDepth;
        public int pixelCount;

        // Simplified polygons; vertices are pixel-corner coordinates, loops are closed
        // (first vertex not repeated at the end).
        public List<List<Vector2>> loops = new();
        public List<bool> loopIsHole = new();
        public List<int> loopSourceCornerCounts = new();
        // Flattened simplified segments for wireframe rendering.
        public List<(Vector2 a, Vector2 b, SegmentType type)> segments = new();
    }

    internal static class CartoonShapePrototypeSettings
    {
        public static float SimplifyEpsilon = 2f;      // screen pixels (RDP)
        public static float MinLoopAreaPixels = 24f;   // screen pixels squared
    }

    // Connected-component extraction over (object + region + paint layer) identities,
    // pixel-grid contour tracing with outer loops and holes, and shared-boundary chains
    // so neighbouring shapes simplify to the exact same polyline (no cracks).
    internal static class CartoonShapeExtractor
    {
        private const int GridShift = 22; // up to 4M grid points per long key

        public static List<ExtractedShape> Extract(VisibilityBuffer buffer, int[] labelBuffer)
        {
            var shapes = LabelConnectedComponents(buffer, labelBuffer);
            var edges = BuildBoundaryEdges(buffer, labelBuffer);
            var sharedChainOfEdge = AssignSharedChains(edges);
            TraceLoops(buffer, labelBuffer, edges, sharedChainOfEdge, shapes);
            shapes.RemoveAll(shape => shape.loops.Count == 0);
            return shapes;
        }

        // 4-neighbour flood fill over the full identity (object, region, layer).
        // shapes[i] corresponds to label i and collects per-component statistics.
        internal static List<ExtractedShape> LabelConnectedComponents(VisibilityBuffer buffer, int[] labelBuffer)
        {
            int width = buffer.width;
            int height = buffer.height;
            var shapes = new List<ExtractedShape>();
            var stack = new Stack<int>();

            for (int i = 0; i < labelBuffer.Length; i++)
                labelBuffer[i] = -1;

            for (int start = 0; start < labelBuffer.Length; start++)
            {
                if (labelBuffer[start] >= 0 || buffer.IsBackground(start))
                    continue;
                int label = shapes.Count;
                int identity = buffer.regionIds[start];
                var shape = new ExtractedShape
                {
                    label = label,
                    region = identity,
                    color = buffer.regionColor[start],
                    avgDepth = 0f,
                    pixelCount = 0
                };
                shapes.Add(shape);
                stack.Push(start);
                labelBuffer[start] = label;
                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    int x = index % width;
                    int y = index / width;
                    shape.pixelCount++;
                    shape.avgDepth += buffer.depth[index];
                    TryVisit(index - 1, x > 0);
                    TryVisit(index + 1, x < width - 1);
                    TryVisit(index - width, y > 0);
                    TryVisit(index + width, y < height - 1);
                    continue;

                    void TryVisit(int neighbour, bool inside)
                    {
                        if (!inside || labelBuffer[neighbour] >= 0)
                            return;
                        if (buffer.regionIds[neighbour] != identity)
                            return;
                        labelBuffer[neighbour] = label;
                        stack.Push(neighbour);
                    }
                }
                shape.avgDepth = shape.pixelCount > 0 ? shape.avgDepth / shape.pixelCount : 0f;
            }
            return shapes;
        }

        private struct DirectedEdge
        {
            public int start;  // grid vertex: g = y * (w + 1) + x
            public int end;
            public int label;
        }

        private static long EdgeKey(DirectedEdge e) => (((long)e.start) << GridShift) | (uint)e.end;

        private static long UndirectedFromEdge(DirectedEdge e)
        {
            return e.start < e.end ? (e.start << GridShift) | e.end : (e.end << GridShift) | e.start;
        }

        private static int GridX(int grid, int width) => grid % (width + 1);
        private static int GridY(int grid, int width) => grid / (width + 1);

        // Directed boundary edges; walking the edge, the owning shape lies on the
        // walker's right-hand side (screen coordinates, y down). With this convention
        // outer loops accumulate a positive shoelace sum and holes a negative one.
        private static Dictionary<long, DirectedEdge> BuildBoundaryEdges(VisibilityBuffer buffer,
            int[] labelBuffer)
        {
            int width = buffer.width;
            int height = buffer.height;
            var edges = new Dictionary<long, DirectedEdge>();

            void Add(int x1, int y1, int x2, int y2, int label)
            {
                var edge = new DirectedEdge
                {
                    start = (int)(y1 * (width + 1L) + x1),
                    end = (int)(y2 * (width + 1L) + x2),
                    label = label
                };
                edges[EdgeKey(edge)] = edge;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    int label = labelBuffer[index];

                    // Vertical grid edge between pixel (x,y) and its right neighbour.
                    int rightLabel = x < width - 1 ? labelBuffer[index + 1] : -1;
                    if (label != rightLabel)
                    {
                        // Walked down (0,+1): right hand is west -> left pixel owns it.
                        // Walked up (0,-1): right hand is east -> right pixel owns it.
                        if (label >= 0)
                            Add(x + 1, y, x + 1, y + 1, label);
                        if (rightLabel >= 0)
                            Add(x + 1, y + 1, x + 1, y, rightLabel);
                    }

                    // Horizontal grid edge between pixel (x,y) and its lower neighbour.
                    int downLabel = y < height - 1 ? labelBuffer[index + width] : -1;
                    if (label != downLabel)
                    {
                        // Walked right (1,0): right hand is south -> LOWER pixel owns it.
                        // Walked left (-1,0): right hand is north -> UPPER pixel owns it.
                        if (label >= 0)
                            Add(x + 1, y + 1, x, y + 1, label);
                        if (downLabel >= 0)
                            Add(x, y + 1, x + 1, y + 1, downLabel);
                    }
                }
            }
            return edges;
        }

        // Groups shared grid edges into chains: shared edges that touch end-to-end and
        // separate the same label pair form one chain, which is simplified exactly once.
        private static Dictionary<long, int> AssignSharedChains(Dictionary<long, DirectedEdge> edges)
        {
            var byUndirected = new Dictionary<long, List<DirectedEdge>>();
            foreach (var pair in edges.Values)
            {
                long key = UndirectedFromEdge(pair);
                if (!byUndirected.TryGetValue(key, out var list))
                {
                    list = new List<DirectedEdge>();
                    byUndirected[key] = list;
                }
                list.Add(pair);
            }

            var indexOfKey = new Dictionary<long, int>();
            var parent = new List<int>();
            var sharedEntries = new List<(long key, int labelMin, int labelMax, int p, int q)>();

            int GetIndex(long key)
            {
                if (!indexOfKey.TryGetValue(key, out int index))
                {
                    index = parent.Count;
                    indexOfKey[key] = index;
                    parent.Add(index);
                }
                return index;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (var pair in byUndirected)
            {
                if (pair.Value.Count != 2 || pair.Value[0].label == pair.Value[1].label)
                    continue;
                int index = GetIndex(pair.Key);
                sharedEntries.Add((pair.Key,
                    Mathf.Min(pair.Value[0].label, pair.Value[1].label),
                    Mathf.Max(pair.Value[0].label, pair.Value[1].label),
                    pair.Value[0].start, pair.Value[0].end));
            }

            var byEndpoint = new Dictionary<int, List<int>>();
            for (int i = 0; i < sharedEntries.Count; i++)
            {
                Join(sharedEntries[i].p, i);
                Join(sharedEntries[i].q, i);
                continue;

                void Join(int point, int edgeIndex)
                {
                    if (!byEndpoint.TryGetValue(point, out var list))
                    {
                        list = new List<int>();
                        byEndpoint[point] = list;
                    }
                    foreach (int other in list)
                    {
                        if (sharedEntries[other].labelMin == sharedEntries[edgeIndex].labelMin &&
                            sharedEntries[other].labelMax == sharedEntries[edgeIndex].labelMax)
                            parent[Find(other)] = Find(edgeIndex);
                    }
                    list.Add(edgeIndex);
                }
            }

            var chainOfEdge = new Dictionary<long, int>();
            var chainIdOfRoot = new Dictionary<int, int>();
            for (int i = 0; i < sharedEntries.Count; i++)
            {
                int root = Find(i);
                if (!chainIdOfRoot.TryGetValue(root, out int chainId))
                {
                    chainId = chainIdOfRoot.Count;
                    chainIdOfRoot[root] = chainId;
                }
                chainOfEdge[sharedEntries[i].key] = chainId;
            }
            return chainOfEdge;
        }

        private static void TraceLoops(VisibilityBuffer buffer, int[] labelBuffer,
            Dictionary<long, DirectedEdge> edges, Dictionary<long, int> sharedChainOfEdge,
            List<ExtractedShape> shapes)
        {
            int width = buffer.width;
            var outgoing = new Dictionary<int, List<DirectedEdge>>();
            foreach (var pair in edges.Values)
            {
                if (!outgoing.TryGetValue(pair.start, out var list))
                {
                    list = new List<DirectedEdge>();
                    outgoing[pair.start] = list;
                }
                list.Add(pair);
            }

            var visited = new HashSet<long>();
            var chainRegistry = new Dictionary<int, List<Vector2>>();

            foreach (var pair in edges.Values)
            {
                if (visited.Contains(EdgeKey(pair)))
                    continue;
                WalkLoop(pair);
            }

            return;

            bool IsShared(DirectedEdge e, out int chainId) =>
                sharedChainOfEdge.TryGetValue(UndirectedFromEdge(e), out chainId);

            DirectedEdge PickNext(DirectedEdge incoming, DirectedEdge firstEdge)
            {
                int dirX = GridX(incoming.end, width) - GridX(incoming.start, width);
                int dirY = GridY(incoming.end, width) - GridY(incoming.start, width);
                int endX = GridX(incoming.end, width);
                int endY = GridY(incoming.end, width);
                // Clockwise walking with the shape on the right: prefer the right turn,
                // then straight, then left turn, then U-turn. Only unvisited edges
                // qualify, except closing back onto the loop's first edge.
                (int dx, int dy)[] preferences = { (-dirY, dirX), (dirX, dirY), (dirY, -dirX), (-dirX, -dirY) };
                if (!outgoing.TryGetValue(incoming.end, out var candidates))
                    return default;
                foreach (var (tx, ty) in preferences)
                {
                    foreach (var candidate in candidates)
                    {
                        int cdx = GridX(candidate.end, width) - GridX(candidate.start, width);
                        int cdy = GridY(candidate.end, width) - GridY(candidate.start, width);
                        if (cdx != tx || cdy != ty)
                            continue;
                        if (!visited.Contains(EdgeKey(candidate)) || EdgeKey(candidate) == EdgeKey(firstEdge))
                            return candidate;
                    }
                }
                return default;
            }

            void WalkLoop(DirectedEdge firstEdge)
            {
                var loopEdges = new List<DirectedEdge> { firstEdge };
                visited.Add(EdgeKey(firstEdge));
                var current = firstEdge;
                while (true)
                {
                    var next = PickNext(current, firstEdge);
                    if (next.start == 0 && next.end == 0)
                        return; // dead end: malformed boundary set, drop this loop
                    if (EdgeKey(next) == EdgeKey(firstEdge))
                        break; // closed
                    visited.Add(EdgeKey(next));
                    loopEdges.Add(next);
                    current = next;
                }

                // Rotate so the loop starts at a segment boundary (property change
                // between the last and first edge). No boundary -> single segment loop.
                int count = loopEdges.Count;
                int rotateTo = -1;
                for (int i = 0; i < count; i++)
                {
                    bool sA = IsShared(loopEdges[i], out int chainA);
                    bool sB = IsShared(loopEdges[(i - 1 + count) % count], out int chainB);
                    if (sA != sB || (sA && chainA != chainB))
                    {
                        rotateTo = i;
                        break;
                    }
                }

                List<Segment> segmentList;
                if (rotateTo < 0)
                {
                    IsShared(loopEdges[0], out int chainId);
                    var corners = new List<int> { loopEdges[0].start };
                    foreach (var edge in loopEdges)
                        corners.Add(edge.end);
                    segmentList = new List<Segment>
                    {
                        new Segment
                        {
                            shared = IsShared(loopEdges[0], out _),
                            chainId = chainId,
                            corners = corners,
                            type = IsShared(loopEdges[0], out _) ? SegmentType.SharedBoundary : SegmentType.OuterUnshared
                        }
                    };
                }
                else
                {
                    var rotated = new List<DirectedEdge>(count);
                    for (int i = 0; i < count; i++)
                        rotated.Add(loopEdges[(rotateTo + i) % count]);

                    segmentList = new List<Segment>();
                    int i2 = 0;
                    while (i2 < count)
                    {
                        bool shared = IsShared(rotated[i2], out int chainId);
                        var corners = new List<int> { rotated[i2].start, rotated[i2].end };
                        int j = i2 + 1;
                        while (j < count)
                        {
                            bool nextShared = IsShared(rotated[j], out int nextChain);
                            if (nextShared != shared || (shared && nextChain != chainId))
                                break;
                            corners.Add(rotated[j].end);
                            j++;
                        }
                        segmentList.Add(new Segment
                        {
                            shared = shared,
                            chainId = chainId,
                            corners = corners,
                            type = shared ? SegmentType.SharedBoundary : SegmentType.OuterUnshared
                        });
                        i2 = j;
                    }
                }

                if (System.Environment.GetEnvironmentVariable("CARTOON_DEBUG") == "1")
                    UnityEngine.Debug.Log($"[dbg] label={firstEdge.label} rotateTo={rotateTo} count={count} segments={segmentList.Count}: " +
                        string.Join(" | ", segmentList.ConvertAll(seg => $"{(seg.shared ? "S" + seg.chainId : "U")}({seg.corners.Count})")));
                // Simplify each segment; shared segments go through the chain registry so
                // both neighbours reuse the identical polyline. Edges are emitted per
                // segment (never from a global type array), so every edge carries the
                // type of the segment it fully belongs to; the closing edge is the last
                // segment's own last edge because the loop ends where it started.
                var ring = new List<Vector2>();
                var loopSegments = new List<(Vector2 a, Vector2 b, SegmentType type)>();
                bool isHole = false;
                foreach (var segment in segmentList)
                {
                    List<Vector2> polyline;
                    if (segment.shared)
                    {
                        if (!chainRegistry.TryGetValue(segment.chainId, out polyline))
                        {
                            polyline = CartoonContourSimplifier.SimplifyPolyline(
                                ToPoints(segment.corners, width), CartoonShapePrototypeSettings.SimplifyEpsilon);
                            chainRegistry[segment.chainId] = polyline;
                        }
                        else
                        {
                            polyline = ReverseIfFlipped(polyline, segment.corners[0], width);
                        }
                    }
                    else
                    {
                        polyline = CartoonContourSimplifier.SimplifyPolyline(
                            ToPoints(segment.corners, width), CartoonShapePrototypeSettings.SimplifyEpsilon);
                    }

                    var segmentType = segment.shared ? SegmentType.SharedBoundary : SegmentType.OuterUnshared;
                    for (int i = 0; i + 1 < polyline.Count; i++)
                        loopSegments.Add((polyline[i], polyline[i + 1], segmentType));

                    for (int i = ring.Count > 0 ? 1 : 0; i < polyline.Count; i++)
                        ring.Add(polyline[i]);
                }
                if (ring.Count > 1 && ring[^1] == ring[0])
                    ring.RemoveAt(ring.Count - 1);
                if (ring.Count < 3)
                    return;

                float area2 = Mathf.Abs(Shoelace(ring));
                if (area2 < CartoonShapePrototypeSettings.MinLoopAreaPixels * 2f)
                    return; // loop too small after simplification (outer or hole)

                isHole = Shoelace(ring) < 0;
                if (isHole)
                    for (int i = 0; i < loopSegments.Count; i++)
                        loopSegments[i] = (loopSegments[i].a, loopSegments[i].b, SegmentType.Hole);

                var shape = shapes[firstEdge.label];
                shape.loopSourceCornerCounts.Add(count);
                shape.loops.Add(ring);
                shape.loopIsHole.Add(isHole);
                shape.segments.AddRange(loopSegments);
            }
        }

        private static List<Vector2> ToPoints(List<int> corners, int width)
        {
            var points = new List<Vector2>(corners.Count);
            foreach (int corner in corners)
                points.Add(new Vector2(GridX(corner, width), GridY(corner, width)));
            return points;
        }

        private static List<Vector2> ReverseIfFlipped(List<Vector2> canonical, int firstCorner, int width)
        {
            bool sameStart = (int)canonical[0].x == GridX(firstCorner, width) &&
                             (int)canonical[0].y == GridY(firstCorner, width);
            if (sameStart)
                return canonical;
            var reversed = new List<Vector2>(canonical.Count);
            for (int i = canonical.Count - 1; i >= 0; i--)
                reversed.Add(canonical[i]);
            return reversed;
        }

        private static float Shoelace(List<Vector2> ring)
        {
            float sum = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum;
        }

        private struct Segment
        {
            public bool shared;
            public int chainId;
            public List<int> corners;
            public SegmentType type;
        }
    }
}
