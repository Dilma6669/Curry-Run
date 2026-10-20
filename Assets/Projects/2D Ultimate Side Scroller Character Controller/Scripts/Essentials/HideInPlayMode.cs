using UnityEngine;

namespace UltimateCC
{
    // Hides this object's sprites while the game is running, keeping them visible in the Editor.
    // Put it on editor-only markers (room centres, debug shapes) that shouldn't show in the game.
    public class HideInPlayMode : MonoBehaviour
    {
        // Awake only runs in Play mode
        private void Awake()
        {
            foreach (var sprite in GetComponents<SpriteRenderer>()) sprite.enabled = false;
        }
    }
}
