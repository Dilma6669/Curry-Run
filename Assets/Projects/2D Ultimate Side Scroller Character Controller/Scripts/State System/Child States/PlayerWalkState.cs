using UnityEngine;

namespace UltimateCC
{
    public class PlayerWalkState : MainState, IMove1D
    {
        enum Phase { SpeedUp, SlowDown, TurnBack }
        Phase phase;
        private float curveTime;
        private float localXVelovity;
        private int turnBackStartDirection;

        // Crowd resistance & inertia tracking
        private float playerSpeedMultiplier = 1f;

        public PlayerWalkState(PlayerMain player, PlayerStateMachine stateMachine, PlayerMain.AnimName animEnum, PlayerData playerData) : base(player, stateMachine, animEnum, playerData)
        {
        }

        public override void Enter()
        {
            base.Enter();
            rigidbody2D.gravityScale = playerData.Walk.Physics2DGravityScale;
            localTime = 0f;
            curveTime = EssentialPhysics.SetCurveTimeByValue(playerData.Walk.SpeedUpCurve, Mathf.Abs(rigidbody2D.linearVelocity.x) / playerData.Walk.MaxSpeed, 1, true);
            curveTime *= playerData.Walk.SpeedUpTime;
            localXVelovity = 0f;
            phase = Phase.SpeedUp;
            playerSpeedMultiplier = 1f;
        }

        public override void Update()
        {
            base.Update();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            CheckPlayerCrowdOverlap(); // Only calculates speed slowdown now; pass-through is handled by MainState!
            Move1D();
            rigidbody2D.linearVelocity += playerData.Physics.Platform.DampedVelocity;
            curveTime += Time.fixedDeltaTime;
            playerData.Walls.CurrentStamina = Mathf.Clamp(playerData.Walls.CurrentStamina + (Time.fixedDeltaTime * playerData.Walls.StaminaRegenPerSec), 0, playerData.Walls.MaxStamina);
        }
        
        // Cleaned up: removed ignoredNpcColliders and IgnoreCollision here because MainState handles it globally.
        void CheckPlayerCrowdOverlap()
        {
            int crowdingCount = 0;

            if (player.CapsuleCollider2D != null)
            {
                Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(player.transform.position, 0.6f);
                foreach (var col in nearbyColliders)
                {
                    if (col.gameObject != player.gameObject)
                    {
                        NPCController otherNpc = col.GetComponent<NPCController>();
                        if (otherNpc != null)
                        {
                            crowdingCount++;
                        }
                    }
                }
            }

            float targetMultiplier = 1f;
            if (crowdingCount == 1) targetMultiplier = 0.3f;
            else if (crowdingCount == 2) targetMultiplier = 0.1f;
            else if (crowdingCount >= 3) targetMultiplier = 0f;

            playerSpeedMultiplier = Mathf.MoveTowards(playerSpeedMultiplier, targetMultiplier, Time.fixedDeltaTime * 10f);
        }

        public override void Exit()
        {
            base.Exit();
        }

        public override void PhysicsCheck()
        {
            base.PhysicsCheck();
        }

        public override void SwitchStateLogic()
        {
            base.SwitchStateLogic();
            if ((phase == Phase.SlowDown && curveTime > playerData.Walk.SlowDownTime) || playerData.Physics.Slope.StayStill)
            {
                stateMachine.ChangeState(player.IdleState);
            }
            else if (inputManager.Input_Jump && playerData.Physics.CanJump)
            {
                stateMachine.ChangeState(player.JumpState);
            }
            else if (!playerData.Physics.IsGrounded || (playerData.Physics.IsOnNotWalkableSlope && !playerData.Physics.Slope.StayStill && !playerData.Physics.IsMultipleContactWithNonWalkableSlope))
            {
                stateMachine.ChangeState(player.LandState);
            }
            else if (inputManager.Input_Dash && playerData.Dash.DashCooldownTimer <= 0f)
            {
                stateMachine.ChangeState(player.DashState);
            }
            else if (inputManager.Input_Crouch)
            {
                stateMachine.ChangeState(player.CrouchIdleState);
            }
            else if (playerData.Physics.IsNextToWall && inputManager.Input_WallGrab && playerData.Walls.CurrentStamina > 0)
            {
                stateMachine.ChangeState(player.WallGrabState);
            }
        }

