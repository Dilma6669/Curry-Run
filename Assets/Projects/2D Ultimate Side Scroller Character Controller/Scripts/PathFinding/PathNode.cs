using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UltimateCC
{
    public class PathNode : MonoBehaviour
    {
        [Header("Connections")]
        public List<NodeConnection> connections = new List<NodeConnection>(); 

        [Header("Pathfinding Costs")]
        public float customCost = 1f; // Extra weight/cost for stepping onto this node

        [Header("Wait Settings")]
        public bool pauseMovement;

        public NodeConnection GetConnectionTo(PathNode targetNode)
        {
            return connections.Find(c => c.connectedNode == targetNode);
        }
        
        private void OnDrawGizmos()
        {
            Gizmos.color = pauseMovement ? Color.yellow : Color.green;
            Gizmos.DrawSphere(transform.position, 0.3f);

            Gizmos.color = Color.white;
            foreach (var connection in connections)
            {
                if (connection != null && connection.connectedNode != null)
                {
                    Gizmos.DrawLine(transform.position, connection.connectedNode.transform.position);
                }
            }

#if UNITY_EDITOR
            Vector3 textPosition = transform.position + Vector3.up;
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;
            Handles.Label(textPosition, $"{gameObject.name} (Cost: {customCost})", style);
#endif
        }
    }

    [System.Serializable]
    public class NodeConnection
    {
        public PathNode connectedNode;
        public List<Collider2D> collidersToIgnore = new List<Collider2D>(); 
    }
}