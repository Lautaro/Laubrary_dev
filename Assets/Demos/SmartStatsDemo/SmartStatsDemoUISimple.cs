using UnityEngine;
using TMPro;

namespace Laubrary.SmartStats.Demo
{
    public class SmartStatsDemoUISimple : MonoBehaviour
    {
        [SerializeField] private SmartStatsDemoSimple statsDemo;
        [SerializeField] private TextMeshProUGUI statsText;
        [SerializeField] private TextMeshProUGUI controlsText;

        void Start()
        {
            if (statsDemo == null)
            {
                statsDemo = FindFirstObjectByType<SmartStatsDemoSimple>();
            }

            UpdateControlsText();
        }

        void Update()
        {
            if (statsDemo != null)
            {
                UpdateStatsDisplay();
            }
        }

        void UpdateStatsDisplay()
        {
            statsText.text = $"<b>CURRENT STATS</b>\n\n" +
                            $"<color=#FF6B6B>Health:</color> {(float)statsDemo.health:F0} / {statsDemo.health.maxValue}\n" +
                            $"<color=#4ECDC4>Move Speed:</color> {(float)statsDemo.moveSpeed:F1}\n\n" +
                            $"<size=16><color=#95E1D3>Base values never change!\nModifiers stack on top.</color></size>";
        }

        void UpdateControlsText()
        {
            controlsText.text = "<b>DEMO CONTROLS</b>\n\n" +
                               "<b><size=18>Permanent Modifiers:</size></b>\n" +
                               "<b>1</b> - Add Health Modifier (+20)\n" +
                               "<b>2</b> - Add Speed Modifier (+50%)\n\n" +
                               "<b><size=18>Timed Modifiers:</size></b>\n" +
                               "<b>3</b> - Timed Health Boost (+30 for 5s)\n" +
                               "<b>4</b> - Timed Speed Boost (+100% for 3s)\n\n" +
                               "<size=14><color=#95E1D3>Watch the Inspector to see\nmodifiers in real-time!</color></size>";
        }
    }
}