        public void Move1D()
        {
            float newVelocity;
            if (playerData.Physics.IsOnNotWalkableSlope && Mathf.Sign(playerData.Physics.ContactPosition.x - player.transform.position.x) == Mathf.Sign(playerData.Physics.FacingDirection)
                && playerData.Physics.Slope.CurrentSlopeAngle > playerData.Physics.Slope.MaxSlopeAngle)
            {
                rigidbody2D.linearVelocity = Vector2.zero;
            }
            else if (!playerData.Physics.IsOnNotWalkableSlope)
            {
                newVelocity = VelocityOnX();
                rigidbody2D.linearVelocity = -1 * newVelocity * playerData.Physics.WalkSpeedDirection.normalized;
            }
            else
            {
                newVelocity = VelocityOnX();
                rigidbody2D.linearVelocity = new Vector2(newVelocity, rigidbody2D.linearVelocity.y);
            }
            
            rigidbody2D.linearVelocity = new Vector2(rigidbody2D.linearVelocity.x * playerSpeedMultiplier, rigidbody2D.linearVelocity.y);

            localXVelovity = rigidbody2D.linearVelocity.x;
        }

        private float VelocityOnX()
        {
            float XVelocity;
            if (inputManager.Input_Walk != 0 && (localXVelovity == 0 || Mathf.Sign(inputManager.Input_Walk) == Mathf.Sign(localXVelovity))
                && (phase != Phase.TurnBack || curveTime > playerData.Walk.TurnBackTime))
            {
                if (phase != Phase.SpeedUp)
                {
                    curveTime = EssentialPhysics.SetCurveTimeByValue(playerData.Walk.SpeedUpCurve, Mathf.Abs(localXVelovity) / playerData.Walk.MaxSpeed, 1f, true);
                    curveTime *= playerData.Walk.SpeedUpTime;
                    phase = Phase.SpeedUp;
                }
                if (curveTime < playerData.Walk.SpeedUpTime)
                {
                    XVelocity = playerData.Walk.SpeedUpCurve.Evaluate(curveTime / playerData.Walk.SpeedUpTime);
                }
                else
                {
                    XVelocity = playerData.Walk.SpeedUpCurve.Evaluate(1f);
                }
                XVelocity *= inputManager.Input_Walk * playerData.Walk.MaxSpeed;
            }
            else if (inputManager.Input_Walk != 0 && ((localXVelovity != 0 && Mathf.Sign(inputManager.Input_Walk) != Mathf.Sign(localXVelovity)) || phase == Phase.TurnBack))
            {
                if (phase != Phase.TurnBack)
                {
                    curveTime = EssentialPhysics.SetCurveTimeByValue(playerData.Walk.TurnBackCurve, Mathf.Abs(localXVelovity) / playerData.Walk.MaxSpeed, 2f, false);
                    curveTime *= playerData.Walk.TurnBackTime;
                    phase = Phase.TurnBack;
                    turnBackStartDirection = (int)Mathf.Sign(localXVelovity);
                }
                if (curveTime < playerData.Walk.TurnBackTime)
                {
                    XVelocity = playerData.Walk.TurnBackCurve.Evaluate(curveTime / playerData.Walk.TurnBackTime);
                }
                else
                {
                    XVelocity = playerData.Walk.TurnBackCurve.Evaluate(2f);
                }
                XVelocity *= playerData.Walk.MaxSpeed * turnBackStartDirection;
            }
            else
            {
                if (phase != Phase.SlowDown)
                {
                    curveTime = EssentialPhysics.SetCurveTimeByValue(playerData.Walk.SlowDownCurve, Mathf.Abs(localXVelovity) / playerData.Walk.MaxSpeed, 1f, false);
                    curveTime *= playerData.Walk.SlowDownTime;
                    phase = Phase.SlowDown;
                }
                if (curveTime < playerData.Walk.SlowDownTime)
                {
                    XVelocity = playerData.Walk.SlowDownCurve.Evaluate(curveTime / playerData.Walk.SlowDownTime);
                }
                else
                {
                    XVelocity = playerData.Walk.SlowDownCurve.Evaluate(1f);
                }
                XVelocity *= playerData.Physics.FacingDirection * playerData.Walk.MaxSpeed;
            }

            if (playerData.Physics.Contacts.Count == 0)
            {
                rigidbody2D.gravityScale = playerData.Land.MinLandSpeed / Physics2D.gravity.y;
            }
            else
            {
                rigidbody2D.gravityScale = playerData.Walk.Physics2DGravityScale;
            }
            return XVelocity;
        }
    }
}