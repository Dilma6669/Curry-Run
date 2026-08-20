using UnityEngine;

namespace UltimateCC
{
    public class LayerSwitcher : MonoBehaviour
    {
        [Header("Layer Settings")]
        [SerializeField] private string playerIgnoreLayerName = "PlayerIgnore";
        [SerializeField] private string npcIgnoreLayerName = "NPCIgnore";

        private int defaultLayer;
        private int playerIgnoreLayer;
        private int npcIgnoreLayer;

        void Awake()
        {
            defaultLayer = gameObject.layer;
            playerIgnoreLayer = LayerMask.NameToLayer(playerIgnoreLayerName);
            npcIgnoreLayer = LayerMask.NameToLayer(npcIgnoreLayerName);
        }

        public void SetPlayerIgnoreLayer()
        {
            if (playerIgnoreLayer != -1)
            {
                gameObject.layer = playerIgnoreLayer;
            }
            else
            {
                Debug.LogWarning($"Layer '{playerIgnoreLayerName}' does not exist in the project settings!", this);
            }
        }

        public void SetNpcIgnoreLayer()
        {
            if (npcIgnoreLayer != -1)
            {
                gameObject.layer = npcIgnoreLayer;
            }
            else
            {
                Debug.LogWarning($"Layer '{npcIgnoreLayerName}' does not exist in the project settings!", this);
            }
        }

        public void RevertToDefaultLayer()
        {
            gameObject.layer = defaultLayer;
        }
    }
}