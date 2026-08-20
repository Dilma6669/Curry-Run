using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class RampGate : MonoBehaviour
    {
        public LayerSwitcher firstRamp;
        public LayerSwitcher secondRamp;
        public LayerSwitcher floor;

        public PlayerMain player;
        
        private bool playerEnteredTrigger;

        public bool ignoreUp;
        public bool ignoreDown;
        
        private void FixedUpdate()
        {
            if (playerEnteredTrigger && player != null)
            {
                float verticalInput = player.InputManager.Input_WallClimb;
                bool pressingDown = verticalInput < 0;

                // Neutral input (not pushing up or down)
                if (verticalInput == 0)
                {
                    if (firstRamp != null) firstRamp.SetPlayerIgnoreLayer();
                    if (secondRamp != null) firstRamp.SetPlayerIgnoreLayer();
                    if (floor != null) floor.RevertToDefaultLayer();
                }
                else if (pressingDown)
                {
                    // Pushing down: check if we should drop through the floor
                    if (floor != null)
                    {
                        bool shouldIgnoreDown = !ignoreDown;
                        if (shouldIgnoreDown)
                            floor.SetPlayerIgnoreLayer();
                        else
                            floor.RevertToDefaultLayer();
                    }

                    if (firstRamp != null) firstRamp.SetPlayerIgnoreLayer();
                    if (secondRamp != null) firstRamp.SetPlayerIgnoreLayer();
                }
                else if (verticalInput == 1) // Pushing up
                {
                    if (!ignoreUp)
                    {
                        if (firstRamp != null) firstRamp.RevertToDefaultLayer();
                        if (secondRamp != null) secondRamp.SetPlayerIgnoreLayer();
                        if (floor != null) floor.SetPlayerIgnoreLayer();
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
                
                // Optional: Apply initial entry state immediately so there's no frame delay
                ApplyCurrentState();
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerMain detectedPlayer = other.GetComponent<PlayerMain>();
            if (detectedPlayer != null && detectedPlayer.gameObject.CompareTag("Player") && detectedPlayer == player)
            {
                // We stop updating this gate's logic, but DO NOT reset the ramps/floors here.
                // They stay in their last configured state until the player hits the next trigger.
                playerEnteredTrigger = false;
            }
        }

        private void ApplyCurrentState()
        {
            // Sets initial touch behavior when first entering the trigger
            if (secondRamp != null) secondRamp.SetPlayerIgnoreLayer(); 
            if (firstRamp != null) firstRamp.SetPlayerIgnoreLayer();
            if (floor != null) floor.RevertToDefaultLayer();
        }
    }
}