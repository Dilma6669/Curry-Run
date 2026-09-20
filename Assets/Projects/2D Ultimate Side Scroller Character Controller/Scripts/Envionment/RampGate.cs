using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class RampGate : MonoBehaviour
    {
        [System.Serializable]
        public class StateRuleConfig
        {
            public List<Collider2D> ignorePlatforms = new List<Collider2D>();
        }

        public enum GateState { Straight, Up, Down }

        [Header("Configurations")]
        [SerializeField] private StateRuleConfig straightState;
        [SerializeField] private StateRuleConfig upState;
        [SerializeField] private StateRuleConfig downState;

        private PlayerMain player;
        private bool playerEnteredTrigger;
        
        // Track the state from the previous frame to detect changes
        private PlayerMain.PlayerMovementState lastPlayerState = (PlayerMain.PlayerMovementState)(-1);

        private void OnTriggerStay2D(Collider2D other)
        {
            PlayerMain detectedPlayer = other.GetComponent<PlayerMain>();
            if (detectedPlayer != null && detectedPlayer.gameObject.CompareTag("Player"))
            {
                player = detectedPlayer;
                playerEnteredTrigger = true;
                
                // When entering a NEW trigger, reset the player's state back to Straight
                player.currentMovementState = PlayerMain.PlayerMovementState.Straight;
                
                ApplyCurrentStateRules();
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerMain detectedPlayer = other.GetComponent<PlayerMain>();
            if (detectedPlayer != null && detectedPlayer.gameObject.CompareTag("Player") && detectedPlayer == player)
            {
                playerEnteredTrigger = false;
                lastPlayerState = (PlayerMain.PlayerMovementState)(-1); // Reset state tracker on exit
            }
        }

        private void FixedUpdate()
        {
            if (!playerEnteredTrigger || player == null) return;

            float verticalInput = player.InputManager.Input_WallClimb;

            // If player pushes up or down while inside this trigger, update persistent player state
            if (verticalInput > 0)
            {
                player.currentMovementState = PlayerMain.PlayerMovementState.Up;
            }
            else if (verticalInput < 0)
            {
                player.currentMovementState = PlayerMain.PlayerMovementState.Down;
            }

            // ONLY apply rules if the player's movement state has actually changed!
            if (player.currentMovementState != lastPlayerState)
            {
                ApplyCurrentStateRules();
            }
        }

        private void ApplyCurrentStateRules()
        {
            lastPlayerState = player.currentMovementState;

            StateRuleConfig activeConfig = straightState;

            if (player.currentMovementState == PlayerMain.PlayerMovementState.Up)
                activeConfig = upState;
            else if (player.currentMovementState == PlayerMain.PlayerMovementState.Down)
                activeConfig = downState;

            if (player != null)
            {
                List<Collider2D> colliders = CollectCollidersForConfig(activeConfig);
                player.SetIgnoredColliders(colliders);
            }
        }

        private List<Collider2D> CollectCollidersForConfig(StateRuleConfig config)
        {
            List<Collider2D> colliders = new List<Collider2D>();
            if (config != null && config.ignorePlatforms != null)
            {
                foreach (var platformObj in config.ignorePlatforms)
                {
                    if (platformObj != null)
                    {
                        Collider2D[] childCols = platformObj.GetComponentsInChildren<Collider2D>(true);
                        foreach (var col in childCols)
                        {
                            if (col != null && !colliders.Contains(col))
                            {
                                colliders.Add(col);
                            }
                        }

                        SurfaceTracker tracker = platformObj.GetComponent<SurfaceTracker>();
                        if (tracker != null && tracker.currentNPCColliders != null)
                        {
                            foreach (var npcCol in tracker.currentNPCColliders)
                            {
                                if (npcCol != null && !colliders.Contains(npcCol))
                                {
                                    colliders.Add(npcCol);
                                }
                            }
                        }
                    }
                }
            }
            return colliders;
        }
    }
}