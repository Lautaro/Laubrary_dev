using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.SimpleUI.Demo
{
    public class SimpleUITestManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private SimpleUIView characterView;

        [Header("Test Data")]
        private CharacterSheet _character;
        private float _nextGoldTime;

        void Start()
        {
            _character = new CharacterSheet
            {
                characterName = "Adventurer",
                health = 100,
                isAlive = true,
                stamina = 1f,
                characterClass = CharacterClass.Warrior,
                level = 1,
                gold = 0,
                experience = 0
            };

            characterView.UpdateUI(_character);

            Debug.Log("[SimpleUITestManager] Character UI initialized!");
            Debug.Log("Controls:\n- Type in name field (auto-updates via Refresh())\n- Space: Take damage\n- R: Heal");
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                _character.health -= 10;
                _character.stamina -= 5f;
                
                if (_character.health <= 0)
                {
                    _character.health = 0;
                    _character.isAlive = false;
                }

                characterView.UpdateUI();
                Debug.Log($"[Test] Took damage! Health: {_character.health}");
            }

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                _character.health = 100;
                _character.stamina = 100f;
                _character.isAlive = true;
                characterView.UpdateUI();
                Debug.Log("[Test] Fully healed!");
            }

            if (Time.time > _nextGoldTime)
            {
                _nextGoldTime = Time.time + 2f;
                var goldGained = Random.Range(10, 50);
                _character.gold += goldGained;
                _character.experience += goldGained / 2;
                
                characterView.UpdateUI();
                Debug.Log($"[Test] Found {goldGained} gold!");
            }

            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            {
                _character.gold -= 10;
                characterView.UpdateUI();
            }

       

            if (Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame)
            {
                _character.stamina -= .05f;
                characterView.UpdateUI();
            }
        }
    }
}
