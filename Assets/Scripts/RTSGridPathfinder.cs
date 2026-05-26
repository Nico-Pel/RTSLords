using System.Collections.Generic;
using UnityEngine;

public class RTSGridPathfinder : MonoBehaviour
{
    private const float DefaultHeight = 2f;

    private static RTSGridPathfinder _instance;

    [Header("Grid")]
    public float cellSize = 0.9f;
    public float pathPadding = 6f;
    public float maxPathExtent = 20f;
    public int maxExpandedNodes = 900;

    [Header("Obstacles")]
    public LayerMask obstacleMask = ~0;
    public float obstacleCheckHeight = 1.25f;
    public float obstacleInset = 0.08f;

    public static RTSGridPathfinder Instance
    {
        get
        {
            if (_instance == null)
            {
                RTSGridPathfinder existing = FindObjectOfType<RTSGridPathfinder>();
                if (existing != null)
                {
                    _instance = existing;
                }
                else
                {
                    GameObject gameObject = new GameObject("RTSGridPathfinder");
                    _instance = gameObject.AddComponent<RTSGridPathfinder>();
                }
            }

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public bool TryFindPath(Vector3 startWorld, Vector3 endWorld, Collider selfCollider, out List<Vector3> waypoints)
    {
        if (TryFindPathInternal(startWorld, endWorld, selfCollider, pathPadding, maxExpandedNodes, out waypoints))
        {
            return true;
        }

        if (TryFindPathInternal(startWorld, endWorld, selfCollider, pathPadding * 1.75f, maxExpandedNodes * 2, out waypoints))
        {
            return true;
        }

        return TryFindPathInternal(startWorld, endWorld, selfCollider, pathPadding * 2.5f, maxExpandedNodes * 3, out waypoints);
    }

    private bool TryFindPathInternal(Vector3 startWorld, Vector3 endWorld, Collider selfCollider, float padding, int expandedNodeBudget, out List<Vector3> waypoints)
    {
        Vector3 flatStart = new Vector3(startWorld.x, 0f, startWorld.z);
        Vector3 flatEnd = new Vector3(endWorld.x, 0f, endWorld.z);

        Vector3 min = Vector3.Min(flatStart, flatEnd) - Vector3.one * Mathf.Min(maxPathExtent, padding);
        Vector3 max = Vector3.Max(flatStart, flatEnd) + Vector3.one * Mathf.Min(maxPathExtent, padding);

        float width = Mathf.Clamp(max.x - min.x, cellSize * 6f, maxPathExtent * 2f);
        float depth = Mathf.Clamp(max.z - min.z, cellSize * 6f, maxPathExtent * 2f);
        int columns = Mathf.Max(6, Mathf.CeilToInt(width / cellSize));
        int rows = Mathf.Max(6, Mathf.CeilToInt(depth / cellSize));

        GridNode[,] nodes = new GridNode[columns, rows];
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Vector3 world = GridToWorld(min, x, y);
                nodes[x, y] = new GridNode(x, y, world, IsWalkable(world, selfCollider, endWorld));
            }
        }

        GridNode startNode = nodes[ClampIndex((flatStart.x - min.x) / cellSize, columns), ClampIndex((flatStart.z - min.z) / cellSize, rows)];
        GridNode endNode = nodes[ClampIndex((flatEnd.x - min.x) / cellSize, columns), ClampIndex((flatEnd.z - min.z) / cellSize, rows)];

        startNode.walkable = true;
        endNode.walkable = true;

        List<GridNode> openSet = new List<GridNode> { startNode };
        HashSet<GridNode> closedSet = new HashSet<GridNode>();
        startNode.gCost = 0;
        startNode.hCost = Heuristic(startNode, endNode);

        int expandedNodes = 0;
        while (openSet.Count > 0 && expandedNodes < Mathf.Max(maxExpandedNodes, expandedNodeBudget))
        {
            expandedNodes++;
            GridNode currentNode = GetBestNode(openSet);
            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            if (currentNode == endNode)
            {
                waypoints = SimplifyPath(RetracePath(startNode, endNode), endWorld);
                return true;
            }

            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    int neighbourX = currentNode.x + offsetX;
                    int neighbourY = currentNode.y + offsetY;
                    if (neighbourX < 0 || neighbourX >= columns || neighbourY < 0 || neighbourY >= rows)
                    {
                        continue;
                    }

                    GridNode neighbour = nodes[neighbourX, neighbourY];
                    if (!neighbour.walkable || closedSet.Contains(neighbour))
                    {
                        continue;
                    }

                    if (offsetX != 0 && offsetY != 0)
                    {
                        GridNode horizontal = nodes[currentNode.x + offsetX, currentNode.y];
                        GridNode vertical = nodes[currentNode.x, currentNode.y + offsetY];
                        if (!horizontal.walkable || !vertical.walkable)
                        {
                            continue;
                        }
                    }

                    int tentativeCost = currentNode.gCost + Heuristic(currentNode, neighbour);
                    if (tentativeCost >= neighbour.gCost && openSet.Contains(neighbour))
                    {
                        continue;
                    }

                    neighbour.gCost = tentativeCost;
                    neighbour.hCost = Heuristic(neighbour, endNode);
                    neighbour.parent = currentNode;
                    if (!openSet.Contains(neighbour))
                    {
                        openSet.Add(neighbour);
                    }
                }
            }
        }

        waypoints = new List<Vector3>();
        return false;
    }

    private bool IsWalkable(Vector3 worldPosition, Collider selfCollider, Vector3 finalDestination)
    {
        Vector3 center = new Vector3(worldPosition.x, obstacleCheckHeight * 0.5f, worldPosition.z);
        Vector3 halfExtents = new Vector3(
            Mathf.Max(0.05f, (cellSize * 0.5f) - obstacleInset),
            obstacleCheckHeight * 0.5f,
            Mathf.Max(0.05f, (cellSize * 0.5f) - obstacleInset));

        Collider[] hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null || hit == selfCollider)
            {
                continue;
            }

            if (selfCollider != null && hit.transform.IsChildOf(selfCollider.transform))
            {
                continue;
            }

            if (ShouldIgnoreCollider(hit))
            {
                continue;
            }

            Unit blockingUnit = hit.GetComponentInParent<Unit>();
            if (blockingUnit != null)
            {
                continue;
            }

            if (Vector3.Distance(new Vector3(worldPosition.x, 0f, worldPosition.z), new Vector3(finalDestination.x, 0f, finalDestination.z)) <= cellSize)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool ShouldIgnoreCollider(Collider collider)
    {
        if (collider == null)
        {
            return true;
        }

        if (collider.bounds.max.y <= 0.35f)
        {
            return true;
        }

        if (collider.GetComponentInParent<BuildZone>() != null ||
            collider.GetComponentInParent<HarvestField>() != null ||
            IsNamedBuildZoneHierarchy(collider.transform))
        {
            return true;
        }

        return collider.GetComponentInParent<Tree>() == null &&
               collider.GetComponentInParent<Build>() == null &&
               collider.GetComponentInParent<Hitbox>() == null &&
               collider.GetComponentInParent<Unit>() == null;
    }

    private bool IsNamedBuildZoneHierarchy(Transform current)
    {
        while (current != null)
        {
            if (current.name.StartsWith("BuildZone"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private GridNode GetBestNode(List<GridNode> openSet)
    {
        GridNode bestNode = openSet[0];
        for (int i = 1; i < openSet.Count; i++)
        {
            GridNode candidate = openSet[i];
            if (candidate.FCost < bestNode.FCost || (candidate.FCost == bestNode.FCost && candidate.hCost < bestNode.hCost))
            {
                bestNode = candidate;
            }
        }

        return bestNode;
    }

    private List<GridNode> RetracePath(GridNode startNode, GridNode endNode)
    {
        List<GridNode> path = new List<GridNode>();
        GridNode currentNode = endNode;
        while (currentNode != startNode)
        {
            path.Add(currentNode);
            currentNode = currentNode.parent;
            if (currentNode == null)
            {
                break;
            }
        }

        path.Reverse();
        return path;
    }

    private List<Vector3> SimplifyPath(List<GridNode> nodes, Vector3 endWorld)
    {
        List<Vector3> path = new List<Vector3>();
        if (nodes == null || nodes.Count == 0)
        {
            path.Add(endWorld);
            return path;
        }

        Vector2Int previousDirection = Vector2Int.zero;
        for (int i = 0; i < nodes.Count; i++)
        {
            Vector2Int direction = i == 0
                ? Vector2Int.zero
                : new Vector2Int(nodes[i].x - nodes[i - 1].x, nodes[i].y - nodes[i - 1].y);

            if (i == nodes.Count - 1 || direction != previousDirection)
            {
                path.Add(nodes[i].worldPosition);
            }

            previousDirection = direction;
        }

        if (path.Count == 0 || Vector3.Distance(path[path.Count - 1], endWorld) > cellSize * 0.5f)
        {
            path.Add(endWorld);
        }

        return path;
    }

    private Vector3 GridToWorld(Vector3 min, int x, int y)
    {
        return new Vector3(
            min.x + (x * cellSize) + (cellSize * 0.5f),
            0f,
            min.z + (y * cellSize) + (cellSize * 0.5f));
    }

    private int Heuristic(GridNode a, GridNode b)
    {
        int deltaX = Mathf.Abs(a.x - b.x);
        int deltaY = Mathf.Abs(a.y - b.y);
        int diagonal = Mathf.Min(deltaX, deltaY);
        int straight = Mathf.Abs(deltaX - deltaY);
        return (diagonal * 14) + (straight * 10);
    }

    private int ClampIndex(float value, int max)
    {
        return Mathf.Clamp(Mathf.FloorToInt(value), 0, max - 1);
    }

    private class GridNode
    {
        public readonly int x;
        public readonly int y;
        public readonly Vector3 worldPosition;
        public bool walkable;
        public int gCost = int.MaxValue;
        public int hCost;
        public GridNode parent;

        public int FCost => gCost + hCost;

        public GridNode(int x, int y, Vector3 worldPosition, bool walkable)
        {
            this.x = x;
            this.y = y;
            this.worldPosition = worldPosition;
            this.walkable = walkable;
        }
    }
}
