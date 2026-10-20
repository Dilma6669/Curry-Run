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
        public float spread = 3f;

        [Tooltip("Sorting order for resting NPCs: behind passers-by (0) but in front of this room's art")]
        public int restingSortingOrder = -1;

        [Tooltip("Resting NPCs' bodies are tinted this colour, so it's clear they're out of the way")]
        public Color restingColor = Color.yellow;

        public static readonly List<RestArea> All = new List<RestArea>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public float RollStayTime() => Random.Range(minStayTime, maxStayTime);

        // A spot in the room for one NPC to settle at. Centred on the art rather than the node, which may sit near a doorway.
        public float RollSpotX() => transform.position.x + Random.Range(-spread, spread);

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
