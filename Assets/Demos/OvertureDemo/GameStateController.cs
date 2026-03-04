using TMPro;
using UnityEngine;

namespace Laubrary.Overture.Demo
{
    /// <summary>
    /// Drives the GameState gameplay: the cube drifts slowly across the plane,
    /// rewards a point on click, then dashes to a new position.
    /// </summary>
    public class GameStateController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ClickableCube interactable;
        [SerializeField] private TextMeshProUGUI pointsText;
        [SerializeField] private TextMeshProUGUI messageTicker;

        [Header("Idle Movement")]
        [SerializeField] private float idleRotateSpeed = 40f;
        [SerializeField] private float idleMoveSpeed  = 1.2f;

        [Header("Excited Movement")]
        [SerializeField] private float excitedSpinSpeed = 600f;
        [SerializeField] private float excitedMoveSpeed = 10f;
        [SerializeField] private float excitedDuration  = 0.8f;

        [Header("Bounds")]
        [SerializeField] private float boundsX = 4f;
        [SerializeField] private float boundsZ = 4f;
        [SerializeField] private float fixedY  = 0f;

        private int     _points;
        private bool    _excited;
        private float   _excitedTimer;
        private Vector3 _target;

        private static readonly string[] Messages =
        {
            "Nice!", "Got it!", "Click faster!", "So skillful!", "Point!"
        };

        private void OnEnable()
        {
            _points      = 0;
            _excited     = false;
            _excitedTimer = 0f;
            _target      = RandomPosition();
            interactable.transform.position = RandomPosition();
            UpdateUI();

            interactable.OnClicked += HandleClick;
        }

        private void OnDisable()
        {
            interactable.OnClicked -= HandleClick;
        }

        private void Update()
        {
            if (_excited)
            {
                // Spin wildly and dash to target.
                interactable.transform.Rotate(
                    excitedSpinSpeed * Time.deltaTime,
                    excitedSpinSpeed * 0.7f * Time.deltaTime,
                    excitedSpinSpeed * 0.5f * Time.deltaTime,
                    Space.Self);

                interactable.transform.position = Vector3.MoveTowards(
                    interactable.transform.position, _target, excitedMoveSpeed * Time.deltaTime);

                _excitedTimer -= Time.deltaTime;
                if (_excitedTimer <= 0f || Vector3.Distance(interactable.transform.position, _target) < 0.05f)
                    _excited = false;
            }
            else
            {
                // Drift gently and rotate slowly.
                interactable.transform.Rotate(Vector3.up,    idleRotateSpeed * Time.deltaTime,        Space.World);
                interactable.transform.Rotate(Vector3.right, idleRotateSpeed * 0.3f * Time.deltaTime, Space.Self);

                interactable.transform.position = Vector3.MoveTowards(
                    interactable.transform.position, _target, idleMoveSpeed * Time.deltaTime);

                if (Vector3.Distance(interactable.transform.position, _target) < 0.1f)
                    _target = RandomPosition();
            }
        }

        private void HandleClick()
        {
            _points++;
            _target       = RandomPosition();
            _excited      = true;
            _excitedTimer = excitedDuration;

            UpdateUI();
            if (messageTicker != null)
                messageTicker.text = Messages[Random.Range(0, Messages.Length)];
        }

        private void UpdateUI()
        {
            if (pointsText != null)
                pointsText.text = $"Points: {_points}";
        }

        private Vector3 RandomPosition() => new Vector3(
            Random.Range(-boundsX, boundsX),
            fixedY,
            Random.Range(-boundsZ, boundsZ));
    }
}
