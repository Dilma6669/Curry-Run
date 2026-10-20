using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    // A call-based elevator. NPCs waiting at a floor (and the player pressing the call button) make hall calls,
    // riders make car calls for where they're going. Like a real elevator it keeps travelling in one direction,
    // stopping for riders getting off and for anyone waiting to go the same way, then reverses when nothing is
    // left ahead. A floor's stop node is only unpaused while the doors are open there, so NPCs never walk into
    // an empty shaft.
    [RequireComponent(typeof(Rigidbody2D))]
    public class Elevator : MonoBehaviour
    {
        public enum State { Idle, Moving, DoorsOpen }

        [Header("Floors")]
        [Tooltip("One waypoint per floor, bottom to top. Each waypoint's Y is the platform height at that floor and its Stop Node is the PathNode in the shaft")]
        [SerializeField] private List<ElevatorWaypoint> floors = new List<ElevatorWaypoint>();
        [SerializeField] private int startFloorIndex = 0;

        [Header("Movement")]
        [SerializeField] private float speed = 5f;

        [Header("Capacity")]
        [Tooltip("Most characters (NPCs and the player) that can be on board at once. When full it won't stop for people waiting, only to let riders off")]
        [SerializeField, Min(1)] private int capacity = 10;

        [Header("Doors")]
        [Tooltip("Minimum time the doors stay open at a stop")]
        [SerializeField] private float minDoorOpenTime = 2f;
        [Tooltip("Doors close after this long even if NPCs are still boarding or leaving, so one stuck NPC can't hold the elevator forever")]
        [SerializeField] private float maxDoorOpenTime = 10f;

        [Header("Pathfinding Cost")]
        [Tooltip("Cost of a stop node when the elevator is at that floor")]
        [SerializeField] private float baseCost = 1f;
        [Tooltip("Extra cost per unit of distance between the elevator and a floor, so NPCs prefer the stairs when it's far away")]
        [SerializeField] private float distanceScaleFactor = 2f;
        [SerializeField] private float maxCost = 50f;

        [Header("Debug")]
        [NonEditable] public State state = State.Idle;
        [NonEditable] public int currentFloor;
        [NonEditable] public int direction; // 1 = up, -1 = down, 0 = no direction
        [NonEditable] public string requests;

        public static readonly List<Elevator> All = new List<Elevator>();

        public Rigidbody2D Body { get; private set; }
        public int FloorCount => floors.Count;
        public int CurrentFloor => currentFloor;

        private Collider2D platformCollider;
        private readonly Dictionary<PathNode, int> floorByStopNode = new Dictionary<PathNode, int>();
        private bool[] upCalls, downCalls, carCalls;
        private int boardingHere, leavingHere, passersByAboard, load;
        private readonly List<(NPCController npc, int toFloor)> waitingHere = new List<(NPCController, int)>(); // NPCs waiting at the current floor
        private readonly HashSet<NPCController> allowedToBoard = new HashSet<NPCController>();
        private int targetFloor;
        private float doorTimer, costTimer;
        private int skipHallCallsAtFloor = -1;   // set when doors time out on someone who never boarded, until we leave the floor
        private bool shaftBlocked;               // holding a descent because someone is in the shaft below

        // Requests from the player, fed into the same hall/car calls as the NPCs'
        private readonly HashSet<int> playerHallCalls = new HashSet<int>();
        private int playerCarCall = -1;
        private PlayerMain player;

        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            platformCollider = GetComponent<Collider2D>();
            upCalls = new bool[floors.Count];
            downCalls = new bool[floors.Count];
            carCalls = new bool[floors.Count];
            for (int i = 0; i < floors.Count; i++)
            {
                if (floors[i] != null && floors[i].stopNode != null)
                {
                    floorByStopNode[floors[i].stopNode] = i;
                }
            }
        }

        private void Start()
        {
            player = FindFirstObjectByType<PlayerMain>();
            if (floors.Count == 0) return;

            currentFloor = Mathf.Clamp(startFloorIndex, 0, floors.Count - 1);
            Body.position = new Vector2(Body.position.x, FloorY(currentFloor));
            transform.position = Body.position;

            // Doors start closed everywhere
            for (int i = 0; i < floors.Count; i++) SetDoorsOpen(i, false);
            UpdateStopCosts();
        }

        // ---------- Queries used by NPCs and the player ----------

        // Floor index of a stop node, or -1 if the node isn't one of this elevator's stops
        public int GetFloorIndex(PathNode node)
        {
            return node != null && floorByStopNode.TryGetValue(node, out int floor) ? floor : -1;
        }

        public float FloorY(int floor) => floors[floor].Height;

        // Height of the platform's walkable top surface at a floor
        public float FloorSurfaceY(int floor) => FloorY(floor) + (platformCollider != null ? platformCollider.bounds.extents.y : 0f);

        public Bounds PlatformBounds => platformCollider.bounds;

        // Whether this NPC has been given one of the free places at the current stop
        public bool MayBoard(NPCController npc) => allowedToBoard.Contains(npc);

        public bool HasRoom => load < capacity;

        // An NPC waiting here may only walk in if the doors are open and we're heading its way (or have nowhere to go yet)
        private bool CanBoard(int fromFloor, int toFloor)
        {
            if (state != State.DoorsOpen || fromFloor != currentFloor) return false;
            int departDirection = NextDirection();
            return departDirection == 0 || toFloor == fromFloor || (int)Mathf.Sign(toFloor - fromFloor) == departDirection;
        }

        // Player pressed the call button at a floor
        public void CallToFloor(int floor)
        {
            if (floor < 0 || floor >= floors.Count) return;
            playerHallCalls.Add(floor);
            if (skipHallCallsAtFloor == floor) skipHallCallsAtFloor = -1;
        }

        // Player picked a destination while riding
        public void SetPlayerDestination(int floor)
        {
            playerCarCall = (floor >= 0 && floor < floors.Count) ? floor : -1;
        }

        public bool IsPlayerRiding()
        {
            return player != null && player.PlayerData.Physics.CollidedMovingRigidbody == Body;
        }

        public bool AreDoorsOpenAt(int floor) => state == State.DoorsOpen && currentFloor == floor;

        // Whether a character is standing on the platform: feet at its top surface and overlapping it horizontally.
        // A position check rather than physics contacts, which flicker for a frame whenever the platform starts or stops.
        public bool IsCarrying(Collider2D character)
        {
            if (character == null || platformCollider == null) return false;
            Bounds c = character.bounds;
            Bounds plat = platformCollider.bounds;
            bool feetOnTop = c.min.y > plat.max.y - 0.2f && c.min.y < plat.max.y + 0.3f;
            bool overlapping = c.max.x > plat.min.x + 0.05f && c.min.x < plat.max.x - 0.05f;
            return feetOnTop && overlapping;
        }

        // ---------- Main loop ----------

        private void FixedUpdate()
        {
            if (floors.Count == 0) return;

            GatherRequests();
            switch (state)
            {
                case State.Idle: UpdateIdle(); break;
                case State.Moving: UpdateMoving(); break;
                case State.DoorsOpen: UpdateDoorsOpen(); break;
            }

            costTimer += Time.fixedDeltaTime;
            if (costTimer >= 1f)
            {
                costTimer = 0f;
                UpdateStopCosts();
            }
            requests = DescribeRequests();
        }

        // Rebuilds every hall call (someone waiting at a floor) and car call (a rider's destination) from scratch each step,
        // so a request disappears as soon as the NPC re-routes, despawns or is delivered.
        private void GatherRequests()
        {
            System.Array.Clear(upCalls, 0, upCalls.Length);
            System.Array.Clear(downCalls, 0, downCalls.Length);
            System.Array.Clear(carCalls, 0, carCalls.Length);
            boardingHere = 0;
            leavingHere = 0;
            passersByAboard = 0;
            load = IsPlayerRiding() ? 1 : 0;
            waitingHere.Clear();
            allowedToBoard.Clear();

            foreach (var npc in NPCController.All)
            {
                bool onBoard = npc.IsStandingOn(this);
                if (onBoard) load++;
                if (npc.TryGetElevatorTrip(this, out int fromFloor, out int toFloor))
                {
                    if (onBoard)
                    {
                        carCalls[toFloor] = true;
                        if (state == State.DoorsOpen && toFloor == currentFloor) leavingHere++;
                    }
                    else
                    {
                        if (toFloor >= fromFloor) upCalls[fromFloor] = true;
                        if (toFloor <= fromFloor) downCalls[fromFloor] = true;
                        if (fromFloor == currentFloor) waitingHere.Add((npc, toFloor));
                    }
                }
                else if (onBoard)
                {
                    // Not a passenger, just walking across the platform (e.g. on the ground floor): never carry them off
                    leavingHere++;
                    passersByAboard++;
                }
            }

            foreach (int floor in playerHallCalls)
            {
                upCalls[floor] = true;
                downCalls[floor] = true;
            }
            if (playerCarCall >= 0)
            {
                if (IsPlayerRiding()) carCalls[playerCarCall] = true;
                else playerCarCall = -1; // got off before getting there
            }

            if (skipHallCallsAtFloor >= 0)
            {
                upCalls[skipHallCallsAtFloor] = false;
                downCalls[skipHallCallsAtFloor] = false;
            }

            // Second pass, now all calls are known: hand out the free places to NPCs waiting here who are going our way,
            // closest to the doors first (so someone already walking in keeps their place)
            float shaftX = platformCollider.bounds.center.x;
            waitingHere.Sort((a, b) => Mathf.Abs(a.npc.transform.position.x - shaftX).CompareTo(Mathf.Abs(b.npc.transform.position.x - shaftX)));
            int freePlaces = capacity - load;
            foreach (var (npc, toFloor) in waitingHere)
            {
                if (freePlaces <= 0) break;
                if (!CanBoard(currentFloor, toFloor)) continue;
                allowedToBoard.Add(npc);
                boardingHere++;
                freePlaces--;
            }
        }

        private void UpdateIdle()
        {
            Body.linearVelocity = Vector2.zero;
            direction = NextDirection();

            if (HasRequestHere(direction))
            {
                OpenDoors();
                return;
            }
            if (direction != 0 && passersByAboard == 0)
            {
                targetFloor = currentFloor + direction;
                state = State.Moving;
            }
        }

        private void UpdateMoving()
        {
            // Someone walking through the shaft below (e.g. across the ground floor): wait for them rather than land on them
            shaftBlocked = direction < 0 && IsShaftBlockedBelow();
            if (shaftBlocked)
            {
                Body.linearVelocity = Vector2.zero;
                return;
            }

            float y = Body.position.y;
            float targetY = FloorY(targetFloor);

            if (Mathf.Abs(targetY - y) <= speed * Time.fixedDeltaTime)
            {
                currentFloor = targetFloor;
                skipHallCallsAtFloor = -1;

                if (ShouldStopAt(currentFloor))
                {
                    StopAt(targetY);
                    OpenDoors();
                    return;
                }
                if (HasRequestBeyond(currentFloor, direction))
                {
                    targetFloor = currentFloor + direction; // pass straight through this floor
                    targetY = FloorY(targetFloor);
                }
                else
                {
                    StopAt(targetY);
                    state = State.Idle;
                    return;
                }
            }
            Body.linearVelocity = new Vector2(0f, Mathf.Sign(targetY - y) * speed);
        }

        private void UpdateDoorsOpen()
        {
            Body.linearVelocity = Vector2.zero;
            doorTimer += Time.fixedDeltaTime;

            // Anyone getting off or on here is served now
            playerHallCalls.Remove(currentFloor);
            if (playerCarCall == currentFloor) playerCarCall = -1;

            bool busy = boardingHere > 0 || leavingHere > 0 || IsPlayerInDoorway();
            if (doorTimer < minDoorOpenTime || (busy && doorTimer < maxDoorOpenTime)) return;

            // If we're giving up on someone who never boarded, ignore this floor's hall calls until we've left it
            if (boardingHere > 0) skipHallCallsAtFloor = currentFloor;
            SetDoorsOpen(currentFloor, false);
            state = State.Idle;
        }

        // ---------- Scheduling ----------

        // Keep going while there are requests further along; otherwise head for riders' destinations first, then the nearest call
        private int NextDirection()
        {
            if (direction != 0 && HasRequestBeyond(currentFloor, direction)) return direction;
            if (HasCarCallBeyond(currentFloor, 1)) return 1;
            if (HasCarCallBeyond(currentFloor, -1)) return -1;

            int nearestUp = NearestRequestBeyond(currentFloor, 1);
            int nearestDown = NearestRequestBeyond(currentFloor, -1);
            if (nearestUp < 0 && nearestDown < 0) return 0;
            if (nearestDown < 0) return 1;
            if (nearestUp < 0) return -1;
            return (nearestUp - currentFloor) <= (currentFloor - nearestDown) ? 1 : -1;
        }

        private bool ShouldStopAt(int floor)
        {
            if (carCalls[floor]) return true;
            if (!HasRoom) return false; // full: only stop to let riders off
            if (direction > 0 ? upCalls[floor] : downCalls[floor]) return true;
            // Turnaround point: nothing further this way, so pick up people going the other way too
            return !HasRequestBeyond(floor, direction) && (upCalls[floor] || downCalls[floor]);
        }

        // Is there a reason to open the doors at the current floor before setting off in departDirection?
        private bool HasRequestHere(int departDirection)
        {
            if (carCalls[currentFloor]) return true;
            if (!HasRoom) return false;
            if (departDirection >= 0 && upCalls[currentFloor]) return true;
            if (departDirection <= 0 && downCalls[currentFloor]) return true;
            return false;
        }

        private bool HasRequestBeyond(int floor, int dir)
        {
            return NearestRequestBeyond(floor, dir) >= 0;
        }

        private int NearestRequestBeyond(int floor, int dir)
        {
            if (dir == 0) return -1;
            for (int f = floor + dir; f >= 0 && f < floors.Count; f += dir)
            {
                if (upCalls[f] || downCalls[f] || carCalls[f]) return f;
            }
            return -1;
        }

        private bool HasCarCallBeyond(int floor, int dir)
        {
            for (int f = floor + dir; f >= 0 && f < floors.Count; f += dir)
            {
                if (carCalls[f]) return true;
            }
            return false;
        }

        // ---------- Doors & helpers ----------

        private void OpenDoors()
        {
            state = State.DoorsOpen;
            doorTimer = 0f;
            SetDoorsOpen(currentFloor, true);
        }

        private void SetDoorsOpen(int floor, bool open)
        {
            PathNode stop = floors[floor] != null ? floors[floor].stopNode : null;
            if (stop != null) stop.pauseMovement = !open;
        }

        private void StopAt(float y)
        {
            Body.linearVelocity = Vector2.zero;
            Body.position = new Vector2(Body.position.x, y);
        }

        // The player straddling the platform edge at this floor: don't close the doors and carry them off half on
        private bool IsPlayerInDoorway()
        {
            if (player == null || player.CapsuleCollider2D == null) return false;
            Bounds p = player.CapsuleCollider2D.bounds;
            Bounds plat = platformCollider.bounds;
            bool atPlatformLevel = Mathf.Abs(p.min.y - plat.max.y) < 0.5f;
            bool straddling = (p.min.x < plat.min.x && p.max.x > plat.min.x) || (p.min.x < plat.max.x && p.max.x > plat.max.x);
            return atPlatformLevel && straddling;
        }

        // Checks a character-height zone directly beneath the platform for NPCs or the player. The zone is slightly narrower
        // than the platform, so someone merely brushing its side as it lands doesn't hold it in mid-air.
        private bool IsShaftBlockedBelow()
        {
            const float ClearanceHeight = 2.5f;
            const float EdgeInset = 0.3f;
            Bounds plat = platformCollider.bounds;
            Vector2 center = new Vector2(plat.center.x, plat.min.y - ClearanceHeight / 2f);
            Vector2 size = new Vector2(plat.size.x - EdgeInset * 2f, ClearanceHeight);
            foreach (var col in Physics2D.OverlapBoxAll(center, size, 0f))
            {
                if (col.isTrigger) continue;
                if (col.GetComponent<NPCController>() != null || col.GetComponent<PlayerMain>() != null) return true;
            }
            return false;
        }

        // Stop nodes cost more the further away the elevator is, so NPCs take the stairs when it's far off
        private void UpdateStopCosts()
        {
            for (int i = 0; i < floors.Count; i++)
            {
                PathNode stop = floors[i] != null ? floors[i].stopNode : null;
                if (stop == null) continue;
                float distance = Mathf.Abs(Body.position.y - FloorY(i));
                stop.customCost = Mathf.Min(baseCost + distance * distanceScaleFactor, maxCost);
            }
        }

        private string DescribeRequests()
        {
            var sb = new System.Text.StringBuilder();
            for (int f = 0; f < floors.Count; f++)
            {
                if (!(upCalls[f] || downCalls[f] || carCalls[f])) continue;
                sb.Append($"F{f}:{(upCalls[f] ? "↑" : "")}{(downCalls[f] ? "↓" : "")}{(carCalls[f] ? "●" : "")} ");
            }
            sb.Append($"| load {load}/{capacity} ");
            if (state == State.DoorsOpen) sb.Append($"| boarding {boardingHere} leaving {leavingHere}");
            if (state == State.Moving && shaftBlocked) sb.Append("| waiting for shaft below to clear");
            if (state == State.Idle && passersByAboard > 0) sb.Append($"| waiting for {passersByAboard} passer-by to step off");
            return sb.ToString();
        }
    }
}
