using UnityEngine;
using UnityEngine.Serialization;

namespace UltimateCC
{
    // Marks one floor an Elevator serves: this object's Y is the elevator platform's height at the floor,
    // stopNode is the PathNode on the walkway in front of the doors, and frontDoor is the door art the Elevator
    // shows while closed and hides while open.
    public class ElevatorWaypoint : MonoBehaviour
    {
        [Tooltip("The PathNode on the walkway in front of this floor's elevator doors")]
        [FormerlySerializedAs("pathNodeToResumeMovement")]
        public PathNode stopNode;

        [Tooltip("This floor's front door art (any SpriteRenderers under it are shown while the doors are closed)")]
        public GameObject frontDoor;

        public float Height => transform.position.y;
    }
}
