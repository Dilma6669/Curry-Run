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

        // Platform tracking variables
        private Rigidbody2D currentPlatformRb = null;
        
        //[Header("Layer Settings")]
        // private int defaultLayer;
        // private int npcIgnoreLayer;

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            capsuleCollider = GetComponent<CapsuleCollider2D>();
            
            // Explicitly look up and enforce the NPC layer as the base fallback
            // defaultLayer = LayerMask.NameToLayer("NPC");
            // npcIgnoreLayer = LayerMask.NameToLayer("NPCIgnore");
            //
            // if (defaultLayer != -1)
            // {
            //     gameObject.layer = defaultLayer;
            // }

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
           // ResetIgnoredColliders();
           // ResetLayerForced();
        }

        void FixedUpdate()
        {
            FollowPath();
            //UpdateLayerBasedOnGround();
        }

        public void SetDestination(PathNode start, PathNode destination)
        {
           // ResetIgnoredColliders();
           // ResetLayerForced();
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
            // Determine base vertical velocity adjustment from a platform if riding one
            float platformVelY = (currentPlatformRb != null) ? currentPlatformRb.linearVelocity.y : rb.linearVelocity.y;

            // If we are waiting, halt horizontal movement completely while keeping the vertical platform motion
            if (isWaitingAtNode)
            {
                rb.linearVelocity = new Vector2(0f, platformVelY);
                return;
            }

            if (currentPath == null || currentPath.Count == 0 || currentNodeIndex >= currentPath.Count)
            {
                rb.linearVelocity = new Vector2(0f, platformVelY);
               // ResetIgnoredColliders();
               // ResetLayerForced();
                return;
            }

            PathNode targetPathNode = currentPath[currentNodeIndex];
            if (targetPathNode == null) return;

            // Apply collision ignores whenever we step to a new node in the path
            if (targetPathNode != lastProcessedNode)
            {
                PathNode fromNode = (currentNodeIndex > 0) ? currentPath[currentNodeIndex - 1] : startNode;
                if (fromNode != null)
                {
                    NodeConnection connection = fromNode.GetConnectionTo(targetPathNode);
                    if (connection != null)
                    {
                        // Convert LayerMask to a single layer index and set the NPC layer
                        int layerIndex = (int)Mathf.Log(connection.targetLayer.value, 2);
                        if (layerIndex >= 0)
                        {
                            gameObject.layer = layerIndex;
                        }
                    }
                }
                lastProcessedNode = targetPathNode;
            }

            float distanceToNode = Vector2.Distance(transform.position, targetPathNode.transform.position);
            if (distanceToNode <= reachThreshold)
            {
                // Look ahead to the NEXT node in the path
                int nextIndex = currentNodeIndex + 1;
                if (nextIndex < currentPath.Count)
                {
                    PathNode nextTargetNode = currentPath[nextIndex];
                    if (nextTargetNode != null && nextTargetNode.pauseMovement)
                    {
                        isWaitingAtNode = true;

                        if (waitCoroutine == null)
                        {
                            waitCoroutine = StartCoroutine(CheckPauseStateRoutine(nextTargetNode));
                        }
                    }
                }

                currentNodeIndex++;
                return;
            }

            // Move only horizontally toward the target node's X position, matching the platform's vertical velocity
            float moveDir = Mathf.Sign(targetPathNode.transform.position.x - transform.position.x);
            if (Mathf.Abs(targetPathNode.transform.position.x - transform.position.x) < 0.05f)
            {
                moveDir = 0f;
            }

            rb.linearVelocity = new Vector2(moveDir * moveSpeed, platformVelY);

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

        // void UpdateLayerBasedOnGround()
        // {
        //     RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 1.2f);
        //     
        //     if (hit.collider != null)
        //     {
        //         if (!hit.collider.CompareTag("Player"))
        //         {
        //             if (hit.collider.gameObject.layer == LayerMask.NameToLayer("PlayerIgnore"))
        //             {
        //                 if (gameObject.layer != npcIgnoreLayer)
        //                 {
        //                     gameObject.layer = npcIgnoreLayer;
        //                 }
        //                 return;
        //             }
        //         }
        //     }
        //     
        //     ResetLayerForced();
        // }

        // void ResetIgnoredColliders()
        // {
        //     foreach (var col in currentlyIgnoredColliders)
        //     {
        //         if (col != null && capsuleCollider != null)
        //         {
        //             Physics2D.IgnoreCollision(capsuleCollider, col, false);
        //         }
        //     }
        //     currentlyIgnoredColliders.Clear();
        // }

        // void ResetLayerForced()
        // {
        //     gameObject.layer = defaultLayer;
        // }
        
        // private void OnCollisionStay2D(Collision2D collision)
        // {
        //     if (collision.gameObject.CompareTag("Player")) return;
        //
        //     // If we touch another NPC, explicitly ignore collision between them so they never push each other
        //     if (collision.gameObject.layer == defaultLayer || collision.gameObject.GetComponent<NPCController>() != null)
        //     {
        //         Physics2D.IgnoreCollision(capsuleCollider, collision.collider, true);
        //         return;
        //     }
        //
        //     // Check if we are standing on a platform script
        //     Platform plat = collision.gameObject.GetComponent<Platform>();
        //     if (plat != null)
        //     {
        //         currentPlatformRb = collision.rigidbody;
        //     }
        //
        //     // If colliding with a "PlayerIgnore" layer surface, set NPC to "NPCIgnore" layer
        //     if (collision.gameObject.layer == LayerMask.NameToLayer("PlayerIgnore"))
        //     {
        //         if (gameObject.layer != npcIgnoreLayer)
        //         {
        //             gameObject.layer = npcIgnoreLayer;
        //         }
        //     }
        //     else
        //     {
        //         ResetLayerForced();
        //     }
        // }

        // private void OnCollisionExit2D(Collision2D collision)
        // {
        //     Platform plat = collision.gameObject.GetComponent<Platform>();
        //     if (plat != null)
        //     {
        //         currentPlatformRb = null;
        //     }
        //
        //     // If we stop colliding with a PlayerIgnore surface, immediately revert back to the NPC layer
        //     if (collision.gameObject.layer == LayerMask.NameToLayer("PlayerIgnore"))
        //     {
        //         ResetLayerForced();
        //     }
        // }
    }
}