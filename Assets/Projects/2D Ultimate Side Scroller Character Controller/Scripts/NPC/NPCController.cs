using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class NPCController : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float moveSpeed = 3f;
        public float reachThreshold = 0.2f;

        [Header("Pathfinding")]
        public PathNode startNode;
        public PathNode targetNode;

        private List<PathNode> currentPath = new List<PathNode>();
        private int currentNodeIndex = 0;
        private PathNode lastProcessedNode = null;

        private Rigidbody2D rb;
        private CapsuleCollider2D capsuleCollider;
        private List<Collider2D> currentlyIgnoredColliders = new List<Collider2D>();

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            capsuleCollider = GetComponent<CapsuleCollider2D>();

            if (startNode != null)
            {
                transform.position = startNode.transform.position;
            }

            if (startNode != null && targetNode != null)
            {
                SetDestination(startNode, targetNode);
            }
        }

        void OnDestroy()
        {
            ResetIgnoredColliders();
        }

        void FixedUpdate()
        {
            FollowPath();
        }

        public void SetDestination(PathNode start, PathNode destination)
        {
            ResetIgnoredColliders();
            currentPath = Pathfinding.FindPath(start, destination);
            currentNodeIndex = 0;
            lastProcessedNode = null;
        }

        void FollowPath()
        {
            if (currentPath == null || currentPath.Count == 0 || currentNodeIndex >= currentPath.Count)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                ResetIgnoredColliders();
                return;
            }

            PathNode targetPathNode = currentPath[currentNodeIndex];
            if (targetPathNode == null) return;

            // Apply collision ignores whenever we step to a new node in the path
            if (targetPathNode != lastProcessedNode)
            {
                ResetIgnoredColliders();

                PathNode fromNode = (currentNodeIndex > 0) ? currentPath[currentNodeIndex - 1] : startNode;
                if (fromNode != null)
                {
                    NodeConnection connection = fromNode.GetConnectionTo(targetPathNode);
                    if (connection != null && connection.collidersToIgnore != null)
                    {
                        foreach (var col in connection.collidersToIgnore)
                        {
                            if (col != null)
                            {
                                Physics2D.IgnoreCollision(capsuleCollider, col, true);
                                currentlyIgnoredColliders.Add(col);
                            }
                        }
                    }
                }
                lastProcessedNode = targetPathNode;
            }

            // Check if we reached the node
            float distanceToNode = Vector2.Distance(transform.position, targetPathNode.transform.position);
            if (distanceToNode <= reachThreshold)
            {
                currentNodeIndex++;
                return;
            }

// Move only horizontally toward the target node's X position, leaving Y velocity entirely to gravity/physics
            float moveDir = Mathf.Sign(targetPathNode.transform.position.x - transform.position.x);
            if (Mathf.Abs(targetPathNode.transform.position.x - transform.position.x) < 0.05f)
            {
                moveDir = 0f;
            }

            rb.linearVelocity = new Vector2(moveDir * moveSpeed, rb.linearVelocity.y);

// Face the direction of movement
            if (moveDir != 0)
            {
                Vector3 scale = transform.localScale;
                scale.x = Mathf.Abs(scale.x) * moveDir;
                transform.localScale = scale;
            }
        }

        void ResetIgnoredColliders()
        {
            foreach (var col in currentlyIgnoredColliders)
            {
                if (col != null && capsuleCollider != null)
                {
                    Physics2D.IgnoreCollision(capsuleCollider, col, false);
                }
            }
            currentlyIgnoredColliders.Clear();
        }
    }
}