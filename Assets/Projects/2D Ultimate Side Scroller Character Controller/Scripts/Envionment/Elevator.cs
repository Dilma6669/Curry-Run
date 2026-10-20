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
        private int boardingHere, leavingHere;
        private readonly List<int> waitingHere = new List<int>(); // destinations of NPCs waiting at the current floor
        private int targetFloor;
        private float doorTimer, costTimer;
        private int skipHallCallsAtFloor = -1;   // set when doors time out on someone who never boarded, until we leave the floor

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

        // An NPC waiting here may only walk in if the doors are open and we're heading its way (or have nowhere to go yet)
        public bool CanBoard(int fromFloor, int toFloor)
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
            waitingHere.Clear();

            foreach (var npc in NPCController.All)
            {
                bool onBoard = npc.IsStandingOn(this);
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
                        if (fromFloor == currentFloor) waitingHere.Add(toFloor);
                    }
                }
                else if (onBoard)
                {
                    leavingHere++; // walking off the platform
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

            // Second pass, now all calls are known: who waiting at this floor is allowed to get on
            foreach (int toFloor in waitingHere)
            {
                if (CanBoard(currentFloor, toFloor)) boardingHere++;
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
            if (direction != 0)
            {
                targetFloor = currentFloor + direction;
                state = State.Moving;
            }
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
            if (direction > 0 ? upCalls[floor] : downCalls[floor]) return true;
            // Turnaround point: nothing further this way, so pick up people going the other way too
            return !HasRequestBeyond(floor, direction) && (upCalls[floor] || downCalls[floor]);
        }

        // Is there a reason to open the doors at the current floor before setting off in departDirection?
        private bool HasRequestHere(int departDirection)
        {
            if (carCalls[currentFloor]) return true;
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
            if (state == State.DoorsOpen) sb.Append($"| boarding {boardingHere} leaving {leavingHere}");
            return sb.ToString();
        }
    }
}
