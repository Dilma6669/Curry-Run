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
        [Tooltip("Each NPC picks a random walk speed between min and max when it spawns")]
        public float minMoveSpeed = 2.5f;
        public float maxMoveSpeed = 3.5f;
        [NonEditable] public float moveSpeed = 3f; // The speed this NPC rolled, shown in the inspector for debugging
        public float reachThreshold = 0.2f;

        [Header("Crowd Slowdown Settings")]
        [Range(0f, 1f)] [SerializeField] private float playerSlowdownMultiplier = 0.6f;
        [Range(0f, 1f)] [SerializeField] private float npcSlowdownMultiplier = 0.5f;

        [Header("Pathfinding")]
        public PathNode startNode;
        public PathNode targetNode;

        [Header("Arrival")]
        public bool despawnOnArrival = true;

        private List<PathNode> currentPath = new List<PathNode>();
        private int currentNodeIndex = 0;
        private PathNode lastProcessedNode = null;
        private PathNode previousNode = null; // Last node we actually reached

        private Rigidbody2D rb;
        private CapsuleCollider2D capsuleCollider;
        
        public bool isWaitingAtNode = false;
        private Coroutine waitCoroutine = null;

        // Platform tracking variables
        private Rigidbody2D currentPlatformRb = null;

        // The surface (floor, ramp, elevator) we're standing on. The player uses this to skip
        // crowd slowdown from NPCs on surfaces it's passing through, e.g. a ramp it's walking past.
        [System.NonSerialized] public Collider2D currentGroundCollider = null;

        // Crowd inertia & gridlock variables
        private float speedMultiplier = 1f;

        void Awake()
        {
            moveSpeed = Random.Range(minMoveSpeed, maxMoveSpeed);
        }

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

            // Start periodic repathing to adapt to changing elevator/waypoint costs
            StartCoroutine(PeriodicRepathRoutine());
        }

        void FixedUpdate()
        {
            CheckCrowdOverlap();
            FollowPath();
        }

        public void SetDestination(PathNode start, PathNode destination)
        {
            currentPath = Pathfinding.FindPath(start, destination);
            currentNodeIndex = 0;
            lastProcessedNode = null;
            previousNode = null;
            speedMultiplier = 1f;
            StopWaiting();
        }

        private void StopWaiting()
        {
            isWaitingAtNode = false;
            if (waitCoroutine != null)
            {
                StopCoroutine(waitCoroutine);
                waitCoroutine = null;
            }
        }

        // Must comfortably exceed an NPC capsule's reach (1.0) so NPCs meeting diagonally on ramps are ignored before they touch
        private const float NpcPassThroughRadius = 2f;
        private const float CrowdCountRadius = 0.5f;

        void CheckCrowdOverlap()
        {
            int crowdingCount = 0;

            if (capsuleCollider != null)
            {
                Vector2 position = transform.position;
                Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(position, NpcPassThroughRadius);
                foreach (var col in nearbyColliders)
                {
                    if (col.gameObject != gameObject)
                    {
                        NPCController otherNpc = col.GetComponent<NPCController>();
                        if (otherNpc != null)
                        {
                            // 1. ALWAYS ignore physical collision with ANY other NPC so they never wedge or stack on each other.
                            // Ask the physics engine directly instead of caching, so a re-enabled collision self-heals.
                            if (!Physics2D.GetIgnoreCollision(capsuleCollider, col))
                            {
                                Physics2D.IgnoreCollision(capsuleCollider, col, true);
                            }

                            // 2. Count peer NPCs on the same layer, overlapping our space, for the general crowd gridlock penalty
                            if (col.gameObject.layer == gameObject.layer
                                && Vector2.Distance(col.ClosestPoint(position), position) <= CrowdCountRadius)
                            {
                                crowdingCount++;
                            }
                        }
                    }
                }
            }

            // Gridlock & stacking rules for general peer crowding:
            float targetMultiplier = 1f;
            if (crowdingCount == 1) targetMultiplier = 0.6f;
            else if (crowdingCount == 2) targetMultiplier = 0.2f;
            else if (crowdingCount >= 3) targetMultiplier = 0f;

            speedMultiplier = Mathf.MoveTowards(speedMultiplier, targetMultiplier, Time.fixedDeltaTime * 4f);
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
                return;
            }

            PathNode targetPathNode = currentPath[currentNodeIndex];
            if (targetPathNode == null) return;

            // Apply collision ignores whenever we step to a new node in the path
            if (targetPathNode != lastProcessedNode)
            {
                if (previousNode != null)
                {
                    NodeConnection connection = previousNode.GetConnectionTo(targetPathNode);
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

                previousNode = targetPathNode;
                currentNodeIndex++;

                // Reached the final destination (the target door)
                if (despawnOnArrival && targetPathNode == targetNode && currentNodeIndex >= currentPath.Count)
                {
                    Destroy(gameObject);
                }
                return;
            }

            // Move only horizontally toward the target node's X position
            float moveDir = Mathf.Sign(targetPathNode.transform.position.x - transform.position.x);
            if (Mathf.Abs(targetPathNode.transform.position.x - transform.position.x) < 0.05f)
            {
                moveDir = 0f;
            }

// --- NPC Crowd & Player Slowdown Check ---
            float npcTargetMultiplier = 1f;
            int npcBlockCount = 0;
            bool blockedByPlayer = false;

            if (capsuleCollider != null && moveDir != 0)
            {
                Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, 0.5f);
                foreach (var col in nearbyColliders)
                {
                    if (col.gameObject != gameObject)
                    {
                        PlayerMain detectedPlayer = col.GetComponent<PlayerMain>();
                        bool isPlayer = detectedPlayer != null && IsPlayerOnOurSurface(detectedPlayer);
                        NPCController otherNpc = col.GetComponent<NPCController>();
                        bool isValidNpcPeer = otherNpc != null && col.gameObject.layer == gameObject.layer;

                        if (isPlayer || isValidNpcPeer)
                        {
                            // Check if they are positioned ahead of us in our walking direction
                            float directionToTarget = Mathf.Sign(col.transform.position.x - transform.position.x);
                            if (directionToTarget == moveDir)
                            {
                                if (isPlayer)
                                {
                                    blockedByPlayer = true;
                                }
                                else if (isValidNpcPeer)
                                {
                                    npcBlockCount++;
                                }
                            }
                        }
                    }
                }

                // Apply stacking slowdown logic
                if (blockedByPlayer)
                {
                    npcTargetMultiplier = playerSlowdownMultiplier;
                }
                else if (npcBlockCount > 0)
                {
                    // Stack the penalty progressively based on how many NPCs are packed ahead
                    if (npcBlockCount == 1) 
                    {
                        npcTargetMultiplier = npcSlowdownMultiplier; // e.g., 0.5f
                    }
                    else if (npcBlockCount == 2) 
                    {
                        npcTargetMultiplier = npcSlowdownMultiplier * 0.6f; // Heavier slowdown
                    }
                    else 
                    {
                        npcTargetMultiplier = 0.1f; // Near-stop gridlock for 3+ stacked NPCs
                    }
                }
            }

            // Smoothly interpolate the NPC's speed multiplier
            speedMultiplier = Mathf.MoveTowards(speedMultiplier, npcTargetMultiplier, Time.fixedDeltaTime * 10f);

            float adjustedSpeed = moveSpeed * speedMultiplier;

            rb.linearVelocity = new Vector2(moveDir * adjustedSpeed, platformVelY);

            // Face the direction of movement (only if not completely gridlocked)
            if (moveDir != 0 && adjustedSpeed > 0.01f)
            {
                Vector3 scale = transform.localScale;
                scale.x = Mathf.Abs(scale.x) * moveDir;
                transform.localScale = scale;
            }
        }

        // The player stays on the Player layer and passes through surfaces via RampGate rules instead. If it's passing
        // through the surface we're standing on (e.g. walking along the floor while we climb the ramp), it isn't in our way.
        private bool IsPlayerOnOurSurface(PlayerMain player)
        {
            return currentGroundCollider == null || player.CapsuleCollider2D == null
                || !Physics2D.GetIgnoreCollision(player.CapsuleCollider2D, currentGroundCollider);
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
        
        private IEnumerator PeriodicRepathRoutine()
        {
            WaitForSeconds repathInterval = new WaitForSeconds(2f); // Check every 2 seconds

            while (true)
            {
                yield return repathInterval;

                // If we have a final destination, periodically check for cheaper/updated paths
                if (targetNode == null || currentPath == null || currentNodeIndex >= currentPath.Count) continue;

                // While waiting we are standing on the node we last reached, so we can reroute from there.
                // Otherwise stay committed to the node we're walking toward so we never turn back mid-segment.
                PathNode repathFrom = isWaitingAtNode ? previousNode : currentPath[currentNodeIndex];
                if (repathFrom == null || repathFrom == targetNode) continue;

                List<PathNode> newPath = Pathfinding.FindPath(repathFrom, targetNode);
                if (newPath == null || newPath.Count == 0) continue;

                currentPath = newPath;
                currentNodeIndex = 0;

                // Index 0 is the node we're standing on, so FollowPath re-reaches it immediately
                // and re-runs the pause check against the new next node.
                if (isWaitingAtNode) StopWaiting();
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Platform plat = collision.gameObject.GetComponent<Platform>();
            if (plat != null)
            {
                currentPlatformRb = collision.rigidbody;
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            Platform plat = collision.gameObject.GetComponent<Platform>();
            if (plat != null)
            {
                currentPlatformRb = null;
            }

            if (collision.collider == currentGroundCollider)
            {
                currentGroundCollider = null;
            }
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            // Any contact pushing us upward means we're standing on this collider
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y > 0.5f)
                {
                    currentGroundCollider = collision.collider;
                    return;
                }
            }
        }
    }
}