using System.Collections;
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
        [SerializeField] private float baseCost = 1f;          // Cost when elevator is right here
        [SerializeField] private float maxCost = 50f;          // Cost when elevator is far away
        [SerializeField] private float distanceScaleFactor = 1f; // How fast cost increases with distance

        private Coroutine costUpdateCoroutine;

        private void Start()
        {
            if (elevatorCollider != null && pathNodeToResumeMovement != null)
            {
                costUpdateCoroutine = StartCoroutine(UpdateElevatorCostRoutine());
            }
        }

        private void OnDestroy()
        {
            if (costUpdateCoroutine != null)
            {
                StopCoroutine(costUpdateCoroutine);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (elevatorCollider != null && other == elevatorCollider)
            {
                hasArrived = true;

                if (pathNodeToResumeMovement != null)
                {
                    pathNodeToResumeMovement.pauseMovement = false;
                    pathNodeToResumeMovement.customCost = baseCost; // Reset cost when arrived
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

        private IEnumerator UpdateElevatorCostRoutine()
        {
            WaitForSeconds waitInterval = new WaitForSeconds(1f);

            while (true)
            {
                if (elevatorCollider != null && pathNodeToResumeMovement != null)
                {
                    // Only adjust dynamic cost if the elevator isn't currently sitting at this waypoint
                    if (!hasArrived)
                    {
                        float distance = Vector2.Distance(transform.position, elevatorCollider.transform.position);
                        
                        // Calculate a dynamic cost that increases the further away the elevator is
                        float calculatedCost = baseCost + (distance * distanceScaleFactor);
                        pathNodeToResumeMovement.customCost = Mathf.Min(calculatedCost, maxCost);
                    }
                }

                yield return waitInterval;
            }
        }
    }
}