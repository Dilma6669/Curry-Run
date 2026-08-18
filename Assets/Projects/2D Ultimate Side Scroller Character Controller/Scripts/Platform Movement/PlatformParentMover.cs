using System.Collections;
using UnityEngine;

namespace UltimateCC
{
    public class PlatformParentMover : MonoBehaviour
    {
        public enum Axis { Horizontal, Vertical }
        [SerializeField] private Axis movementAxis = Axis.Vertical;
        [SerializeField] private float distance = 5f; // Total distance to move before reversing/pausing
        [SerializeField] private float speed = 3f;
        [SerializeField] private float pauseDuration = 1f;
        [SerializeField] private float stopInterval = 2.5f; // Pause every X units (set to 0 or >= distance to disable regular intervals)
        [SerializeField, Range(-1, 1)] private int direction = 1;

        private Vector3 startPosition;

        void Start()
        {
            startPosition = transform.position;
            if (direction == 0) direction = 1;

            StartCoroutine(MoveRoutine());
        }

        private IEnumerator MoveRoutine()
        {
            float targetDistance = distance;
            float nextIntervalDistance = stopInterval;

            while (true)
            {
                Vector3 targetPosition;
                if (movementAxis == Axis.Vertical)
                {
                    targetPosition = startPosition + new Vector3(0, direction * targetDistance, 0);
                }
                else
                {
                    targetPosition = startPosition + new Vector3(direction * targetDistance, 0, 0);
                }

                // Move toward the target
                while (Vector3.Distance(transform.position, targetPosition) > 0.001f)
                {
                    // Check if we hit an intermediate stop interval
                    float traveled = Vector3.Distance(startPosition, transform.position);
                    if (stopInterval > 0f && traveled >= nextIntervalDistance && traveled < targetDistance - 0.1f)
                    {
                        // Snap slightly or pause
                        yield return new WaitForSeconds(pauseDuration);
                        nextIntervalDistance += stopInterval;
                    }

                    transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);
                    yield return null;
                }

                // Pause at the end point of the travel distance
                yield return new WaitForSeconds(pauseDuration);

                // Reverse direction and reset tracking
                direction *= -1;
                startPosition = transform.position;
                nextIntervalDistance = stopInterval;
            }
        }
    }
}