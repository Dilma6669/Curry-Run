using UnityEngine;

namespace UltimateCC
{
    public class Platform_OLD : MonoBehaviour
    {
        public enum Axis { Horizontal, Vertical }
        [SerializeField] private Axis movementAxis = Axis.Horizontal;
        [SerializeField] private Vector2 rightBorderOffset;
        [SerializeField] private Vector2 leftBorderOffset;
        [SerializeField] private Vector2 topBorderOffset;
        [SerializeField] private Vector2 bottomBorderOffset;
        [SerializeField] private float pauseDuration = 1f;
        [SerializeField] private float stopInterval = 5f; // Interval in world space units to pause on
        private Vector2 startPoint;
        [SerializeField, Range(-1, 1)] private int direction;
        [SerializeField] private float speed;
        Rigidbody2D rb;

        private float pauseTimer;
        private float nextPauseY;

        void Start()
        {
            startPoint = transform.position;
            if (direction == 0)
            {
                direction = 1;
            }
            rb = GetComponent<Rigidbody2D>();

            if (movementAxis == Axis.Vertical && stopInterval > 0f)
            {
                float halfInterval = stopInterval * 0.5f;
                nextPauseY = Mathf.Round((transform.position.y - halfInterval) / stopInterval) * stopInterval + halfInterval;
                if (nextPauseY <= transform.position.y && direction == 1)
                {
                    nextPauseY += stopInterval;
                }
                else if (nextPauseY >= transform.position.y && direction == -1)
                {
                    nextPauseY -= stopInterval;
                }
            }
        }

        void FixedUpdate()
        {
            PlatformMovement();
        }

        private void PlatformMovement()
        {
            if (movementAxis == Axis.Horizontal)
            {
                if (direction == 1)
                {
                    if (transform.position.x < startPoint.x)
                    {
                        rb.linearVelocity = new Vector2(direction * speed, 0);
                    }
                    else if (transform.position.x < startPoint.x + rightBorderOffset.x)
                    {
                        rb.linearVelocity = new Vector2(direction * speed, 0);
                    }
                    else if (transform.position.x >= startPoint.x + rightBorderOffset.x)
                    {
                        direction = -1;
                        rb.linearVelocity = new Vector2(direction * speed, 0);
                    }
                }
                else if (direction == -1)
                {
                    if (transform.position.x > startPoint.x + startPoint.x)
                    {
                        rb.linearVelocity = new Vector2(direction * speed, 0);
                    }
                    else if (transform.position.x > startPoint.x + leftBorderOffset.x)
                    {
                        rb.linearVelocity = new Vector2(direction * speed, 0);
                    }
                    else if (transform.position.x <= startPoint.x + leftBorderOffset.x)
                    {
                        direction = 1;
                        rb.linearVelocity = new Vector2(direction * speed, 0);
                    }
                }
            }
            else
            {
                if (pauseTimer > 0f)
                {
                    pauseTimer -= Time.fixedDeltaTime;
                    rb.linearVelocity = Vector2.zero;
                    return;
                }

                if (stopInterval <= 0f)
                {
                    HandleStandardVerticalMovement();
                    return;
                }

                float halfInterval = stopInterval * 0.5f;

                if (direction == 1)
                {
                    if (transform.position.y >= nextPauseY)
                    {
                        Vector2 pos = rb.position;
                        pos.y = Mathf.Round(nextPauseY);
                        rb.position = pos;

                        pauseTimer = pauseDuration;
                        nextPauseY += stopInterval;
                        rb.linearVelocity = Vector2.zero;
                        return;
                    }

                    if (transform.position.y < startPoint.y)
                    {
                        rb.linearVelocity = new Vector2(0, direction * speed);
                    }
                    else if (transform.position.y < startPoint.y + topBorderOffset.y)
                    {
                        rb.linearVelocity = new Vector2(0, direction * speed);
                    }
                    else if (transform.position.y >= startPoint.y + topBorderOffset.y)
                    {
                        direction = -1;
                        // Snap position to the border and step inward safely so it doesn't instantly trigger a pause
                        Vector2 pos = rb.position;
                        pos.y = startPoint.y + topBorderOffset.y;
                        rb.position = pos;
                        nextPauseY = pos.y - stopInterval;
                        
                        rb.linearVelocity = new Vector2(0, direction * speed);
                    }
                }
                else if (direction == -1)
                {
                    if (transform.position.y <= nextPauseY)
                    {
                        Vector2 pos = rb.position;
                        pos.y = Mathf.Round(nextPauseY);
                        rb.position = pos;

                        pauseTimer = pauseDuration;
                        nextPauseY -= stopInterval;
                        rb.linearVelocity = Vector2.zero;
                        return;
                    }

                    if (transform.position.y > startPoint.y + bottomBorderOffset.y)
                    {
                        rb.linearVelocity = new Vector2(0, direction * speed);
                    }
                    else if (transform.position.y <= startPoint.y + bottomBorderOffset.y)
                    {
                        direction = 1;
                        // Snap position to the border and step inward safely so it doesn't instantly trigger a pause
                        Vector2 pos = rb.position;
                        pos.y = startPoint.y + bottomBorderOffset.y;
                        rb.position = pos;
                        nextPauseY = pos.y + stopInterval;

                        rb.linearVelocity = new Vector2(0, direction * speed);
                    }
                }
            }
        }

        private void HandleStandardVerticalMovement()
        {
            if (direction == 1)
            {
                if (transform.position.y < startPoint.y || transform.position.y < startPoint.y + topBorderOffset.y)
                {
                    rb.linearVelocity = new Vector2(0, direction * speed);
                }
                else
                {
                    direction = -1;
                    rb.linearVelocity = new Vector2(0, direction * speed);
                }
            }
            else
            {
                if (transform.position.y > startPoint.y + bottomBorderOffset.y)
                {
                    rb.linearVelocity = new Vector2(0, direction * speed);
                }
                else
                {
                    direction = 1;
                    rb.linearVelocity = new Vector2(0, direction * speed);
                }
            }
        }
    }
}