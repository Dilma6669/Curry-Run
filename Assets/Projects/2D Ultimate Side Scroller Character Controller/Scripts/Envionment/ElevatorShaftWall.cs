using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    // The solid wall in front of the elevator shaft (doorway holes are cut with SpriteMasks). It fades to see-through
    // while the player rides, so the player can see themselves inside the car, and back to solid when they step out.
    public class ElevatorShaftWall : MonoBehaviour
    {
        [SerializeField] private Elevator elevator;
        [Tooltip("The wall sprites to fade")]
        [SerializeField] private List<SpriteRenderer> walls = new List<SpriteRenderer>();

        [Tooltip("Wall opacity while the player is in the elevator")]
        [Range(0f, 1f)] [SerializeField] private float playerRidingAlpha = 0.5f;
        [Tooltip("How fast the wall fades, in opacity per second")]
        [SerializeField] private float fadeSpeed = 3f;

        private float alpha = 1f;

        private void LateUpdate()
        {
            if (elevator == null) return;

            float target = elevator.IsPlayerRiding() ? playerRidingAlpha : 1f;
            if (Mathf.Approximately(alpha, target)) return;

            alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
            foreach (var wall in walls)
            {
                if (wall == null) continue;
                Color color = wall.color;
                color.a = alpha;
                wall.color = color;
            }
        }
    }
}
