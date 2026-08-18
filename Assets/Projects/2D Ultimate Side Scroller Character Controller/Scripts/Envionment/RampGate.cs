using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class RampGate : MonoBehaviour
    {
        public Collider2D secondRamp;
        public Collider2D firstRamp;
        public Collider2D floor;

        public PlayerMain player;
        public PlayerMain npc;
        
        private bool playerEnteredTrigger;
        private bool npcEnteredTrigger;

        public bool ignoreUp;
        public bool ignoreDown;
        
        private void FixedUpdate()
        {
            if (playerEnteredTrigger && player != null)
            {
                float verticalInput = player.InputManager.Input_WallClimb;
                Collider2D playerCollider = player.CapsuleCollider2D;
                
                // Check for down input (assuming negative value means pressing down)
                bool pressingDown = verticalInput < 0;

                if (floor != null && playerCollider != null)
                {
                    // Ignore floor collision if pressing down and ignoreDown is NOT set, otherwise keep it active (false)
                    bool shouldIgnoreDown = pressingDown && !ignoreDown;
                    Physics2D.IgnoreCollision(playerCollider, floor, shouldIgnoreDown);
                }

                if (verticalInput <= 0 && !pressingDown) // if not pushing up or down
                {
                    if (firstRamp != null)
                        Physics2D.IgnoreCollision(playerCollider, firstRamp, true);
                    if (secondRamp != null)
                        Physics2D.IgnoreCollision(playerCollider, secondRamp, true);
                }
                else if (verticalInput == 1) // If pushing up
                {
                    // If ignoreUp is checked, we ignore this entire block (meaning it won't apply these specific up-key ignores)
                    if (!ignoreUp)
                    {
                        if (firstRamp != null)
                            Physics2D.IgnoreCollision(playerCollider, firstRamp, false);
                        if (secondRamp != null)
                            Physics2D.IgnoreCollision(playerCollider, secondRamp, true);
                        if (floor != null)
                            Physics2D.IgnoreCollision(playerCollider, floor, true);
                    }
                }
            }
        }
        
        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerMain detectedPlayer = other.GetComponent<PlayerMain>();
            if (detectedPlayer != null && detectedPlayer.gameObject.CompareTag("Player"))
            {
                player = detectedPlayer;
                playerEnteredTrigger = true;
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

        private void SetRampsToDefault()
        {
            Collider2D playerCollider = player.CapsuleCollider2D;
            if (secondRamp != null) Physics2D.IgnoreCollision(playerCollider, secondRamp, false); 
            if (firstRamp != null) Physics2D.IgnoreCollision(playerCollider, firstRamp, false);
        }
    }
}