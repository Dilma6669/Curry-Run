using UnityEngine;

namespace UltimateCC
{
    // The little display above an elevator's doors showing which floor the car is at, counting floors as it passes
    // them, with an arrow for the direction it's travelling.
    public class ElevatorFloorDisplay : MonoBehaviour
    {
        [SerializeField] private Elevator elevator;
        [SerializeField] private TextMesh label;
        [Tooltip("What the bottom floor is called; the floors above are numbered 1, 2, 3...")]
        [SerializeField] private string groundFloorName = "G";

        private string shown;

        private void LateUpdate()
        {
            if (elevator == null || label == null) return;

            int floor = elevator.NearestFloor;
            string text = floor == 0 ? groundFloorName : floor.ToString();
            if (elevator.state == Elevator.State.Moving)
            {
                text = (elevator.direction > 0 ? "↑" : "↓") + text;
            }

            if (text != shown)
            {
                shown = text;
                label.text = text;
            }
        }
    }
}
