using UnityEngine;

namespace UltimateCC
{
    // Lets the player use elevators through the same system as the NPCs, via each floor's front doors:
    // F in front of the doors calls the elevator to that floor (a hall call), W steps in once the doors are open,
    // number keys (0-9, top row or numpad) inside request floors (car calls, queued like any rider's - they don't jump
    // the elevator's existing route), and S steps back out onto the walkway when the doors are open.
    [RequireComponent(typeof(PlayerMain))]
    public class PlayerElevatorControls : MonoBehaviour
    {
        [Tooltip("How far from the middle of an elevator's front doors the player can be and still press the call button")]
        [SerializeField] private float callRange = 3f;

        private PlayerMain player;
        private bool wasCallPressed;
        private int lastVerticalInput;

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
            if (vertical != lastVerticalInput)
            {
                if (vertical > 0) TryStepIn();
                else if (vertical < 0) TryStepOut();
            }
            lastVerticalInput = vertical;

            int keypadFloor = input.TakeElevatorFloorPress();
            if (keypadFloor >= 0) TryRequestFloor(keypadFloor);
        }

        // F pressed: call the elevator to the floor we're standing on, if we're in front of its doors
        private void TryCallElevator()
        {
            Bounds body = player.CapsuleCollider2D.bounds;
            foreach (var elevator in Elevator.All)
            {
                if (elevator.IsPlayerRiding() || Mathf.Abs(body.center.x - elevator.DoorX) > callRange) continue;

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

        // W pressed at open front doors: step into the car
        private void TryStepIn()
        {
            foreach (var elevator in Elevator.All)
            {
                if (elevator.IsPlayerRiding() || !elevator.IsAtFrontDoor(player.CapsuleCollider2D, elevator.CurrentFloor)) continue;
                if (elevator.TryEnter(player.CapsuleCollider2D)) Debug.Log("Stepped into the elevator");
                else if (!elevator.HasRoom) Debug.Log("The elevator is full");
                return;
            }
        }

        // S pressed inside the car: step out onto the walkway once the doors are open
        private void TryStepOut()
        {
            foreach (var elevator in Elevator.All)
            {
                if (!elevator.IsPlayerRiding()) continue;
                if (elevator.TryExit(player.CapsuleCollider2D)) Debug.Log("Stepped out of the elevator");
                return;
            }
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
