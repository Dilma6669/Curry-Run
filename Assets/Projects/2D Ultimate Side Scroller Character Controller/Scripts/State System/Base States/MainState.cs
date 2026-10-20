using System.Collections.Generic;
using UnityEngine;

namespace UltimateCC
{
    public class MainState
    {
        // Required variables to create logic of states
        protected PlayerMain player;
        protected PlayerStateMachine stateMachine;
        protected Rigidbody2D rigidbody2D;
        protected readonly PlayerMain.AnimName _animEnum;
        protected PlayerData playerData;
        protected PlayerInputManager inputManager;

        protected float localTime; // Variable to work at states local timeline that resets with every state change

        public MainState(PlayerMain player, PlayerStateMachine stateMachine, PlayerMain.AnimName animEnum, PlayerData playerData)
        {
            this.player = player;
            this.stateMachine = stateMachine;
            _animEnum = animEnum;
            this.playerData = playerData;
            this.inputManager = player.InputManager;
        }

        // Enter function runs at every state change after old state's exit
        public virtual void Enter()
        {
            player.CurrentState = _animEnum;
            rigidbody2D = player.Rigidbody2D;
            player.Animator.SetBool(_animEnum.ToString(), true);
            localTime = 0f;
        }

        // Update function runs at every frame
        public virtual void Update()
        {
            localTime += Time.deltaTime;

            SwitchStateLogic();
        }

        // FixedUpdate function runs at every fixed time frame (per 0.02 second default)
        public virtual void FixedUpdate()
        {
            PhysicsCheck();
            CheckGlobalNpcPassThrough(); // Runs automatically for every state!
        }

        // Must comfortably exceed the player capsule's reach (~1.2 at 1.2 scale) so NPCs are ignored a step before they can touch us
        private const float NpcPassThroughRadius = 2f;
        private const float CrowdSlowdownRadius = 0.6f;

        // Ignores physical collision with nearby NPCs and updates player.CrowdSpeedMultiplier based on how many NPCs we're overlapping
        void CheckGlobalNpcPassThrough()
        {
            int crowdingCount = 0;

            if (player.CapsuleCollider2D != null)
            {
                Vector2 playerPosition = player.transform.position;
                Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(playerPosition, NpcPassThroughRadius); // Slightly larger radius
                foreach (var col in nearbyColliders)
                {
                    if (col != null && col.gameObject != player.gameObject)
                    {
                        NPCController otherNpc = col.GetComponent<NPCController>();
                        if (otherNpc != null)
                        {
                            // Ask the physics engine directly instead of caching, so if anything re-enables
                            // this collision it gets ignored again on the next step
                            if (!Physics2D.GetIgnoreCollision(player.CapsuleCollider2D, col))
                            {
                                Physics2D.IgnoreCollision(player.CapsuleCollider2D, col, true);
                            }

                            // Only NPCs within the smaller crowd radius, and on a surface we're actually walking on, slow us down.
                            // NPCs resting at a rest area (eating, smoking) are out of the way and never do.
                            if (!otherNpc.IsResting && Vector2.Distance(col.ClosestPoint(playerPosition), playerPosition) <= CrowdSlowdownRadius
                                && IsOnPlayerSurface(otherNpc))
                            {
                                crowdingCount++;
                            }
                        }
                    }
                }
            }

            float targetMultiplier = 1f;
            if (crowdingCount == 1) targetMultiplier = 0.3f;
            else if (crowdingCount == 2) targetMultiplier = 0.1f;
            else if (crowdingCount >= 3) targetMultiplier = 0f;

            player.CrowdSpeedMultiplier = Mathf.MoveTowards(player.CrowdSpeedMultiplier, targetMultiplier, Time.fixedDeltaTime * 10f);
        }

        // An NPC standing on a surface the player currently passes through (per the RampGate rules, e.g. a ramp
        // we're walking past on the floor) isn't on our path, so it shouldn't slow us down
        bool IsOnPlayerSurface(NPCController npc)
        {
            Collider2D npcGround = npc.currentGroundCollider;
            return npcGround == null || !Physics2D.GetIgnoreCollision(player.CapsuleCollider2D, npcGround);
        }
        
        // Exit function runs at every state change before new state's enter
        public virtual void Exit()
        {
            player.Animator.SetBool(_animEnum.ToString(), false);
            localTime = 0f;
        }

        // The PhysicsCheck function runs in FixedUpdate and its purpose is to reduce the complexity of FixedUpdate and separate out the algorithms for readability and maintainability.
        // This function includes essential physics checks that are required for the player state logic to function correctly.
        public virtual void PhysicsCheck()
        {
            EssentialPhysics.SetPlayerFacingDirection(inputManager, player, playerData);
            player.CapsuleCollider2D.GetContacts(playerData.Physics.Contacts);
            EssentialPhysics.GroundCheck(player, playerData);
            EssentialPhysics.WallCheck(player, playerData);
            if (playerData.Physics.UseCustomZRotations)
            {
                rigidbody2D.freezeRotation = false;
                EssentialPhysics.ApplyRotationOnSlope(player, playerData);
            }
            else
            {
                rigidbody2D.freezeRotation = true;
            }
            EssentialPhysics.HeadBumpCheck(player, playerData);
            playerData.Physics.CanSlideCorner = EssentialPhysics.CornerSlideCheck(playerData.Physics.Contacts, player, playerData);
            playerData.Physics.CanJump = playerData.Jump.CoyoteTimeTimer > 0 && playerData.Jump.JumpBufferTimer > 0
                                            && (!playerData.Physics.IsOnNotWalkableSlope || playerData.Physics.IsMultipleContactWithNonWalkableSlope || playerData.Physics.Slope.StayStill);
            playerData.Physics.CanWallJump = playerData.Walls.WallJump.CoyoteTimeTimer > 0 && playerData.Walls.WallJump.JumpBufferTimer > 0;
            EssentialPhysics.GetPlatformVelocity(playerData.Physics.CollidedMovingRigidbody, playerData);
            playerData.Physics.LocalVelocity = rigidbody2D.linearVelocity - playerData.Physics.Platform.DampedVelocity;
            if (player.CurrentState == PlayerMain.AnimName.Jump
                || player.CurrentState == PlayerMain.AnimName.Land
                || player.CurrentState == PlayerMain.AnimName.Dash
                || player.CurrentState == PlayerMain.AnimName.WallJump)
            {
                if (localTime > playerData.Jump.CoyoteTimeTimer && playerData.Jump.NextJumpInt == 1)
                {
                    playerData.Jump.NextJumpInt++;
                }
                if (playerData.Jump.NewJump && playerData.Jump.NextJumpInt <= playerData.Jump.Jumps.Count && !playerData.Physics.CanWallJump)
                {
                    stateMachine.ChangeState(player.JumpState);
                }
            }
            else
            {
                playerData.Jump.NextJumpInt = 1;
            }
        }
        public virtual void SwitchStateLogic() { }
    }
}