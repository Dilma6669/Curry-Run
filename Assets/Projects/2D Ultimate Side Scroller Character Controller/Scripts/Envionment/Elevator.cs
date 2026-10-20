using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    // A call-based elevator that runs "behind" each floor's walkway, entered through front doors.
    //
    // Characters are either walkers or riders. Walkers (the default) walk across the shaft on each floor's walkway and
    // pass straight through the car. Riders have stepped in through the front doors (NPCs when given a place, the player
    // by pressing W): the car and its side walls are solid for them and the walkways are not, so the car carries them
    // through other floors. They become walkers again when they step out (NPCs at their floor, the player by pressing S).
    //
    // NPCs waiting at the doors (and the player pressing the call button) make hall calls, riders make car calls for
    // where they're going. Like a real elevator it keeps travelling in one direction, stopping for riders getting off and
    // for anyone waiting to go the same way, then reverses when nothing is left ahead.
    [RequireComponent(typeof(Rigidbody2D))]
    public class Elevator : MonoBehaviour
    {
        public enum State { Idle, Moving, DoorsOpen, DoorsClosing }

        [Header("Floors")]
        [Tooltip("One waypoint per floor, bottom to top. Each waypoint's Y is the platform height at that floor, its Stop Node is the PathNode in front of the doors, and its Front Door is the door art")]
        [SerializeField] private List<ElevatorWaypoint> floors = new List<ElevatorWaypoint>();
        [SerializeField] private int startFloorIndex = 0;

        [Header("Movement")]
        [SerializeField] private float speed = 5f;

        [Header("Capacity")]
        [Tooltip("Most riders (NPCs and the player) at once. When full it won't stop for people waiting, only to let riders off")]
        [SerializeField, Min(1)] private int capacity = 10;

        [Header("Car")]
        [Tooltip("Extra walls for the car. Every solid collider on the car (its floor, roof, walls and these) is solid only for riders, so nobody walks out of a moving car; walkers pass through all of it")]
        [SerializeField] private List<Collider2D> carWalls = new List<Collider2D>();
        [Tooltip("The walkway floor across the shaft on each floor. Riders pass through these so the car can carry them past other floors")]
        [SerializeField] private List<Collider2D> shaftFloors = new List<Collider2D>();
        [Tooltip("How close to the middle of the front doors a character must stand to step in")]
        [SerializeField] private float doorReach = 1.5f;
        [Tooltip("Riders spread out up to this far either side of the doors inside the car, instead of all standing in the middle")]
        [SerializeField] private float riderSpread = 3.5f;

        [Header("Visuals")]
        [Tooltip("Sorting order riders' sprites are drawn at while inside the car (their own offsets are kept on top of this). Keep it between the car's sprites and the shaft's front wall overlay, so riders appear behind the wall")]
        [SerializeField] private int riderSortingOrder = -19;

        [Header("Doors")]
        [Tooltip("How long after stopping at a floor before the doors open")]
        [SerializeField] private float doorOpenDelay = 1f;
        [Tooltip("How long the doors take to close before the elevator sets off")]
        [SerializeField] private float doorCloseDelay = 0.75f;
        [Tooltip("How long the doors take to slide apart (the last part of the opening delay). They slide shut over the closing delay")]
        [SerializeField] private float doorSlideTime = 0.5f;
        [Tooltip("Minimum time the doors stay open at a stop")]
        [SerializeField] private float minDoorOpenTime = 2f;
        [Tooltip("Doors close after this long even if NPCs are still boarding or leaving, so one stuck NPC can't hold the elevator forever")]
        [SerializeField] private float maxDoorOpenTime = 10f;

        [Header("Pathfinding Cost")]
        [Tooltip("Cost of a stop node when the elevator is at that floor (applied to riding legs only, not to walking past)")]
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
        public float DoorX => platformCollider.bounds.center.x;
        public bool HasRoom => riders.Count < capacity;

        private Collider2D platformCollider;
        private readonly List<Collider2D> carParts = new List<Collider2D>();   // every solid collider that moves with the car
        private readonly Dictionary<PathNode, int> floorByStopNode = new Dictionary<PathNode, int>();
        private readonly HashSet<Collider2D> riders = new HashSet<Collider2D>();
        private readonly Dictionary<Collider2D, List<(SpriteRenderer sprite, int order)>> riderSortingBackup = new Dictionary<Collider2D, List<(SpriteRenderer, int)>>();
        private bool[] upCalls, downCalls, carCalls;
        private int boardingHere, leavingHere;
        private readonly List<(NPCController npc, int toFloor)> waitingHere = new List<(NPCController, int)>(); // NPCs at the doors of the current floor
        private readonly HashSet<NPCController> allowedToBoard = new HashSet<NPCController>();
        private int targetFloor;
        private float doorTimer, costTimer;
        private float stoppedTimer;   // time since we last stopped; the doors open once it reaches doorOpenDelay
        private float closingTimer;
        private int skipHallCallsAtFloor = -1;   // set when doors time out on someone who never boarded, until we leave the floor
        private Vector3[] leftDoorClosed, rightDoorClosed;   // door leaf positions when shut, per floor
        private float[] doorLeafWidth;

        // Requests from the player, fed into the same hall/car calls as the NPCs'
        private readonly HashSet<int> playerHallCalls = new HashSet<int>();
        private readonly HashSet<int> playerCarCalls = new HashSet<int>();   // floor buttons the player has lit inside the car
        private PlayerMain player;

        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            platformCollider = GetComponent<Collider2D>();
            foreach (var part in GetComponentsInChildren<Collider2D>(true))
            {
                if (!part.isTrigger) carParts.Add(part);
            }
            foreach (var wall in carWalls)
            {
                if (wall != null && !carParts.Contains(wall)) carParts.Add(wall);
            }
            upCalls = new bool[floors.Count];
            downCalls = new bool[floors.Count];
            carCalls = new bool[floors.Count];
            leftDoorClosed = new Vector3[floors.Count];
            rightDoorClosed = new Vector3[floors.Count];
            doorLeafWidth = new float[floors.Count];
            for (int i = 0; i < floors.Count; i++)
            {
                var wp = floors[i];
                if (wp == null) continue;
                if (wp.stopNode != null) floorByStopNode[wp.stopNode] = i;

                // Door leaves are placed shut in the scene; remember that and how far each slides to open
                if (wp.leftDoor != null) leftDoorClosed[i] = wp.leftDoor.position;
                if (wp.rightDoor != null) rightDoorClosed[i] = wp.rightDoor.position;
                var leaf = wp.leftDoor != null ? wp.leftDoor.GetComponentInChildren<SpriteRenderer>() : null;
                doorLeafWidth[i] = leaf != null ? leaf.bounds.size.x : 0f;
            }
        }

        private void Start()
        {
            player = FindFirstObjectByType<PlayerMain>();
            if (floors.Count == 0) return;

            currentFloor = Mathf.Clamp(startFloorIndex, 0, floors.Count - 1);
            Body.position = new Vector2(Body.position.x, FloorY(currentFloor));
            transform.position = Body.position;

            // Stop nodes are just the spot in front of the doors now, always reachable on the walkway
            foreach (var stop in floorByStopNode.Keys) stop.pauseMovement = false;
            stoppedTimer = doorOpenDelay; // parked with the doors open
            UpdateStopCosts();
            UpdateCollisions();
            UpdateFrontDoors();
        }

        // ---------- Queries used by NPCs, the player and pathfinding ----------

        // Floor index of a stop node, or -1 if the node isn't one of this elevator's stops
        public int GetFloorIndex(PathNode node)
        {
            return node != null && floorByStopNode.TryGetValue(node, out int floor) ? floor : -1;
        }

        public static Elevator ForStop(PathNode node)
        {
            foreach (var elevator in All)
            {
                if (elevator.GetFloorIndex(node) >= 0) return elevator;
            }
            return null;
        }

        // Moving between two of the same elevator's stops means riding it (as opposed to walking past one stop)
        public static bool IsRideLeg(PathNode from, PathNode to)
        {
            var elevator = ForStop(from);
            return elevator != null && elevator.GetFloorIndex(to) >= 0;
        }

        public float FloorY(int floor) => floors[floor].Height;

        // The floor the car is at or currently passing, for the floor displays above the doors. Unlike currentFloor
        // (which changes on arrival) this counts floors as the car goes by them.
        public int NearestFloor
        {
            get
            {
                if (Body == null || floors.Count == 0) return currentFloor;
                int nearest = 0;
                for (int i = 1; i < floors.Count; i++)
                {
                    if (Mathf.Abs(FloorY(i) - Body.position.y) < Mathf.Abs(FloorY(nearest) - Body.position.y)) nearest = i;
                }
                return nearest;
            }
        }

        // Height of the platform's walkable top surface at a floor
        public float FloorSurfaceY(int floor) => FloorY(floor) + (platformCollider != null ? platformCollider.bounds.extents.y : 0f);

        public bool IsRiding(Collider2D character) => character != null && riders.Contains(character);

        public bool IsPlayerRiding() => player != null && IsRiding(player.CapsuleCollider2D);

        // Stopped at this floor with the doors fully open (after the opening delay, including while parked idle), so people
        // can step in and out
        public bool AreDoorsOpenAt(int floor) => currentFloor == floor && DoorsPhysicallyOpen;

        private bool DoorsPhysicallyOpen => state != State.Moving && state != State.DoorsClosing && stoppedTimer >= doorOpenDelay;

        // Whether this NPC has been given one of the free places at the current stop
        public bool MayBoard(NPCController npc) => allowedToBoard.Contains(npc);

        // Standing on this floor's walkway in front of its doors
        public bool IsAtFrontDoor(Collider2D character, int floor)
        {
            if (character == null || floor < 0 || floor >= floors.Count) return false;
            Bounds c = character.bounds;
            return Mathf.Abs(c.center.x - DoorX) <= doorReach && Mathf.Abs(c.min.y - FloorSurfaceY(floor)) < 0.5f;
        }

        // A spot inside the car for one rider to stand at
        public float RollRiderSpotX() => DoorX + Random.Range(-riderSpread, riderSpread);

        // Step in through the front doors: only while they're open at this floor and there's room
        public bool TryEnter(Collider2D character)
        {
            if (character == null || riders.Contains(character)) return false;
            if (!AreDoorsOpenAt(currentFloor) || !HasRoom || !IsAtFrontDoor(character, currentFloor)) return false;
            riders.Add(character);
            SetRiderCollisions(character, true);
            DrawBehindShaftWall(character, true);
            return true;
        }

        // Step out onto the walkway: only while the doors are open
        public bool TryExit(Collider2D character)
        {
            if (character == null || !riders.Contains(character) || !AreDoorsOpenAt(currentFloor)) return false;
            riders.Remove(character);
            SetRiderCollisions(character, false);
            DrawBehindShaftWall(character, false);
            return true;
        }

        // Riders are drawn back at the car's depth (behind the shaft's front wall), walkers at their normal sorting order
        private void DrawBehindShaftWall(Collider2D character, bool behind)
        {
            if (behind)
            {
                var backup = new List<(SpriteRenderer, int)>();
                foreach (var sprite in character.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    backup.Add((sprite, sprite.sortingOrder));
                    sprite.sortingOrder = riderSortingOrder + sprite.sortingOrder;
                }
                riderSortingBackup[character] = backup;
            }
            else if (riderSortingBackup.TryGetValue(character, out var backup))
            {
                foreach (var (sprite, order) in backup)
                {
                    if (sprite != null) sprite.sortingOrder = order;
                }
                riderSortingBackup.Remove(character);
            }
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

        // ---------- Main loop ----------

        private void FixedUpdate()
        {
            if (floors.Count == 0) return;

            if (state != State.Moving && state != State.DoorsClosing) stoppedTimer += Time.fixedDeltaTime;

            if (riders.RemoveWhere(c => c == null) > 0) // riders that despawned
            {
                var gone = new List<Collider2D>();
                foreach (var key in riderSortingBackup.Keys) if (key == null) gone.Add(key);
                foreach (var key in gone) riderSortingBackup.Remove(key);
            }
            GatherRequests();
            switch (state)
            {
                case State.Idle: UpdateIdle(); break;
                case State.Moving: UpdateMoving(); break;
                case State.DoorsOpen: UpdateDoorsOpen(); break;
                case State.DoorsClosing: UpdateDoorsClosing(); break;
            }
            UpdateCollisions();
            UpdateFrontDoors();

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
            waitingHere.Clear();
            allowedToBoard.Clear();

            foreach (var npc in NPCController.All)
            {
                if (!npc.TryGetElevatorTrip(this, out int fromFloor, out int toFloor, out bool atDoor)) continue;
                if (IsRiding(npc.BodyCollider))
                {
                    carCalls[toFloor] = true;
                    if (state == State.DoorsOpen && toFloor == currentFloor) leavingHere++;
                }
                else
                {
                    if (toFloor >= fromFloor) upCalls[fromFloor] = true;
                    if (toFloor <= fromFloor) downCalls[fromFloor] = true;
                    if (atDoor && fromFloor == currentFloor) waitingHere.Add((npc, toFloor));
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
                    playerCarCalls.Clear(); // stepped out before getting there
                }
            }

            if (skipHallCallsAtFloor >= 0)
            {
                upCalls[skipHallCallsAtFloor] = false;
                downCalls[skipHallCallsAtFloor] = false;
            }

            // Second pass, now all calls are known: hand out the free places to NPCs at the doors who are going our way,
            // closest to the middle of the doors first
            float doorX = DoorX;
            waitingHere.Sort((a, b) => Mathf.Abs(a.npc.transform.position.x - doorX).CompareTo(Mathf.Abs(b.npc.transform.position.x - doorX)));
            int freePlaces = capacity - riders.Count;
            foreach (var (npc, toFloor) in waitingHere)
            {
                if (freePlaces <= 0) break;
                if (!CanBoard(currentFloor, toFloor)) continue;
                allowedToBoard.Add(npc);
                boardingHere++;
                freePlaces--;
            }
        }

        // An NPC waiting here may only step in if the doors are open and we're heading its way (or have nowhere to go yet)
        private bool CanBoard(int fromFloor, int toFloor)
        {
            if (state != State.DoorsOpen || !AreDoorsOpenAt(fromFloor)) return false; // places are only handed out at a proper stop
            int departDirection = NextDirection();
            return departDirection == 0 || toFloor == fromFloor || (int)Mathf.Sign(toFloor - fromFloor) == departDirection;
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
            if (direction != 0)
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

            // Someone new wants on here: re-open, like a real elevator, sliding back from wherever the doors had got to
            if (HasRequestHere(NextDirection()))
            {
                float openAmount = DoorOpenAmount(currentFloor);
                state = State.DoorsOpen;
                stoppedTimer = doorOpenDelay - Mathf.Min(doorSlideTime, doorOpenDelay) * (1f - openAmount);
                OpenDoors();
                return;
            }

            closingTimer += Time.fixedDeltaTime;
            if (closingTimer >= doorCloseDelay) state = State.Moving;
        }

        private void UpdateMoving()
        {
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

            // Doors are still opening: nobody steps in or out yet
            if (!DoorsPhysicallyOpen) return;
            doorTimer += Time.fixedDeltaTime;

            // Anyone getting off or on here is served now
            playerHallCalls.Remove(currentFloor);
            playerCarCalls.Remove(currentFloor);

            bool busy = boardingHere > 0 || leavingHere > 0;
            if (doorTimer < minDoorOpenTime || (busy && doorTimer < maxDoorOpenTime)) return;

            // If we're giving up on someone who never boarded, ignore this floor's hall calls until we've left it
            if (boardingHere > 0) skipHallCallsAtFloor = currentFloor;
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

        // ---------- Doors, collisions & helpers ----------

        // Start serving this floor. The door timer starts once the doors have physically opened.
        private void OpenDoors()
        {
            state = State.DoorsOpen;
            doorTimer = 0f;
        }

        private void StopAt(float y)
        {
            Body.linearVelocity = Vector2.zero;
            Body.position = new Vector2(Body.position.x, y);
            stoppedTimer = 0f;
        }

        // Keeps every character's collisions matching whether they're a rider or a walker. Only changes pairs that are wrong,
        // so it's cheap to run every step, and it also covers characters that spawned since the last step.
        private void UpdateCollisions()
        {
            foreach (var npc in NPCController.All) SetRiderCollisions(npc.BodyCollider, IsRiding(npc.BodyCollider));
            if (player != null) SetRiderCollisions(player.CapsuleCollider2D, IsPlayerRiding());
        }

        // Riders: solid against the whole car (floor, roof, walls), passing through the walkways. Walkers: the other way round.
        private void SetRiderCollisions(Collider2D character, bool riding)
        {
            if (character == null) return;
            foreach (var part in carParts) SetIgnored(part, character, !riding);
            foreach (var walkway in shaftFloors) SetIgnored(walkway, character, riding);
        }

        private static void SetIgnored(Collider2D a, Collider2D b, bool ignored)
        {
            if (a != null && b != null && Physics2D.GetIgnoreCollision(a, b) != ignored) Physics2D.IgnoreCollision(a, b, ignored);
        }

        // How far open this floor's doors are, 0 (shut) to 1 (fully apart). They slide open over the last part of the
        // opening delay (so they're fully open exactly when people can step in) and shut over the closing delay.
        private float DoorOpenAmount(int floor)
        {
            if (floor != currentFloor || state == State.Moving) return 0f;
            if (state == State.DoorsClosing) return 1f - Mathf.Clamp01(closingTimer / doorCloseDelay);
            float slide = Mathf.Min(doorSlideTime, doorOpenDelay);
            return slide <= 0f ? (DoorsPhysicallyOpen ? 1f : 0f) : Mathf.Clamp01((stoppedTimer - (doorOpenDelay - slide)) / slide);
        }

        // Slides each floor's door leaves apart by their own width when open, back together when shut
        private void UpdateFrontDoors()
        {
            for (int floor = 0; floor < floors.Count; floor++)
            {
                var wp = floors[floor];
                if (wp == null) continue;
                float open = DoorOpenAmount(floor);
                if (wp.leftDoor != null) wp.leftDoor.position = leftDoorClosed[floor] + Vector3.left * (doorLeafWidth[floor] * open);
                if (wp.rightDoor != null) wp.rightDoor.position = rightDoorClosed[floor] + Vector3.right * (doorLeafWidth[floor] * open);
            }
        }

        private void LateUpdate()
        {
            if (floors.Count > 0) UpdateFrontDoors(); // every frame, so the doors slide smoothly
        }

        // Stop nodes cost more the further away the elevator is, so NPCs take the stairs when it's far off.
        // Pathfinding only applies this to riding legs, so walking past the doors on the walkway costs nothing extra.
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
            sb.Append($"| riders {riders.Count}/{capacity} ");
            if (state == State.DoorsOpen) sb.Append($"| boarding {boardingHere} leaving {leavingHere}");
            sb.Append(state == State.DoorsClosing ? "| doors closing" : DoorsPhysicallyOpen ? "| doors open" : state == State.Moving ? "| doors closed" : "| doors opening");
            return sb.ToString();
        }
    }
}
