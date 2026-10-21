using UnityEngine;
using UnityEngine.UI;

namespace UltimateCC
{
    // The race to the toilet: a countdown shown as a meter across the top of the screen that shrinks as time runs out.
    // Every dash takes a chunk off it. Getting through a toilet's men's door in time wins, running out loses; either
    // way the game freezes and the result panel pops up.
    public class ToiletTimer : MonoBehaviour
    {
        [Header("Timer")]
        [Tooltip("Seconds the player has to reach a toilet")]
        [SerializeField, Min(1f)] private float timeLimit = 30f;
        [Tooltip("Seconds each dash takes off the timer")]
        [SerializeField, Min(0f)] private float dashCost = 4f;
        [NonEditable] public float timeLeft;

        [Header("References")]
        [SerializeField] private PlayerMain player;
        [Tooltip("The meter's fill. Its right edge is anchored to the fraction of time left")]
        [SerializeField] private RectTransform meterFill;
        [SerializeField] private Image meterFillImage;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultMessage;

        [Header("Meter Colours")]
        [SerializeField] private Color fullColor = new Color(0.3f, 0.85f, 0.3f);
        [SerializeField] private Color lowColor = new Color(0.9f, 0.2f, 0.15f);
        [Tooltip("Below this fraction of time left the meter shows the low colour")]
        [Range(0f, 1f)] [SerializeField] private float lowFraction = 0.25f;

        private bool finished;
        private bool wasDashing;

        private void Awake()
        {
            Time.timeScale = 1f;
            timeLeft = timeLimit;
            if (resultPanel != null) resultPanel.SetActive(false);
            UpdateMeter();
        }

        private void Update()
        {
            if (finished || player == null) return;

            // Take a chunk off as each dash starts
            bool dashing = player.CurrentState == PlayerMain.AnimName.Dash;
            if (dashing && !wasDashing) timeLeft -= dashCost;
            wasDashing = dashing;

            timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);
            UpdateMeter();

            // In through the men's door: standing at it and pushing up (up arrow / W)
            if (player.InputManager.Input_WallClimb > 0f && Toilet.AnyAtDoor(player.transform.position))
            {
                Finish(true);
            }
            else if (timeLeft <= 0f)
            {
                Finish(false);
            }
        }

        private void UpdateMeter()
        {
            float fraction = timeLimit > 0f ? timeLeft / timeLimit : 0f;
            if (meterFill != null) meterFill.anchorMax = new Vector2(fraction, meterFill.anchorMax.y);
            if (meterFillImage != null) meterFillImage.color = fraction <= lowFraction ? lowColor : Color.Lerp(lowColor, fullColor, (fraction - lowFraction) / (1f - lowFraction));
        }

        private void Finish(bool reachedToilet)
        {
            finished = true;
            Time.timeScale = 0f;

            if (resultTitle != null) resultTitle.text = reachedToilet ? "You made it!" : "Too late!";
            if (resultMessage != null)
            {
                resultMessage.text = reachedToilet
                    ? $"You reached the toilet with {Mathf.CeilToInt(timeLeft)}s to spare."
                    : "You ran out of time before reaching the toilet.";
            }
            if (resultPanel != null) resultPanel.SetActive(true);
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }
    }
}
