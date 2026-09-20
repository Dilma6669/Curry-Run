using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class RampGate : MonoBehaviour
    {
        public LayerSwitcher TriggerPlatform;
        public LayerSwitcher BottomRamp;
        public LayerSwitcher BottomPlatform;
        public LayerSwitcher TopRamp;
        public LayerSwitcher TopPlatform;
        public LayerSwitcher SecondTopRamp;

        public PlayerMain player;
        
        private bool playerEnteredTrigger;
        
        private List<Collider2D> ignoredColliders = new List<Collider2D>();
        
        public enum GateState { Straight, Up, Down }
        public GateState currentState = GateState.Straight;

        public void ResetAllPlayerIgnoredColliders()
        {
            if (player != null)
            {
                if (player.CapsuleCollider2D != null)
                {
                    foreach (var col in ignoredColliders)
                    {
                        if (col != null)
                        {
                            Physics2D.IgnoreCollision(player.CapsuleCollider2D, col, false);
                        }
                    }
                }
            }
            ignoredColliders.Clear();
            
            currentState = GateState.Straight;
        }

        private void TrackAndIgnore(LayerSwitcher targetPlatform, bool ignore)
        {
            if (targetPlatform == null || player == null || player.CapsuleCollider2D == null) return;

            Collider2D targetCol = targetPlatform.GetComponent<Collider2D>();
            if (targetCol != null)
            {
                // 1. Ignore the platform/ramp itself
                Physics2D.IgnoreCollision(player.CapsuleCollider2D, targetCol, ignore);

                if (ignore)
                {
                    if (!ignoredColliders.Contains(targetCol)) ignoredColliders.Add(targetCol);
                }
                else
                {
                    ignoredColliders.Remove(targetCol);
                }
            }

            // 2. Ignore any NPCs currently standing on this platform/ramp!
            if (targetPlatform.currentNPCColliders != null)
            {
                foreach (var npcCol in targetPlatform.currentNPCColliders)
                {
                    if (npcCol != null)
                    {
                        Physics2D.IgnoreCollision(player.CapsuleCollider2D, npcCol, ignore);

                        if (ignore)
                        {
                            if (!ignoredColliders.Contains(npcCol)) ignoredColliders.Add(npcCol);
                        }
                        else
                        {
                            ignoredColliders.Remove(npcCol);
                        }
                    }
                }
            }
        }
        
        private void FixedUpdate()
        {
            if (!playerEnteredTrigger || player == null) return;

            float verticalInput = player.InputManager.Input_WallClimb;
            GateState desiredState = currentState;

            // 1. Determine desired state from input
            if (verticalInput > 0)
            {
                desiredState = GateState.Up;
            }
            else if (verticalInput < 0)
            {
                desiredState = GateState.Down;
            }
            else
            {
                desiredState = GateState.Straight;
            }

            // 2. Switch states if input changed, BUT also continuously enforce rules 
            // so late-arriving NPCs on ignored platforms are caught!
            if (desiredState != currentState)
            {
                currentState = desiredState;
            }

            // Always apply rules every frame so newly added NPCs to these platforms are immediately ignored
            ApplyCurrentStateRules();
        }

        private void ApplyCurrentStateRules()
        {
            switch (currentState)
            {
                case GateState.Straight:
                    TrackAndIgnore(TriggerPlatform, false);
                    TrackAndIgnore(BottomRamp, false);
                    TrackAndIgnore(BottomPlatform, false);
                    TrackAndIgnore(TopRamp, true);
                    TrackAndIgnore(TopPlatform, true);
                    TrackAndIgnore(SecondTopRamp, true);
                    break;

                case GateState.Down:
                    TrackAndIgnore(TriggerPlatform, true);
                    TrackAndIgnore(BottomRamp, false);
                    TrackAndIgnore(BottomPlatform, false);
                    TrackAndIgnore(TopRamp, true);
                    TrackAndIgnore(TopPlatform, true);
                    TrackAndIgnore(SecondTopRamp, true);
                    break;

                case GateState.Up:
                    TrackAndIgnore(TriggerPlatform, false);
                    TrackAndIgnore(BottomRamp, true);
                    TrackAndIgnore(BottomPlatform, true);
                    TrackAndIgnore(TopRamp, false);
                    TrackAndIgnore(TopPlatform, true);
                    TrackAndIgnore(SecondTopRamp, true);
                    break;
            }
        }
        
        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerMain detectedPlayer = other.GetComponent<PlayerMain>();
            if (detectedPlayer != null && detectedPlayer.gameObject.CompareTag("Player"))
            {
                player = detectedPlayer;
                playerEnteredTrigger = true;
                
                ResetAllPlayerIgnoredColliders();
                ApplyCurrentStateRules(); // <-- Force initial rules to apply immediately on entry!
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerMain detectedPlayer = other.GetComponent<PlayerMain>();
            if (detectedPlayer != null && detectedPlayer.gameObject.CompareTag("Player") && detectedPlayer == player)
            {
                playerEnteredTrigger = false;
            }
        }
    }
}