using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class NPCManager : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [SerializeField] private GameObject npcPrefab;
        [SerializeField, Range(0.1f, 10f)] private float spawnInterval = 1f;

        [Header("Path Assignment")]
        [Tooltip("Each NPC spawns at a random node from this list")]
        [SerializeField] private List<PathNode> startNodes = new List<PathNode>();
        [Tooltip("Each NPC walks to a random node from this list (never the one it spawned at, and only ones it can reach)")]
        [SerializeField] private List<PathNode> targetNodes = new List<PathNode>();

        private float spawnTimer;

        void Update()
        {
            if (npcPrefab == null) return;

            spawnTimer += Time.deltaTime;

            if (spawnTimer >= spawnInterval)
            {
                SpawnNPC();
                spawnTimer = 0f;
            }
        }

        private void SpawnNPC()
        {
            PathNode startNode = PickRandom(startNodes);
            if (startNode == null)
            {
                Debug.LogWarning("NPCManager: no start nodes assigned, can't spawn NPCs", this);
                return;
            }

            PathNode targetNode = PickReachableTarget(startNode);
            if (targetNode == null)
            {
                Debug.LogWarning($"NPCManager: no reachable target node from {startNode.name}, skipping this spawn", this);
                return;
            }

            GameObject newNPC = Instantiate(npcPrefab, startNode.transform.position, Quaternion.identity);

            // Ensure newly spawned NPC starts on the "NPC" layer
            int npcLayer = LayerMask.NameToLayer("NPC");
            if (npcLayer != -1)
            {
                newNPC.layer = npcLayer;
            }

            // Get the NPCController from the spawned clone and assign nodes
            NPCController controller = newNPC.GetComponent<NPCController>();
            if (controller != null)
            {
                controller.startNode = startNode;
                controller.targetNode = targetNode;

                // If the controller's Start method already ran before assignment,
                // we can explicitly call SetDestination to kick off pathfinding
                controller.SetDestination(startNode, targetNode);
            }
        }

        // Tries the target nodes in a random order and returns the first one (other than the start) with a path to it
        private PathNode PickReachableTarget(PathNode startNode)
        {
            List<PathNode> candidates = new List<PathNode>();
            foreach (var node in targetNodes)
            {
                if (node != null && node != startNode && !candidates.Contains(node))
                {
                    candidates.Add(node);
                }
            }

            while (candidates.Count > 0)
            {
                int index = Random.Range(0, candidates.Count);
                PathNode candidate = candidates[index];
                if (Pathfinding.FindPath(startNode, candidate).Count > 0)
                {
                    return candidate;
                }
                candidates.RemoveAt(index);
            }
            return null;
        }

        private static PathNode PickRandom(List<PathNode> nodes)
        {
            List<PathNode> valid = nodes.FindAll(n => n != null);
            return valid.Count > 0 ? valid[Random.Range(0, valid.Count)] : null;
        }
    }
}
