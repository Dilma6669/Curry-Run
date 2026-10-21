using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    // A toilet room the player is racing to reach. The player gets in by standing at the men's door in the room's art
    // and pushing up.
    public class Toilet : MonoBehaviour
    {
        [Tooltip("Centre of the men's door, relative to this object (the room, whose art is centred on it)")]
        public Vector2 doorCentre = new Vector2(-1.74f, -1.66f);
        [Tooltip("Size of the men's door. The player must be standing in front of it")]
        public Vector2 doorSize = new Vector2(2.8f, 5.45f);

        public static readonly List<Toilet> All = new List<Toilet>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public bool IsAtDoor(Vector2 point)
        {
            Vector2 offset = point - ((Vector2)transform.position + doorCentre);
            return Mathf.Abs(offset.x) <= doorSize.x * 0.5f && Mathf.Abs(offset.y) <= doorSize.y * 0.5f;
        }

        public static bool AnyAtDoor(Vector2 point)
        {
            foreach (var toilet in All)
            {
                if (toilet.IsAtDoor(point)) return true;
            }
            return false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.8f);
            Gizmos.DrawWireCube((Vector2)transform.position + doorCentre, doorSize);
        }
    }
}
