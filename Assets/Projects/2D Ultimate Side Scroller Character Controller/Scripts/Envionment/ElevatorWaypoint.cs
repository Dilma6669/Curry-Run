using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class ElevatorWaypoint : MonoBehaviour
    {
        [Header("Status")]
        public bool hasArrived = false;
        
        public PathNode pathNodeToResumeMovement;

        [Header("Settings")]
        [SerializeField] private Collider2D elevatorCollider;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (elevatorCollider != null && other == elevatorCollider)
            {
                hasArrived = true;

                if (pathNodeToResumeMovement != null)
                {
                    pathNodeToResumeMovement.pauseMovement = false;
                }

            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (elevatorCollider != null && other == elevatorCollider)
            {
                hasArrived = false;

                if (pathNodeToResumeMovement != null)
                {
                    pathNodeToResumeMovement.pauseMovement = true;
                }
            }
        }
    }
}