using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public static class Pathfinding
    {
        public static PathNode GetClosestNode(Vector3 position, List<PathNode> allNodes)
        {
            PathNode closest = null;
            float minDst = float.MaxValue;

            foreach (var node in allNodes)
            {
                float dst = Vector3.Distance(position, node.transform.position);
                if (dst < minDst)
                {
                    minDst = dst;
                    closest = node;
                }
            }
            return closest;
        }

        // A* Pathfinding implementation with Node-based costs
        public static List<PathNode> FindPath(PathNode startNode, PathNode targetNode)
        {
            List<PathNode> path = new List<PathNode>();
            if (startNode == null || targetNode == null) return path;

            if (startNode == targetNode)
            {
                path.Add(startNode);
                return path;
            }

            List<PathNode> openSet = new List<PathNode>();
            HashSet<PathNode> closedSet = new HashSet<PathNode>();

            Dictionary<PathNode, PathNode> cameFrom = new Dictionary<PathNode, PathNode>();
            Dictionary<PathNode, float> gScore = new Dictionary<PathNode, float>();
            Dictionary<PathNode, float> fScore = new Dictionary<PathNode, float>();

            openSet.Add(startNode);
            gScore[startNode] = 0f;
            fScore[startNode] = Vector2.Distance(startNode.transform.position, targetNode.transform.position);

            while (openSet.Count > 0)
            {
                PathNode current = openSet[0];
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (fScore.ContainsKey(openSet[i]) && fScore[openSet[i]] < fScore[current])
                    {
                        current = openSet[i];
                    }
                }

                if (current == targetNode)
                {
                    PathNode curr = targetNode;
                    while (curr != null)
                    {
                        path.Add(curr);
                        curr = cameFrom.ContainsKey(curr) ? cameFrom[curr] : null;
                    }
                    path.Reverse();
                    return path;
                }

                openSet.Remove(current);
                closedSet.Add(current);

                foreach (NodeConnection connection in current.connections)
                {
                    PathNode neighbor = connection != null ? connection.connectedNode : null;
                    if (neighbor == null || closedSet.Contains(neighbor)) continue;

                    // Calculate distance cost + the neighbor node's custom cost
                    float distanceCost = Vector2.Distance(current.transform.position, neighbor.transform.position);
                    float tentativeGScore = gScore[current] + distanceCost * neighbor.customCost;

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                    else if (tentativeGScore >= (gScore.ContainsKey(neighbor) ? gScore[neighbor] : float.MaxValue))
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = gScore[neighbor] + Vector2.Distance(neighbor.transform.position, targetNode.transform.position);
                }
            }

            path.Add(startNode);
            path.Add(targetNode);
            return path;
        }
    }
}