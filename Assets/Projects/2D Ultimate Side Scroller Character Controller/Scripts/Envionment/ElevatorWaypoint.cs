using UnityEngine;
using UnityEngine.Serialization;

namespace UltimateCC
{
    // Marks one floor an Elevator serves: this object's Y is the elevator platform's height at the floor,
    // stopNode is the PathNode on the walkway in front of the doors, and leftDoor/rightDoor are the sliding door
    // leaves the Elevator animates open (apart) and shut at this floor.
    public class ElevatorWaypoint : MonoBehaviour
    {
        [Tooltip("The PathNode on the walkway in front of this floor's elevator doors")]
        [FormerlySerializedAs("pathNodeToResumeMovement")]
        public PathNode stopNode;

        [Tooltip("Parent object of this floor's door art")]
        public GameObject frontDoor;

        [Tooltip("Sliding door leaves, positioned closed. They slide outwards by their own width to open")]
        public Transform leftDoor;
        public Transform rightDoor;

        public float Height => transform.position.y;
    }
}
