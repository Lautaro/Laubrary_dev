using UnityEngine;
using TMPro;

namespace Laubrary.SmartStats.Demo
{
    public class SmartStatsDemoUI : MonoBehaviour
{
    [SerializeField] private SmartStatsDemo statsDemo;
    [SerializeField] private TextMeshProUGUI statsText;
    [SerializeField] private TextMeshProUGUI controlsText;

    void Start()
    {
        if (statsDemo == null)
        {
            statsDemo = FindFirstObjectByType<SmartStatsDemo>();
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
                        $"<color=red>Health:</color> {(float)statsDemo.health:F0} / {statsDemo.health.maxValue}\n" +
                        $"<color=orange>Damage:</color> {(float)statsDemo.damage:F1}\n" +
                        $"<color=cyan>Speed:</color> {(float)statsDemo.moveSpeed:F1}\n" +
                        $"<color=yellow>Gold:</color> {(int)statsDemo.gold}\n" +
                        $"<color=green>XP:</color> {(int)statsDemo.experience}\n\n" +
                        $"<color=magenta>Invincible:</color> {(bool)statsDemo.isInvincible}\n" +
                        $"<color=white>Can Attack:</color> {(bool)statsDemo.canAttack}";
    }

    void UpdateControlsText()
    {
        controlsText.text = "<b>DEMO CONTROLS</b>\n\n" +
                           "<b>1</b> - Damage Boost (+25 for 5s)\n" +
                           "<b>2</b> - Speed Boost (+50% for 3s)\n" +
                           "<b>3</b> - Invincibility (10s)\n" +
                           "<b>4</b> - Take Damage (-20)\n" +
                           "<b>5</b> - Heal (+30)\n" +
                           "<b>6</b> - Add Gold (+100)\n" +
                           "<b>7</b> - Gain XP (+50)\n" +
                           "<b>8</b> - Show Stats (Console)\n" +
                           "<b>9</b> - Priority Demo\n" +
                           "<b>0</b> - Merged Multipliers";
    }
    }
}
