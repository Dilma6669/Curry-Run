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

        [Header("Input")]
        [Tooltip("When ticked, pressing up (W) inside this gate won't switch to the Up rules")]
        [SerializeField] private bool ignoreUpInput = false;
        [Tooltip("When ticked, pressing down (S) inside this gate won't switch to the Down rules")]
        [SerializeField] private bool ignoreDownInput = false;

        private PlayerMain player;
        private bool playerEnteredTrigger;
        
        // Track the state from the previous frame to detect changes
        private PlayerMain.PlayerMovementState lastPlayerState = (PlayerMain.PlayerMovementState)(-1);

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerMain detectedPlayer = GetPlayer(other);
            if (detectedPlayer != null)
            {
                ActivateFor(detectedPlayer);
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // Covers starting inside a gate, or still being inside this gate after leaving an overlapping one
            PlayerMain detectedPlayer = GetPlayer(other);
            if (detectedPlayer != null && detectedPlayer.activeRampGate == null)
            {
                ActivateFor(detectedPlayer);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerMain detectedPlayer = GetPlayer(other);
            if (detectedPlayer != null && detectedPlayer == player)
            {
                playerEnteredTrigger = false;
                lastPlayerState = (PlayerMain.PlayerMovementState)(-1); // Reset state tracker on exit

                if (player.activeRampGate == this)
                {
                    player.activeRampGate = null;
                }
            }
        }

        private PlayerMain GetPlayer(Collider2D other)
        {
            PlayerMain detectedPlayer = other.GetComponent<PlayerMain>();
            return detectedPlayer != null && detectedPlayer.gameObject.CompareTag("Player") ? detectedPlayer : null;
        }

        // Makes this the gate in control of the player. Only resets to Straight when first entering,
        // so an Up/Down choice persists after the key is released until the player enters another gate.
        private void ActivateFor(PlayerMain detectedPlayer)
        {
            player = detectedPlayer;
            playerEnteredTrigger = true;
            player.activeRampGate = this;

            player.currentMovementState = PlayerMain.PlayerMovementState.Straight;
            ApplyCurrentStateRules();
        }

        private void FixedUpdate()
        {
            // Only the most recently entered gate reads input, so overlapping gates don't fight over the player
            if (!playerEnteredTrigger || player == null || player.activeRampGate != this) return;

            float verticalInput = player.InputManager.Input_WallClimb;

            // If player pushes up or down while inside this trigger, update persistent player state
            if (verticalInput > 0 && !ignoreUpInput)
            {
                player.currentMovementState = PlayerMain.PlayerMovementState.Up;
            }
            else if (verticalInput < 0 && !ignoreDownInput)
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

                        // NPCs on the platform aren't added here: MainState already makes the player ignore all nearby NPCs,
                        // and putting them in this list caused their collision to be re-enabled when the gate rules changed
                    }
                }
            }
            return colliders;
        }
    }
}