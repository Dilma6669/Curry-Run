using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    // A room NPCs visit to eat, smoke or take a break, e.g. the cafeteria or smokers' area. NPCManager picks these as
    // destinations; an NPC arriving at the node settles at a spot in the room and rests there, untouchable by the player
    // and other NPCs, for a random stay before heading somewhere new.
    public class RestArea : MonoBehaviour
    {
        [Tooltip("The PathNode in this room NPCs walk to")]
        public PathNode node;

        [Header("Stay")]
        [Tooltip("Each visit lasts a random time between min and max, in seconds")]
        public float minStayTime = 15f;
        public float maxStayTime = 25f;

        [Tooltip("Resting NPCs spread out up to this far either side of this object (the room art's centre), so groups don't stand in one spot")]
        public float spread = 3.5f;

        [Tooltip("Sorting order for resting NPCs: behind passers-by (0) but in front of this room's art")]
        public int restingSortingOrder = -1;

        [Tooltip("Resting NPCs' bodies are tinted this colour, so it's clear they're out of the way")]
        public Color restingColor = Color.yellow;

        public static readonly List<RestArea> All = new List<RestArea>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public float RollStayTime() => Random.Range(minStayTime, maxStayTime);

        // A spot in the room for one NPC to settle at, centred on the art rather than the node (which may sit near a
        // doorway). The first NPC in picks anywhere; later ones take the spot furthest from everyone already resting
        // here (checked across the room, with a little jitter), so a group spreads out instead of bunching up.
        public float RollSpotX()
        {
            float centre = transform.position.x;
            bool anyoneHere = NPCController.All.Exists(npc => npc.CurrentRestArea == this);
            if (!anyoneHere) return centre + Random.Range(-spread, spread);

            const int Steps = 24;
            float step = 2f * spread / Steps;
            float best = centre;
            float bestGap = -1f;
            for (int i = 0; i <= Steps; i++)
            {
                float candidate = Mathf.Clamp(centre - spread + i * step + Random.Range(-0.25f, 0.25f) * step, centre - spread, centre + spread);
                float gap = float.MaxValue;
                foreach (var npc in NPCController.All)
                {
                    if (npc.CurrentRestArea == this) gap = Mathf.Min(gap, Mathf.Abs(npc.RestSpotX - candidate));
                }
                if (gap > bestGap)
                {
                    best = candidate;
                    bestGap = gap;
                }
            }
            return best;
        }

        public static RestArea ForNode(PathNode node)
        {
            if (node == null) return null;
            foreach (var area in All)
            {
                if (area.node == node) return area;
            }
            return null;
        }
    }
}
