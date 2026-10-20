using UnityEngine;
using UnityEngine.Serialization;

namespace UltimateCC
{
    // Marks one floor an Elevator serves: this object's Y is the elevator platform's height at the floor,
    // and stopNode is the PathNode in the shaft that NPCs walk to when boarding or leaving here.
    // The Elevator opens a floor by unpausing its stop node, and closes it by pausing it again.
    public class ElevatorWaypoint : MonoBehaviour
    {
        [Tooltip("The PathNode in the elevator shaft at this floor")]
        [FormerlySerializedAs("pathNodeToResumeMovement")]
        public PathNode stopNode;

        public float Height => transform.position.y;
    }
}
