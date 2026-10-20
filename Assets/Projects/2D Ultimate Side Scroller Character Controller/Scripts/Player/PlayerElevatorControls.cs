using UnityEngine;

namespace UltimateCC
{
    // Lets the player use elevators through the same call system as the NPCs:
    // pressing F next to an elevator shaft calls the elevator to that floor (a hall call),
    // and pressing W/S while standing in it picks the floor to go to, one floor per press (a car call).
    [RequireComponent(typeof(PlayerMain))]
    public class PlayerElevatorControls : MonoBehaviour
    {
        [Tooltip("How far from the edge of the elevator shaft the player can be and still press the call button")]
        [SerializeField] private float callRange = 2.5f;

        private PlayerMain player;
        private bool wasCallPressed;
        private int lastVerticalInput;
        private Elevator ridingElevator;
        private int selectedFloor = -1;

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

            int vertical = input.Input_WallClimb > 0.5f ? 1 : input.Input_WallClimb < -0.5f ? -1 : 0;
            if (vertical != 0 && vertical != lastVerticalInput) TrySelectFloor(vertical);
            lastVerticalInput = vertical;

            // Forget our floor choice once we step off
            if (ridingElevator != null && !ridingElevator.IsPlayerRiding())
            {
                ridingElevator = null;
                selectedFloor = -1;
            }
        }

        // F pressed: call the elevator to the floor we're standing on, if we're beside its shaft
        private void TryCallElevator()
        {
            Bounds body = player.CapsuleCollider2D.bounds;
            foreach (var elevator in Elevator.All)
            {
                if (elevator.IsPlayerRiding()) continue; // inside the elevator W/S picks the floor instead

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
                        Debug.Log($"Elevator called to floor {floor}");
                        return;
                    }
                }
            }
        }

        // W/S pressed while riding: move our chosen destination up or down one floor
        private void TrySelectFloor(int direction)
        {
            foreach (var elevator in Elevator.All)
            {
                if (!elevator.IsPlayerRiding()) continue;

                if (ridingElevator != elevator)
                {
                    ridingElevator = elevator;
                    selectedFloor = elevator.CurrentFloor;
                }
                int next = Mathf.Clamp(selectedFloor + direction, 0, elevator.FloorCount - 1);
                if (next == selectedFloor) return;

                selectedFloor = next;
                elevator.SetPlayerDestination(selectedFloor);
                Debug.Log($"Elevator: going to floor {selectedFloor}");
                return;
            }
        }
    }
}
