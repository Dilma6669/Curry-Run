using UnityEngine;

namespace UltimateCC
{
    public class NPCManager : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [SerializeField] private GameObject npcPrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField, Range(0.1f, 10f)] private float spawnInterval = 1f;

        [Header("Path Assignment")]
        [SerializeField] private PathNode startNode;
        [SerializeField] private PathNode targetNode;

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
            Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : transform.position;
            GameObject newNPC = Instantiate(npcPrefab, spawnPosition, Quaternion.identity);

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
    }
}