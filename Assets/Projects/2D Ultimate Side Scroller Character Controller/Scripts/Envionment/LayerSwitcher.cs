using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class LayerSwitcher : MonoBehaviour
    {
        // A list of all NPCs currently touching/standing on this platform/ramp
        public List<Collider2D> currentNPCColliders = new List<Collider2D>();

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Collider2D other = collision.collider;

            // Check if it's an NPC (assuming your NPCs have an "NPC" tag or script)
            if (other.CompareTag("NPC") || other.GetComponent<NPCController>() != null)
            {
                if (!currentNPCColliders.Contains(other))
                {
                    currentNPCColliders.Add(other);
                }
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            Collider2D other = collision.collider;

            if (other.CompareTag("NPC") || other.GetComponent<NPCController>() != null)
            {
                currentNPCColliders.Remove(other);
            }
        }
    }
}