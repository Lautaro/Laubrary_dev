using Laubrary.Switcheroo;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Switcheroo.Samples
{
    public class SwitcherooTester : MonoBehaviour
    {
        public string On;
        public string OffTransition;
        public string Off;
        public string OnTransition;
        public float TransitionDuration;

        TextMeshProUGUI text;
        Switcheroo switcheroo;

        void Start()
        {
            switcheroo = new();
            text = GetComponent<TextMeshProUGUI>();

            Vector3 defaultScale = transform.localScale;

            switcheroo.AddTransition("Size", (progress) =>
            {
                switch (switcheroo.CurrentState)
                {
                    case Switcheroo.SwitcherooState.On:
                        text.text = On;
                        break;
                    case Switcheroo.SwitcherooState.Off:
                        text.text = Off;
                        break;
                    case Switcheroo.SwitcherooState.TransitionToOn:
                        text.text = OnTransition;
                        break;
                    case Switcheroo.SwitcherooState.TransitionToOff:
                        text.text = OffTransition;
                        break;
                    default:
                        break;
                }

                transform.localScale = Vector3.Lerp(defaultScale, defaultScale * 3, progress);
            });
        }

        void Update()
        {
            switcheroo.transitionSpeed = TransitionDuration;

            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                switcheroo.Flip();
        }

        public void KillMe()
        {
            DestroyImmediate(gameObject);
        }

        private void OnDisable()
        {
            switcheroo.Dispose();
        }
    }
}