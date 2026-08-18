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

        // Breadth-First Search to find the actual chain of connected nodes
        public static List<PathNode> FindPath(PathNode startNode, PathNode targetNode)
        {
            List<PathNode> path = new List<PathNode>();
            if (startNode == null || targetNode == null) return path;

            if (startNode == modelTargetMatch(startNode, targetNode))
            {
                path.Add(startNode);
                return path;
            }

            Queue<PathNode> queue = new Queue<PathNode>();
            Dictionary<PathNode, PathNode> cameFrom = new Dictionary<PathNode, PathNode>();
            
            queue.Enqueue(startNode);
            cameFrom[startNode] = null;

            bool reachedTarget = false;

            while (queue.Count > 0)
            {
                PathNode current = queue.Dequeue();

                if (current == targetNode)
                {
                    reachedTarget = true;
                    break;
                }

                // Updated to loop through the new 'connections' list
                foreach (NodeConnection connection in current.connections)
                {
                    PathNode neighbor = connection != null ? connection.connectedNode : null;
                    if (neighbor != null && !cameFrom.ContainsKey(neighbor))
                    {
                        queue.Enqueue(neighbor);
                        cameFrom[neighbor] = current;
                    }
                }
            }

            if (reachedTarget)
            {
                PathNode curr = targetNode;
                while (curr != null)
                {
                    path.Add(curr);
                    curr = cameFrom[curr];
                }
                path.Reverse();
            }
            else
            {
                // Fallback if no direct graph connection exists
                path.Add(startNode);
                path.Add(targetNode);
            }

            return path;
        }

        private static PathNode modelTargetMatch(PathNode a, PathNode b) => a == b ? a : null;
    }
}