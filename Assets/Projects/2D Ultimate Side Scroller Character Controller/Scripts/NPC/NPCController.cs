using System.Collections;
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
        
        public bool isWaitingAtNode = false;
        private Coroutine waitCoroutine = null;
        
        [Header("Layer Settings")]
        private int defaultLayer;

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            capsuleCollider = GetComponent<CapsuleCollider2D>();
            defaultLayer = gameObject.layer;

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
            ResetLayerForced();
        }

        void FixedUpdate()
        {
            FollowPath();
            UpdateLayerBasedOnGround();
        }

        public void SetDestination(PathNode start, PathNode destination)
        {
            ResetIgnoredColliders();
            ResetLayerForced();
            currentPath = Pathfinding.FindPath(start, destination);
            currentNodeIndex = 0;
            lastProcessedNode = null;
            isWaitingAtNode = false;
            if (waitCoroutine != null)
            {
                StopCoroutine(waitCoroutine);
                waitCoroutine = null;
            }
        }

        void FollowPath()
        {
            // If we are waiting, halt movement completely while maintaining our path index
            if (isWaitingAtNode)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                return;
            }

            if (currentPath == null || currentPath.Count == 0 || currentNodeIndex >= currentPath.Count)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                ResetIgnoredColliders();
                ResetLayerForced();
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

            float distanceToNode = Vector2.Distance(transform.position, targetPathNode.transform.position);
            if (distanceToNode <= reachThreshold)
            {
                // Look ahead to the NEXT node in the path (e.g., P11 if we are currently at P3)
                int nextIndex = currentNodeIndex + 1;
                if (nextIndex < currentPath.Count)
                {
                    PathNode nextTargetNode = currentPath[nextIndex];
                    if (nextTargetNode != null && nextTargetNode.pauseMovement)
                    {
                        isWaitingAtNode = true;

                        // Start coroutine to poll if the pauseMovement condition clears
                        if (waitCoroutine == null)
                        {
                            waitCoroutine = StartCoroutine(CheckPauseStateRoutine(nextTargetNode));
                        }
                    }
                }

                currentNodeIndex++;
                return;
            }

            // Move only horizontally toward the target node's X position
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

        private IEnumerator CheckPauseStateRoutine(PathNode nodeToCheck)
        {
            WaitForSeconds waitInterval = new WaitForSeconds(1f);

            while (isWaitingAtNode)
            {
                // If the elevator arrived and flipped pauseMovement to false
                if (nodeToCheck != null && !nodeToCheck.pauseMovement)
                {
                    isWaitingAtNode = false;
                    waitCoroutine = null;
                    yield break;
                }

                yield return waitInterval;
            }

            waitCoroutine = null;
        }

        void UpdateLayerBasedOnGround()
        {
            RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 1.2f);
            
            if (hit.collider != null)
            {
                if (!hit.collider.CompareTag("Player"))
                {
                    int surfaceLayer = hit.collider.gameObject.layer;
                    if (gameObject.layer != surfaceLayer)
                    {
                        gameObject.layer = surfaceLayer;
                    }
                }
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

        void ResetLayerForced()
        {
            gameObject.layer = defaultLayer;
        }
        
        private void OnCollisionStay2D(Collision2D collision)
        {
            if (collision.gameObject.CompareTag("Player")) return;

            int surfaceLayer = collision.gameObject.layer;

            if (gameObject.layer != surfaceLayer)
            {
                gameObject.layer = surfaceLayer;
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            
        }
    }
}