using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosExplorer
{
    public class PathNetworkComponent : GH_Component
    {
        public PathNetworkComponent()
          : base("Path Network", "Path",
              "Finds shortest path from start to end via interest points while avoiding obstacles",
              "Neos", "NeosExplorer")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Starting Positions", "SP", "Starting positions", GH_ParamAccess.item);
            pManager.AddPointParameter("Points of Interest", "POIs", "Points of Interest(optional)", GH_ParamAccess.list);
            pManager.AddPointParameter("Destination", "DP", "Destination position", GH_ParamAccess.item);
            pManager.AddCurveParameter("Obstacles", "O", "Closed obstacle polyline(Draw counterclockwise)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Navigation Offset", "NO", "Offset value for obstacle curves when finding navigation points (m)", GH_ParamAccess.item, 0.5);
            pManager.AddBooleanParameter("Use Index Order", "IO", "Visit order: false=auto by distance (default), true=by input index", GH_ParamAccess.item, false);

            pManager[1].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Network", "N", "Path network geometry", GH_ParamAccess.list);
            pManager.AddCurveParameter("ShortestPath", "SP", "Shortest path polyline", GH_ParamAccess.item);
            pManager.AddPointParameter("Vertices", "V", "Path vertices", GH_ParamAccess.list);
            pManager.AddNumberParameter("SegmentLengths", "SL", "Individual segment lengths", GH_ParamAccess.list);
            pManager.AddNumberParameter("TotalLength", "TL", "Total path length", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. Retrieve input data
            Point3d start = Point3d.Unset;
            List<Point3d> points = new List<Point3d>();
            Point3d end = Point3d.Unset;
            List<Curve> obstacles = new List<Curve>();
            double offset = 0.3;

            if (!DA.GetData(0, ref start)) return;
            DA.GetDataList(1, points);
            if (!DA.GetData(2, ref end)) return;
            DA.GetDataList(3, obstacles);
            DA.GetData(4, ref offset);
            bool useIndexOrder = false;
            DA.GetData(5, ref useIndexOrder);

            // 2. Project all points to XY plane
            start.Z = 0;
            end.Z = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point3d pt = points[i];
                pt.Z = 0;
                points[i] = pt;
            }

            // 3. Process obstacles
            List<Curve> offsetCurves = new List<Curve>();
            List<Line> obstacleSegments = new List<Line>();
            List<Point3d> obstacleVertices = new List<Point3d>();
            Point3dComparer comparer = new Point3dComparer();

            foreach (Curve obstacle in obstacles)
            {
                if (obstacle == null || !obstacle.IsClosed) continue;

                Curve offsetCrv = (offset != 0) ?
                    obstacle.Offset(Plane.WorldXY, offset, 0.1, CurveOffsetCornerStyle.Sharp)[0] :
                    obstacle.DuplicateCurve();

                offsetCurves.Add(offsetCrv);

                // Convert to polyline and extract segments
                if (offsetCrv.ToPolyline(0, 0, 1, 0, 0, 0.1, 0, 0, true) is PolylineCurve plCurve)
                {
                    Polyline pl;
                    if (plCurve.TryGetPolyline(out pl))
                    {
                        for (int i = 0; i < pl.SegmentCount; i++)
                        {
                            Line segment = pl.SegmentAt(i);
                            obstacleSegments.Add(segment);
                        }
                        obstacleVertices.AddRange(pl);
                    }
                }
            }

            // 3.1 Check if points are inside obstacles
            if (IsPointInAnyObstacle(start, offsetCurves) ||
                points.Any(pt => IsPointInAnyObstacle(pt, offsetCurves)) ||
                IsPointInAnyObstacle(end, offsetCurves))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "One or more points are inside obstacles");
                return;
            }

            // 4. Collect all network vertices
            List<Point3d> allVertices = new List<Point3d> { start };
            allVertices.AddRange(points);
            allVertices.Add(end);
            allVertices.AddRange(obstacleVertices);
            allVertices = allVertices.Distinct(comparer).ToList();

            // 5. Generate potential connections - all pairs of vertices
            List<Line> potentialConnections = new List<Line>();
            for (int i = 0; i < allVertices.Count; i++)
            {
                for (int j = i + 1; j < allVertices.Count; j++)
                {
                    Point3d p1 = allVertices[i];
                    Point3d p2 = allVertices[j];
                    if (!comparer.Equals(p1, p2))
                    {
                        potentialConnections.Add(new Line(p1, p2));
                    }
                }
            }

            // 6. Create boundary segment set for fast lookup
            var boundarySegmentSet = new HashSet<Tuple<Point3d, Point3d>>(new UndirectedLineComparer(comparer));
            foreach (Line segment in obstacleSegments)
            {
                var normalizedSegment = NormalizeLine(segment, comparer);
                boundarySegmentSet.Add(normalizedSegment);
            }

            // 7. Filter connections that are safe
            List<Line> validConnections = new List<Line>();
            foreach (Line connection in potentialConnections)
            {
                var normalizedConn = NormalizeLine(connection, comparer);

                // Always allow boundary segments
                if (boundarySegmentSet.Contains(normalizedConn))
                {
                    validConnections.Add(connection);
                }
                // For non-boundary connections, check for intersections
                else if (!LineIntersectsAnyOffsetCurve(connection, offsetCurves, comparer))
                {
                    // 修改点：检测多个样本点（0.25, 0.5, 0.75）
                    bool anyPointInside = false;

                    // 计算三个样本点
                    Point3d p1 = connection.PointAt(0.25);
                    Point3d p2 = connection.PointAt(0.5); // 中点
                    Point3d p3 = connection.PointAt(0.75);

                    // 检查任意点是否在障碍物内
                    if (IsPointInAnyObstacle(p1, offsetCurves) ||
                        IsPointInAnyObstacle(p2, offsetCurves) ||
                        IsPointInAnyObstacle(p3, offsetCurves))
                    {
                        anyPointInside = true;
                    }

                    // 只有所有样本点都在障碍外才添加连接
                    if (!anyPointInside)
                    {
                        validConnections.Add(connection);
                    }
                }
            }

            // 8. Create network geometry for output
            List<Curve> networkGeometry = new List<Curve>();

            // Add valid connections
            foreach (Line line in validConnections)
            {
                networkGeometry.Add(new LineCurve(line));
            }

            // Add offset curves (obstacle boundaries)
            networkGeometry.AddRange(offsetCurves);

            // 9. Build graph from valid connections
            Graph networkGraph = BuildGraph(allVertices, validConnections, comparer);

            // 10. Calculate shortest path through interest points using A* algorithm
            List<Point3d> fullPath = new List<Point3d>();
            double totalLength = 0;
            List<double> segmentLengths = new List<double>();
            bool pathFound = true;

            // 根据访问模式排序兴趣点
            List<Point3d> sortedPoints;
            if (useIndexOrder)
            {
                // 索引顺序模式：保持原始输入顺序
                sortedPoints = new List<Point3d>(points);
            }
            else
            {
                // 自动模式：按距起点的直线距离由近到远排序
                sortedPoints = points
                    .OrderBy(p => p.DistanceTo(start))
                    .ToList();
            }

            // Create list of waypoints: start -> sorted interest points -> end
            List<Point3d> waypoints = new List<Point3d> { start };
            waypoints.AddRange(sortedPoints);
            waypoints.Add(end);

            // Calculate path segments between waypoints
            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                Point3d from = waypoints[i];
                Point3d to = waypoints[i + 1];

                List<Point3d> segmentPath = networkGraph.AStarPath(from, to, comparer);

                if (segmentPath == null || segmentPath.Count < 2)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"No path found between point {i} and {i + 1} ({from} to {to})");
                    pathFound = false;
                    break;
                }

                // Remove duplicate point at connection
                if (i > 0 && fullPath.Count > 0 &&
                    comparer.Equals(fullPath.Last(), segmentPath.First()))
                {
                    segmentPath.RemoveAt(0);
                }

                fullPath.AddRange(segmentPath);

                // Calculate segment lengths
                for (int j = 0; j < segmentPath.Count - 1; j++)
                {
                    double segLength = segmentPath[j].DistanceTo(segmentPath[j + 1]);
                    segmentLengths.Add(segLength);
                    totalLength += segLength;
                }
            }

            // 11. Create output polyline (only if path was found)
            Curve shortestPath = null;
            if (pathFound && fullPath.Count >= 2)
            {
                if (fullPath.Count == 2)
                {
                    // For exactly two points, create a simple line
                    shortestPath = new LineCurve(fullPath[0], fullPath[1]);
                }
                else
                {
                    // For more than two points, create a polyline
                    Polyline pathPolyline = new Polyline(fullPath);
                    shortestPath = new PolylineCurve(pathPolyline);
                }
            }

            // 12. Set outputs
            DA.SetDataList(0, networkGeometry);
            DA.SetData(1, shortestPath);
            DA.SetDataList(2, fullPath);
            DA.SetDataList(3, segmentLengths);
            DA.SetData(4, totalLength);
        }

        private Tuple<Point3d, Point3d> NormalizeLine(Line line, Point3dComparer comparer)
        {
            // Ensure consistent order for undirected comparison
            return ComparePoints(line.From, line.To, comparer) < 0 ?
                Tuple.Create(line.From, line.To) :
                Tuple.Create(line.To, line.From);
        }

        private int ComparePoints(Point3d a, Point3d b, Point3dComparer comparer)
        {
            // Compare points using tolerance-based comparison
            if (comparer.Equals(a, b)) return 0;
            if (Math.Abs(a.X - b.X) > 0.05) return a.X.CompareTo(b.X);
            if (Math.Abs(a.Y - b.Y) > 0.05) return a.Y.CompareTo(b.Y);
            return a.Z.CompareTo(b.Z);
        }

        private bool IsPointInAnyObstacle(Point3d pt, List<Curve> obstacles)
        {
            foreach (Curve crv in obstacles)
            {
                if (crv == null) continue;

                PointContainment containment = crv.Contains(pt, Plane.WorldXY, 0.1);
                if (containment == PointContainment.Inside || containment == PointContainment.Coincident)
                    return true;
            }
            return false;
        }

        private bool LineIntersectsAnyOffsetCurve(Line line, List<Curve> curves, Point3dComparer comparer)
        {
            LineCurve testCurve = new LineCurve(line);

            foreach (Curve curve in curves)
            {
                if (curve == null) continue;

                CurveIntersections intersections = Intersection.CurveCurve(testCurve, curve, 0.1, 0.1);
                if (HasInternalIntersection(intersections, testCurve, curve, comparer))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasInternalIntersection(CurveIntersections intersections, Curve testCurve, Curve obstacleCurve, Point3dComparer comparer)
        {
            if (intersections == null || intersections.Count == 0)
                return false;

            for (int i = 0; i < intersections.Count; i++)
            {
                IntersectionEvent eventData = intersections[i];

                // Skip endpoint intersections
                if (IsEndpointIntersection(eventData, testCurve, obstacleCurve, comparer))
                    continue;

                // Any other intersection is considered invalid
                return true;
            }
            return false;
        }

        private bool IsEndpointIntersection(IntersectionEvent eventData, Curve testCurve, Curve obstacleCurve, Point3dComparer comparer)
        {
            // Get intersection point
            Point3d intersectionPt = eventData.PointA;

            // Check if intersection is at start or end of test curve
            Point3d testStart = testCurve.PointAtStart;
            Point3d testEnd = testCurve.PointAtEnd;

            bool isTestEndpoint = comparer.Equals(intersectionPt, testStart) ||
                                  comparer.Equals(intersectionPt, testEnd);

            // Check if intersection is at a vertex of obstacle curve
            bool isObstacleVertex = false;
            if (obstacleCurve is PolylineCurve plCurve)
            {
                Polyline pl;
                if (plCurve.TryGetPolyline(out pl))
                {
                    foreach (Point3d vertex in pl)
                    {
                        if (comparer.Equals(intersectionPt, vertex))
                        {
                            isObstacleVertex = true;
                            break;
                        }
                    }
                }
            }

            return isTestEndpoint && isObstacleVertex;
        }

        private Graph BuildGraph(List<Point3d> vertices, List<Line> connections, Point3dComparer comparer)
        {
            Graph graph = new Graph(comparer);

            // Add all vertices
            foreach (Point3d vertex in vertices)
            {
                graph.AddVertex(vertex);
            }

            // Add connections
            foreach (Line connection in connections)
            {
                double length = connection.Length;
                graph.AddEdge(connection.From, connection.To, length);
            }

            return graph;
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_PathNetwork;
        public override Guid ComponentGuid => new Guid("47B10E23-E509-4F08-9199-87FACDA0ECFB");
    }

    class UndirectedLineComparer : IEqualityComparer<Tuple<Point3d, Point3d>>
    {
        private Point3dComparer _pointComparer;

        public UndirectedLineComparer(Point3dComparer comparer)
        {
            _pointComparer = comparer;
        }

        public bool Equals(Tuple<Point3d, Point3d> x, Tuple<Point3d, Point3d> y)
        {
            return (_pointComparer.Equals(x.Item1, y.Item1) && _pointComparer.Equals(x.Item2, y.Item2)) ||
                   (_pointComparer.Equals(x.Item1, y.Item2) && _pointComparer.Equals(x.Item2, y.Item1));
        }

        public int GetHashCode(Tuple<Point3d, Point3d> obj)
        {
            int h1 = _pointComparer.GetHashCode(obj.Item1);
            int h2 = _pointComparer.GetHashCode(obj.Item2);
            return h1 ^ h2; // Order-insensitive hash
        }
    }

    class Graph
    {
        private Dictionary<Point3d, List<Edge>> adjacencyList;
        private Point3dComparer comparer;

        public Graph(Point3dComparer comp)
        {
            comparer = comp;
            adjacencyList = new Dictionary<Point3d, List<Edge>>(comparer);
        }

        public void AddVertex(Point3d vertex)
        {
            if (!adjacencyList.ContainsKey(vertex))
            {
                adjacencyList[vertex] = new List<Edge>();
            }
        }

        public void AddEdge(Point3d from, Point3d to, double weight)
        {
            if (!adjacencyList.ContainsKey(from)) AddVertex(from);
            if (!adjacencyList.ContainsKey(to)) AddVertex(to);

            // Add bidirectional edge
            adjacencyList[from].Add(new Edge(to, weight));
            adjacencyList[to].Add(new Edge(from, weight));
        }

        public List<Point3d> AStarPath(Point3d start, Point3d end, Point3dComparer comp)
        {
            if (!adjacencyList.ContainsKey(start) || !adjacencyList.ContainsKey(end))
                return null;

            var openSet = new PriorityQueue<Node>();
            var gScore = new Dictionary<Point3d, double>(comparer);
            var cameFrom = new Dictionary<Point3d, Point3d>(comparer);

            // Initialize gScore with infinity for all nodes
            foreach (var vertex in adjacencyList.Keys)
            {
                gScore[vertex] = double.MaxValue;
            }

            // Start node
            gScore[start] = 0;
            double hStart = Heuristic(start, end);
            openSet.Enqueue(new Node(start, 0, hStart));

            while (openSet.Count > 0)
            {
                Node current = openSet.Dequeue();

                if (comp.Equals(current.Point, end))
                {
                    return ReconstructPath(cameFrom, current.Point, comp);
                }

                foreach (Edge neighborEdge in adjacencyList[current.Point])
                {
                    Point3d neighbor = neighborEdge.Destination;
                    double tentativeGScore = gScore[current.Point] + neighborEdge.Weight;

                    // FIX: Use TryGetValue for compatibility
                    double currentGScore;
                    if (!gScore.TryGetValue(neighbor, out currentGScore))
                    {
                        currentGScore = double.MaxValue;
                    }

                    if (tentativeGScore < currentGScore)
                    {
                        cameFrom[neighbor] = current.Point;
                        gScore[neighbor] = tentativeGScore;

                        double fScore = tentativeGScore + Heuristic(neighbor, end);
                        openSet.Enqueue(new Node(neighbor, tentativeGScore, fScore));
                    }
                }
            }
            return null;
        }

        private double Heuristic(Point3d a, Point3d b)
        {
            // Simple Euclidean distance heuristic
            return a.DistanceTo(b);
        }

        private List<Point3d> ReconstructPath(Dictionary<Point3d, Point3d> cameFrom, Point3d current, Point3dComparer comp)
        {
            List<Point3d> path = new List<Point3d> { current };

            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                path.Add(current);
            }

            path.Reverse();
            return path;
        }
    }

    class Edge
    {
        public Point3d Destination { get; }
        public double Weight { get; }

        public Edge(Point3d destination, double weight)
        {
            Destination = destination;
            Weight = weight;
        }
    }

    // Node for A* algorithm
    class Node : IComparable<Node>
    {
        public Point3d Point { get; }
        public double GScore { get; }
        public double FScore { get; }

        public Node(Point3d point, double gScore, double fScore)
        {
            Point = point;
            GScore = gScore;
            FScore = fScore;
        }

        public int CompareTo(Node other)
        {
            return FScore.CompareTo(other.FScore);
        }
    }

    // Priority queue implementation for A*
    class PriorityQueue<T> where T : IComparable<T>
    {
        private List<T> data;

        public PriorityQueue()
        {
            data = new List<T>();
        }

        public int Count => data.Count;

        public void Enqueue(T item)
        {
            data.Add(item);
            int ci = data.Count - 1;
            while (ci > 0)
            {
                int pi = (ci - 1) / 2;
                if (data[ci].CompareTo(data[pi]) >= 0) break;
                T tmp = data[ci]; data[ci] = data[pi]; data[pi] = tmp;
                ci = pi;
            }
        }

        public T Dequeue()
        {
            if (data.Count == 0) throw new InvalidOperationException("Queue is empty");
            int li = data.Count - 1;
            T frontItem = data[0];
            data[0] = data[li];
            data.RemoveAt(li);

            --li;
            int pi = 0;
            while (true)
            {
                int ci = pi * 2 + 1;
                if (ci > li) break;
                int rc = ci + 1;
                if (rc <= li && data[rc].CompareTo(data[ci]) < 0)
                    ci = rc;
                if (data[pi].CompareTo(data[ci]) <= 0) break;
                T tmp = data[pi]; data[pi] = data[ci]; data[ci] = tmp;
                pi = ci;
            }
            return frontItem;
        }
        
    }
    class Point3dComparer : IEqualityComparer<Point3d>
    {
        public bool Equals(Point3d p1, Point3d p2)
        {
            return p1.DistanceTo(p2) < 0.1;
        }

        public int GetHashCode(Point3d p)
        {
            return p.X.GetHashCode() ^ p.Y.GetHashCode() ^ p.Z.GetHashCode();
        }
    }
}