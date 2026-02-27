using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace Laubrary.SmartStats.Demo
{
    public class SmartStatsDemoUISimple : MonoBehaviour
    {
        [SerializeField] private SmartStatsDemoSimple simpleDemo;
        [SerializeField] private SmartStatsDemoAdvancedFloat advancedFloatDemo;
        [SerializeField] private SmartStatsDemoBool boolDemo;
        [SerializeField] private TextMeshProUGUI statsText;
        [SerializeField] private TextMeshProUGUI controlsText;

        void Start()
        {
            if (simpleDemo == null)
                simpleDemo = FindFirstObjectByType<SmartStatsDemoSimple>();
            if (advancedFloatDemo == null)
                advancedFloatDemo = FindFirstObjectByType<SmartStatsDemoAdvancedFloat>();
            if (boolDemo == null)
                boolDemo = FindFirstObjectByType<SmartStatsDemoBool>();

            UpdateControlsText();
        }

        void Update()
        {
            UpdateStatsDisplay();

            if (Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame)
                ClearAll();
        }

        void UpdateStatsDisplay()
        {
            if (simpleDemo == null) return;

            statsText.text = "<b>CURRENT STATS</b>\n\n" +
                             $"<color=#FF6B6B>Health:</color> {(int)simpleDemo.health} / {simpleDemo.health.maxValue}\n" +
                             $"<color=#4ECDC4>Move Speed:</color> {(float)simpleDemo.moveSpeed:F1}\n";

            if (advancedFloatDemo != null)
                statsText.text += $"<color=#FFD700>Power:</color> {(float)advancedFloatDemo.power:F1}\n";

            if (boolDemo != null)
                statsText.text += $"<color=#B0C4DE>Is Alive:</color> {(bool)boolDemo.isAlive}\n";

            statsText.text += "\n<size=16><color=#95E1D3>Base values never change!\nModifiers stack on top.</color></size>";
        }

        void UpdateControlsText()
        {
            controlsText.text =
                "<b>DEMO CONTROLS</b>\n\n" +

                "<b><size=15>Basic (Health / Speed):</size></b>\n" +
                "<b>1</b> - Add Health +20\n" +
                "<b>2</b> - Add Speed +50%\n" +
                "<b>3</b> - Timed Health +30 (5s)\n" +
                "<b>4</b> - Timed Speed +100% (3s)\n\n" +

                "<b><size=15>Advanced Float (Power):</size></b>\n" +
                "<b>5</b> - Toggle Rage (conditional +50)\n" +
                "<b>6</b> - Toggle Switch Bonus (+30)\n" +
                "<b>7</b> - Priority Demo (+100 P:-10, x1.5 P:10)\n" +
                "<b>8</b> - Fading Boost +150 (decreasing)\n" +
                "<b>9</b> - Building Boost +150 (increasing)\n\n" +

                "<b><size=15>Bool (isAlive, base: true):</size></b>\n" +
                "<b>Z</b> - Toggle Flip (P:0)\n" +
                "<b>X</b> - Toggle AlwaysTrue (P:5)\n" +
                "<b>C</b> - Toggle AlwaysFalse (P:-10)\n" +
                "<b>V</b> - Clear Bool modifiers\n\n" +

                "<b>Backspace</b> - Clear ALL modifiers\n\n" +

                "<size=13><color=#95E1D3>Select a GameObject in the Inspector\nto see modifiers in real-time!</color></size>";
        }

        /// <summary>Clears all modifiers from every demo script in the scene.</summary>
        void ClearAll()
        {
            simpleDemo?.ClearAllModifiers();
            advancedFloatDemo?.ClearAllModifiers();
            boolDemo?.ClearAllModifiers();
            Debug.Log("<color=white><b>--- CLEARED ALL MODIFIERS ---</b></color>");
        }
    }
}
