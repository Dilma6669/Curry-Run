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
        public enum State { Idle, Moving, DoorsOpen, DoorsClosing }

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
        [Tooltip("How long after stopping at a floor before the doors open (each floor's landing doors are set on its ElevatorWaypoint)")]
        [SerializeField] private float doorOpenDelay = 1f;
        [Tooltip("How long the doors take to close before the elevator sets off")]
        [SerializeField] private float doorCloseDelay = 0.75f;
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
        private float stoppedTimer;   // time since we last stopped; the doors open once it reaches doorOpenDelay
        private float closingTimer;
        private int skipHallCallsAtFloor = -1;   // set when doors time out on someone who never boarded, until we leave the floor
        private bool shaftBlocked;               // holding a descent because someone is in the shaft below

        // Requests from the player, fed into the same hall/car calls as the NPCs'
        private readonly HashSet<int> playerHallCalls = new HashSet<int>();
        private readonly HashSet<int> playerCarCalls = new HashSet<int>();   // floor buttons the player has lit inside the car
        private PlayerMain player;

        // Landing doors: closed (solid for everyone) unless we're stopped at that floor or the player forced them.
        // When open but we're full they stay solid for the player only.
        private float[] forcedOpenUntil;
        private int[] doorShownOpen;   // per floor: -1 not yet shown, 0 closed, 1 open
        private bool playerBlocked;

        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            platformCollider = GetComponent<Collider2D>();
            upCalls = new bool[floors.Count];
            downCalls = new bool[floors.Count];
            carCalls = new bool[floors.Count];
            forcedOpenUntil = new float[floors.Count];
            doorShownOpen = new int[floors.Count];
            for (int i = 0; i < floors.Count; i++) doorShownOpen[i] = -1;
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

            // Stop nodes start closed everywhere; the car itself starts parked with its doors open
            for (int i = 0; i < floors.Count; i++) SetDoorsOpen(i, false);
            stoppedTimer = doorOpenDelay;
            UpdateStopCosts();
            UpdateDoors();
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

        // Player pressed a floor on the keypad while riding. It becomes a car call like any rider's, so it's served in the
        // order the elevator's route reaches it rather than jumping the queue.
        public bool AddPlayerDestination(int floor)
        {
            if (floor < 0 || floor >= floors.Count) return false;
            playerCarCalls.Add(floor);
            return true;
        }

        public bool IsPlayerRiding()
        {
            if (player == null || player.CapsuleCollider2D == null) return false;
            if (player.PlayerData.Physics.CollidedMovingRigidbody == Body) return true;
            // The physics contact can drop for a moment while moving (e.g. pushing against a door), so also count
            // being inside the car: between its edges and on or just above its floor
            Bounds p = player.CapsuleCollider2D.bounds;
            Bounds plat = platformCollider.bounds;
            return p.min.x > plat.min.x && p.max.x < plat.max.x && p.min.y > plat.max.y - 0.2f && p.min.y < plat.max.y + 1.5f;
        }

        // Stopped at this floor with the doors fully open (after the opening delay), so people can get on and off
        public bool AreDoorsOpenAt(int floor) => state == State.DoorsOpen && currentFloor == floor && DoorsPhysicallyOpen;

        private bool DoorsPhysicallyOpen => state != State.Moving && state != State.DoorsClosing && stoppedTimer >= doorOpenDelay;

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

            if (state != State.Moving && state != State.DoorsClosing) stoppedTimer += Time.fixedDeltaTime;

            GatherRequests();
            switch (state)
            {
                case State.Idle: UpdateIdle(); break;
                case State.Moving: UpdateMoving(); break;
                case State.DoorsOpen: UpdateDoorsOpen(); break;
                case State.DoorsClosing: UpdateDoorsClosing(); break;
            }
            UpdateDoors();

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
            // The player takes a place while riding or while stepping through the doorway
            load = IsPlayerRiding() || IsPlayerInDoorway() ? 1 : 0;
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
            if (playerCarCalls.Count > 0)
            {
                if (IsPlayerRiding())
                {
                    foreach (int floor in playerCarCalls) carCalls[floor] = true;
                }
                else
                {
                    playerCarCalls.Clear(); // got off before getting there
                }
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
            if (direction != 0 && passersByAboard == 0 && !IsAnyoneInDoorway())
            {
                // Close the doors first, then set off
                targetFloor = currentFloor + direction;
                closingTimer = 0f;
                state = State.DoorsClosing;
            }
        }

        private void UpdateDoorsClosing()
        {
            Body.linearVelocity = Vector2.zero;

            // Someone new wants on here: re-open, like a real elevator
            if (HasRequestHere(NextDirection()))
            {
                stoppedTimer = 0f;
                OpenDoors();
                return;
            }

            closingTimer += Time.fixedDeltaTime;
            if (closingTimer >= doorCloseDelay) state = State.Moving;
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

            // Doors are still opening: nobody gets on or off yet
            if (!DoorsPhysicallyOpen) return;
            SetDoorsOpen(currentFloor, true);
            doorTimer += Time.fixedDeltaTime;

            // Anyone getting off or on here is served now
            playerHallCalls.Remove(currentFloor);
            playerCarCalls.Remove(currentFloor);

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

        // Start serving this floor. The stop node opens (and the door timer starts) once the doors have physically opened.
        private void OpenDoors()
        {
            state = State.DoorsOpen;
            doorTimer = 0f;
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
            stoppedTimer = 0f;
        }

        // Each floor's landing doors are closed unless the elevator is stopped there with its doors open, or the player has
        // forced them open. Open doors let everyone through, except the player at the elevator's floor when it's full.
        // Passing through is done with IgnoreCollision per character, so the colliders never get toggled, and a door never
        // turns solid on top of someone (we don't set off, or end a forced opening, while anyone is in the doorway).
        private void UpdateDoors()
        {
            // Full-elevator rule for the player, at the floor we're stopped at
            bool servingFloorOpen = DoorsPhysicallyOpen;
            var servingDoors = LandingDoors(currentFloor);
            bool blockPlayer = servingFloorOpen && !HasRoom && !IsPlayerRiding() && !IsPlayerInDoorway() && !IsPlayerTouching(servingDoors);
            // Once blocking, keep blocking until there's room again (the touching check only stops it turning on mid-overlap)
            if (playerBlocked && servingFloorOpen && !HasRoom && !IsPlayerRiding()) blockPlayer = true;
            playerBlocked = blockPlayer;

            for (int floor = 0; floor < floors.Count; floor++)
            {
                var doors = LandingDoors(floor);
                if (doors == null || doors.Count == 0) continue;

                bool forced = IsForcedOpen(floor, doors);
                bool open = forced || (floor == currentFloor && servingFloorOpen);
                SetDoorVisuals(floor, doors, open);

                foreach (var npc in NPCController.All) SetPassable(doors, npc.BodyCollider, open);
                if (player != null && player.CapsuleCollider2D != null)
                {
                    bool blocked = blockPlayer && floor == currentFloor && !forced;
                    SetPassable(doors, player.CapsuleCollider2D, open && !blocked);
                }
            }
        }

        // The player prising the doors open: stays open for the duration, and after that until nobody is in the doorway
        private bool IsForcedOpen(int floor, List<Collider2D> doors)
        {
            if (forcedOpenUntil[floor] <= 0f) return false;
            if (Time.time < forcedOpenUntil[floor]) return true;
            if (IsAnyoneIn(doors)) return true;
            forcedOpenUntil[floor] = 0f;
            return false;
        }

        // Player spamming the call button: force this floor's landing doors open even with no elevator there
        public bool ForceOpenLandingDoors(int floor, float duration)
        {
            if (floor < 0 || floor >= floors.Count || LandingDoors(floor) == null || LandingDoors(floor).Count == 0) return false;
            forcedOpenUntil[floor] = Mathf.Max(forcedOpenUntil[floor], Time.time + duration);
            return true;
        }

        private List<Collider2D> LandingDoors(int floor)
        {
            return floor >= 0 && floor < floors.Count && floors[floor] != null ? floors[floor].landingDoors : null;
        }

        private void SetDoorVisuals(int floor, List<Collider2D> doors, bool open)
        {
            if (doorShownOpen[floor] == (open ? 1 : 0)) return;
            doorShownOpen[floor] = open ? 1 : 0;
            foreach (var door in doors)
            {
                if (door == null) continue;
                foreach (var sprite in door.GetComponentsInChildren<SpriteRenderer>()) sprite.enabled = !open;
            }
        }

        // Like a door's safety sensor: a closed door stays passable for anyone already standing in it, so it never turns
        // solid on top of them (which would shove them out); it becomes solid for them once they're clear.
        private static void SetPassable(List<Collider2D> doors, Collider2D character, bool passable)
        {
            if (character == null) return;
            Bounds body = character.bounds;
            body.Expand(-0.1f); // real overlap only: someone merely pressed against the door doesn't count
            foreach (var door in doors)
            {
                if (door == null) continue;
                bool wanted = passable || Overlaps2D(door.bounds, body);
                if (Physics2D.GetIgnoreCollision(door, character) != wanted) Physics2D.IgnoreCollision(door, character, wanted);
            }
        }

        private bool IsPlayerTouching(List<Collider2D> doors)
        {
            if (doors == null) return false;
            Bounds p = player.CapsuleCollider2D.bounds;
            foreach (var door in doors)
            {
                if (door != null && Overlaps2D(door.bounds, p)) return true;
            }
            return false;
        }

        // Bounds.Intersects also compares z, and the doors don't sit at the same depth as the characters
        private static bool Overlaps2D(Bounds a, Bounds b)
        {
            return a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y;
        }

        // Anyone standing in this floor's doorway: don't set off and close the doors on them
        private bool IsAnyoneInDoorway() => IsAnyoneIn(LandingDoors(currentFloor));

        private static bool IsAnyoneIn(List<Collider2D> doors)
        {
            if (doors == null) return false;
            foreach (var door in doors)
            {
                if (door == null) continue;
                foreach (var col in Physics2D.OverlapBoxAll(door.bounds.center, door.bounds.size, 0f))
                {
                    if (col.isTrigger) continue;
                    if (col.GetComponent<NPCController>() != null || col.GetComponent<PlayerMain>() != null) return true;
                }
            }
            return false;
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
            if (playerBlocked) sb.Append("| full: player blocked ");
            if (state == State.DoorsOpen) sb.Append($"| boarding {boardingHere} leaving {leavingHere}");
            if (state == State.Moving && shaftBlocked) sb.Append("| waiting for shaft below to clear");
            if (state == State.Idle && passersByAboard > 0) sb.Append($"| waiting for {passersByAboard} passer-by to step off");
            sb.Append(state == State.DoorsClosing ? "| doors closing" : DoorsPhysicallyOpen ? "| doors open" : state == State.Moving ? "| doors closed" : "| doors opening");
            for (int f = 0; f < floors.Count; f++)
            {
                if (forcedOpenUntil[f] > 0f) sb.Append($" | F{f} doors FORCED open");
            }
            return sb.ToString();
        }
    }
}
