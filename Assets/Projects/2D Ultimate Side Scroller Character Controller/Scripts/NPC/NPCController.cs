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

        // The spawner that sent us, asked for somewhere new to go after resting at a rest area
        [System.NonSerialized] public NPCManager manager;

        // Resting at a rest area (eating, smoking): we hold still in the room and are untouchable, so the player and
        // other NPCs pass by without colliding or being slowed down by us
        public bool IsResting { get; private set; }
        private float restSpotX;

        // Where we stand inside the elevator car while riding, so riders spread out instead of all standing in the middle
        private float rideSpotX = float.NaN;

        private List<PathNode> currentPath = new List<PathNode>();
        private int currentNodeIndex = 0;
        private PathNode lastProcessedNode = null;
        private PathNode previousNode = null; // Last node we actually reached

        private Rigidbody2D rb;
        private CapsuleCollider2D capsuleCollider;
        
        public bool isWaitingAtNode = false;

        // Every active NPC, so elevators can see who is waiting for them or riding
        public static readonly List<NPCController> All = new List<NPCController>();

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

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

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
            if (IsResting)
            {
                SettleIntoRestSpot();
                return;
            }

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
                            if (!otherNpc.IsResting && col.gameObject.layer == gameObject.layer
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

            if (currentPath == null || currentPath.Count == 0 || currentNodeIndex >= currentPath.Count)
            {
                rb.linearVelocity = new Vector2(0f, platformVelY);
                return;
            }

            PathNode targetPathNode = currentPath[currentNodeIndex];
            if (targetPathNode == null) return;

            // Waiting for an elevator (or any paused node): halt horizontal movement while keeping the vertical platform motion
            if (!CanHeadFor(ref targetPathNode))
            {
                isWaitingAtNode = true;
                rb.linearVelocity = new Vector2(MoveToRideSpot(), platformVelY);
                return;
            }
            isWaitingAtNode = false;

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
                previousNode = targetPathNode;
                currentNodeIndex++;

                // Reached the final destination: rest there if it's a rest area, otherwise leave through the target door
                if (targetPathNode == targetNode && currentNodeIndex >= currentPath.Count)
                {
                    RestArea restArea = RestArea.ForNode(targetNode);
                    if (restArea != null) StartCoroutine(RestRoutine(restArea));
                    else if (despawnOnArrival) Destroy(gameObject);
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
                        bool isValidNpcPeer = otherNpc != null && !otherNpc.IsResting && col.gameObject.layer == gameObject.layer;

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

        // Whether we can walk toward our target node right now. Elevators are used through their front doors: we walk
        // along the walkway to the stop node in front of the doors, wait there, step in when given a place, ride (holding
        // still) until the doors open at a stop on our route, and step out there. Walking past a single stop node is just
        // walking. Everyone else never walks toward a paused node.
        private bool CanHeadFor(ref PathNode target)
        {
            Elevator riding = GetElevatorUnderfoot();
            if (riding != null)
            {
                if (riding.GetFloorIndex(target) >= 0)
                {
                    // Riding: when the doors open at any stop still on our route, that's where we are now
                    for (int i = currentNodeIndex; i < currentPath.Count && riding.GetFloorIndex(currentPath[i]) >= 0; i++)
                    {
                        if (riding.AreDoorsOpenAt(riding.GetFloorIndex(currentPath[i])))
                        {
                            currentNodeIndex = i;
                            target = currentPath[i];
                            return true;
                        }
                    }
                    return false;
                }

                // Past our last stop: step out once the doors are open at our floor. If the car left before we could,
                // our exit floor stays one of its car calls and it brings us back.
                int exitFloor = GetExitFloor(riding);
                return exitFloor >= 0 && riding.AreDoorsOpenAt(exitFloor) && riding.TryExit(capsuleCollider);
            }

            // At the front doors waiting to ride: step in once we've been given a place
            Elevator waitingFor = ElevatorWeAreWaitingAt();
            if (waitingFor != null)
            {
                if (waitingFor.MayBoard(this)) waitingFor.TryEnter(capsuleCollider);
                return false; // either still waiting, or just stepped in (riding logic takes over next step)
            }

            return !target.pauseMovement;
        }

        // Horizontal velocity while holding still for the elevator: riders shuffle to their own spot in the car (rolled
        // when they step in), everyone else stands still. When the doors open at our floor we walk back to them as usual.
        private float MoveToRideSpot()
        {
            Elevator riding = GetElevatorUnderfoot();
            if (riding == null)
            {
                rideSpotX = float.NaN;
                return 0f;
            }

            if (float.IsNaN(rideSpotX)) rideSpotX = riding.RollRiderSpotX();
            float offset = rideSpotX - transform.position.x;
            if (Mathf.Abs(offset) < 0.05f) return 0f;

            float moveDir = Mathf.Sign(offset);
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * moveDir;
            transform.localScale = scale;
            return moveDir * Mathf.Min(moveSpeed * 0.5f, Mathf.Abs(offset) / Time.fixedDeltaTime);
        }

        // Standing at an elevator's front doors (we've reached a stop node) with the next node another of its stops
        private Elevator ElevatorWeAreWaitingAt()
        {
            if (currentPath == null || currentNodeIndex == 0 || currentNodeIndex >= currentPath.Count) return null;
            return Elevator.IsRideLeg(currentPath[currentNodeIndex - 1], currentPath[currentNodeIndex])
                ? Elevator.ForStop(currentPath[currentNodeIndex - 1]) : null;
        }

        // If our route uses this elevator now, which floor we board at and which floor we get off at, and whether we're
        // already at its doors. A ride is a run of two or more consecutive stop nodes; passing a single one is walking.
        public bool TryGetElevatorTrip(Elevator elevator, out int fromFloor, out int toFloor, out bool atDoor)
        {
            fromFloor = toFloor = -1;
            atDoor = false;
            if (currentPath == null || currentNodeIndex >= currentPath.Count) return false;

            int targetFloor = elevator.GetFloorIndex(currentPath[currentNodeIndex]);
            if (IsStandingOn(elevator))
            {
                // Riding: heading for the last stop of our run, or stepping out at the stop we just reached
                fromFloor = targetFloor >= 0 ? targetFloor : GetExitFloor(elevator);
                toFloor = targetFloor >= 0 ? LastStopFloor(elevator, currentNodeIndex) : fromFloor;
                return fromFloor >= 0;
            }

            if (currentNodeIndex > 0 && targetFloor >= 0 && elevator.GetFloorIndex(currentPath[currentNodeIndex - 1]) >= 0)
            {
                // Waiting at the doors: we've reached the stop we board at and the next node is another stop
                atDoor = true;
                fromFloor = elevator.GetFloorIndex(currentPath[currentNodeIndex - 1]);
                toFloor = LastStopFloor(elevator, currentNodeIndex);
                return true;
            }

            if (targetFloor >= 0 && currentNodeIndex + 1 < currentPath.Count && elevator.GetFloorIndex(currentPath[currentNodeIndex + 1]) >= 0)
            {
                // Walking up to the doors to ride: call it now, like pressing the button as you arrive
                fromFloor = targetFloor;
                toFloor = LastStopFloor(elevator, currentNodeIndex + 1);
                return true;
            }
            return false;
        }

        // Floor of the last stop in the run of consecutive stop nodes starting at the given path index
        private int LastStopFloor(Elevator elevator, int startIndex)
        {
            int floor = -1;
            for (int i = startIndex; i < currentPath.Count; i++)
            {
                int f = elevator.GetFloorIndex(currentPath[i]);
                if (f < 0) break;
                floor = f;
            }
            return floor;
        }

        // The floor we're getting off at: the elevator stop we last reached, if we've stepped past it toward a floor node
        private int GetExitFloor(Elevator elevator)
        {
            if (currentPath == null || currentNodeIndex == 0 || currentNodeIndex > currentPath.Count) return -1;
            return elevator.GetFloorIndex(currentPath[currentNodeIndex - 1]);
        }

        public Collider2D BodyCollider => capsuleCollider != null ? capsuleCollider : GetComponent<CapsuleCollider2D>();

        public bool IsStandingOn(Elevator elevator)
        {
            return elevator.IsRiding(BodyCollider);
        }

        private Elevator GetElevatorUnderfoot()
        {
            foreach (var elevator in Elevator.All)
            {
                if (elevator.IsRiding(BodyCollider)) return elevator;
            }
            return null;
        }

        // Settle at a spot in the room, rest for a random stay, then become a normal walker again and head somewhere new.
        // While resting the body is kinematic so nothing can shove us, and other NPCs and the player skip us in their
        // crowd checks (their collisions with us are already ignored at close range).
        private IEnumerator RestRoutine(RestArea area)
        {
            IsResting = true;
            restSpotX = area.RollSpotX();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            currentPlatformRb = null;
            currentGroundCollider = null;

            // Draw behind passers-by, so they visibly walk past in front of us
            SpriteRenderer[] sprites = GetComponentsInChildren<SpriteRenderer>();
            int[] originalOrders = new int[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                originalOrders[i] = sprites[i].sortingOrder;
                sprites[i].sortingOrder = area.restingSortingOrder;
            }

            // Tint the body (not accessories like the glasses) to show we're resting
            SpriteRenderer body = GetComponent<SpriteRenderer>();
            Color originalColor = body != null ? body.color : Color.white;
            if (body != null) body.color = area.restingColor;

            // Stay at least one visit, and keep resting if there's nowhere reachable to go next
            PathNode next = null;
            while (next == null)
            {
                yield return new WaitForSeconds(area.RollStayTime());
                if (manager != null) next = manager.PickDestination(area.node);
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null) sprites[i].sortingOrder = originalOrders[i];
            }
            if (body != null) body.color = originalColor;
            rb.bodyType = RigidbodyType2D.Dynamic;
            IsResting = false;

            // Walk back to the room's node and on from there
            startNode = area.node;
            targetNode = next;
            SetDestination(area.node, next);
        }

        // Stroll to our spot in the rest area, then stand still
        private void SettleIntoRestSpot()
        {
            float offset = restSpotX - transform.position.x;
            if (Mathf.Abs(offset) < 0.05f)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            float moveDir = Mathf.Sign(offset);
            rb.linearVelocity = new Vector2(moveDir * Mathf.Min(moveSpeed * 0.5f, Mathf.Abs(offset) / Time.fixedDeltaTime), 0f);

            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * moveDir;
            transform.localScale = scale;
        }

        private IEnumerator PeriodicRepathRoutine()
        {
            WaitForSeconds repathInterval = new WaitForSeconds(2f); // Check every 2 seconds

            while (true)
            {
                yield return repathInterval;

                // If we have a final destination, periodically check for cheaper/updated paths
                if (targetNode == null || currentPath == null || currentNodeIndex >= currentPath.Count) continue;

                // Never re-route mid-ride: we're committed until the elevator lets us off
                if (GetElevatorUnderfoot() != null) continue;

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

        private static bool IsMovingPlatform(GameObject obj)
        {
            return obj.GetComponent<Platform>() != null || obj.GetComponent<Elevator>() != null;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (IsMovingPlatform(collision.gameObject))
            {
                currentPlatformRb = collision.rigidbody;
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (IsMovingPlatform(collision.gameObject))
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