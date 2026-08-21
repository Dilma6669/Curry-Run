using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UltimateCC
{
    public class PathNode : MonoBehaviour
    {
        public bool pauseMovement; // If true, NPC stops here before proceeding to this node
        
        [Header("Connections")]
        public List<NodeConnection> connections = new List<NodeConnection>(); // Links and specific collision rules per neighbor
        
        // Helper to find the specific connection data for a neighbor
        public NodeConnection GetConnectionTo(PathNode targetNode)
        {
            return connections.Find(c => c.connectedNode == targetNode);
        }
        
        private void OnDrawGizmos()
        {
            // Draw the node in the scene view
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(transform.position, 0.3f);

            // Draw lines to connected nodes using the new connection structure
            Gizmos.color = Color.white;
            foreach (var connection in connections)
            {
                if (connection != null && connection.connectedNode != null)
                {
                    Gizmos.DrawLine(transform.position, connection.connectedNode.transform.position);
                }
            }

#if UNITY_EDITOR
            // Display the GameObject's name slightly above the node sphere
            Vector3 textPosition = transform.position + Vector3.up;
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;
            Handles.Label(textPosition, gameObject.name, style);
#endif
        }
    }

    [System.Serializable]
    public class NodeConnection
    {
        public PathNode connectedNode;
        public List<Collider2D> collidersToIgnore = new List<Collider2D>(); // Ramps, floors, or barriers to disable for this step
    }
}