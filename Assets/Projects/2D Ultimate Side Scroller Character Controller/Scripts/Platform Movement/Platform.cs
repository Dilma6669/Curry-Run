using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class Platform : MonoBehaviour
    {
        [Header("Vertical Floor Settings")]
        [SerializeField] private List<float> floorHeights = new List<float> { -5f, 5f, 15f, 25f, 35f, 45f };
        [SerializeField] private int startFloorIndex = 0;
        [SerializeField] private float pauseDuration = 3f;
        [SerializeField] private float speed = 5f;

        private Rigidbody2D rb;
        private float pauseTimer;
        
        private int currentFloorIndex = 0;
        private int targetFloorIndex = 0;
        private int direction = 1; // 1 = Up, -1 = Down

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();

            if (floorHeights != null && floorHeights.Count > 0)
            {
                startFloorIndex = Mathf.Clamp(startFloorIndex, 0, floorHeights.Count - 1);
                currentFloorIndex = startFloorIndex;
                targetFloorIndex = startFloorIndex;

                // Snap directly to the starting floor Y coordinate on launch
                Vector2 pos = rb.position;
                pos.y = floorHeights[startFloorIndex];
                rb.position = pos;
                transform.position = pos;

                // Set initial target to the next floor up
                SetNextTarget();
            }
        }

        void FixedUpdate()
        {
            if (floorHeights == null || floorHeights.Count == 0) return;

            // 1. Handle pause timer when arriving at a floor
            if (pauseTimer > 0f)
            {
                pauseTimer -= Time.fixedDeltaTime;
                rb.linearVelocity = Vector2.zero;
                return;
            }

            // 2. Move toward the current target floor
            float targetY = floorHeights[targetFloorIndex];
            float currentY = rb.position.y;

            // Check if we have reached the target floor
            if (Mathf.Abs(currentY - targetY) < 0.05f)
            {
                // Snap to exact position
                Vector2 pos = rb.position;
                pos.y = targetY;
                rb.position = pos;
                rb.linearVelocity = Vector2.zero;

                // Update current floor tracker
                currentFloorIndex = targetFloorIndex;

                // Start pause timer
                pauseTimer = pauseDuration;

                // Pick the next floor to go to
                SetNextTarget();
                return;
            }

            // Keep moving in the current travel direction
            float moveDir = targetY > currentY ? 1f : -1f;
            rb.linearVelocity = new Vector2(0, moveDir * speed);
        }

        private void SetNextTarget()
        {
            // If we hit the top floor, reverse direction to go down
            if (targetFloorIndex >= floorHeights.Count - 1)
            {
                direction = -1;
            }
            // If we hit the bottom floor, reverse direction to go up
            else if (targetFloorIndex <= 0)
            {
                direction = 1;
            }

            targetFloorIndex += direction;
            targetFloorIndex = Mathf.Clamp(targetFloorIndex, 0, floorHeights.Count - 1);
        }

        // Optional: Call this if you ever want to force it to a specific floor immediately
        public void MoveToFloor(int floorIndex)
        {
            if (floorHeights != null && floorIndex >= 0 && floorIndex < floorHeights.Count)
            {
                targetFloorIndex = floorIndex;
                pauseTimer = 0f;
            }
        }
    }
}