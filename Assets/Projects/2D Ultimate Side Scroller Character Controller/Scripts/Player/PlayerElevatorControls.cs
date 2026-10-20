using UnityEngine;

namespace UltimateCC
{
    // Lets the player use elevators through the same call system as the NPCs:
    // pressing F next to an elevator shaft calls the elevator to that floor (a hall call),
    // pressing a number key (0-9, top row or numpad) while standing in it requests that floor (a car call, queued
    // like any rider's - it doesn't jump the elevator's existing route),
    // and mashing F at a floor prises its landing doors open even with no elevator there.
    [RequireComponent(typeof(PlayerMain))]
    public class PlayerElevatorControls : MonoBehaviour
    {
        [Tooltip("How far from the edge of the elevator shaft the player can be and still press the call button")]
        [SerializeField] private float callRange = 2.5f;

        [Header("Forcing The Doors")]
        [Tooltip("This many F presses within the window below prises the floor's elevator doors open")]
        [SerializeField, Min(2)] private int pressesToForceDoors = 5;
        [SerializeField] private float forcePressWindow = 2f;
        [Tooltip("How long forced doors stay open (they won't close on anyone standing in the doorway)")]
        [SerializeField] private float forcedOpenTime = 4f;

        private PlayerMain player;
        private readonly System.Collections.Generic.Queue<float> recentPresses = new System.Collections.Generic.Queue<float>();
        private bool wasCallPressed;

        private void Awake()
        {
            player = GetComponent<PlayerMain>();
        }

        private void Update()
        {
            PlayerInputManager input = player.InputManager;
            if (input == null) return;

            bool callPressed = input.Input_ElevatorCall;
            if (callPressed && !wasCallPressed) TryCallElevator();
            wasCallPressed = callPressed;

            int keypadFloor = input.TakeElevatorFloorPress();
            if (keypadFloor >= 0) TryRequestFloor(keypadFloor);
        }

        // F pressed: call the elevator to the floor we're standing on, if we're beside its shaft
        private void TryCallElevator()
        {
            Bounds body = player.CapsuleCollider2D.bounds;
            foreach (var elevator in Elevator.All)
            {
                if (elevator.IsPlayerRiding()) continue; // inside the elevator the keypad picks the floor instead

                Bounds shaft = elevator.PlatformBounds;
                float distanceToShaft = body.max.x < shaft.min.x ? shaft.min.x - body.max.x
                                      : body.min.x > shaft.max.x ? body.min.x - shaft.max.x
                                      : 0f;
                if (distanceToShaft > callRange) continue;

                for (int floor = 0; floor < elevator.FloorCount; floor++)
                {
                    if (Mathf.Abs(body.min.y - elevator.FloorSurfaceY(floor)) < 1f)
                    {
                        elevator.CallToFloor(floor);
                        if (RegisterPressAndCheckMashing() && elevator.ForceOpenLandingDoors(floor, forcedOpenTime))
                        {
                            Debug.Log($"Prised the elevator doors open on floor {floor}!");
                        }
                        else
                        {
                            Debug.Log($"Elevator called to floor {floor}");
                        }
                        return;
                    }
                }
            }
        }

        // Records an F press; true once enough presses have landed inside the time window
        private bool RegisterPressAndCheckMashing()
        {
            float now = Time.time;
            recentPresses.Enqueue(now);
            while (recentPresses.Count > 0 && now - recentPresses.Peek() > forcePressWindow) recentPresses.Dequeue();
            if (recentPresses.Count < pressesToForceDoors) return false;
            recentPresses.Clear();
            return true;
        }

        // Number key pressed while riding: light up that floor's button
        private void TryRequestFloor(int floor)
        {
            foreach (var elevator in Elevator.All)
            {
                if (!elevator.IsPlayerRiding()) continue;

                if (elevator.AddPlayerDestination(floor)) Debug.Log($"Elevator: floor {floor} requested");
                else Debug.Log($"Elevator: there's no floor {floor} (floors 0-{elevator.FloorCount - 1})");
                return;
            }
        }
    }
}
