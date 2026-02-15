using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.SimpleUI;

namespace Laubrary.SimpleUI.Demo
{
    [SimpleUI]
    public class CharacterSheet : SimpleUIPoco
    {
        private string _characterName = "Adventurer";
        
        [SimpleUIPath("CharacterNameLabel")]
        [SimpleUIPath("CharacterNameInput")]
        public string characterName
        {
            get => _characterName;
            set
            {
                _characterName = value;
                Refresh();
            }
        }
        
        public int health = 100;
        public float stamina = 1f;
        public bool isAlive = true;
        public CharacterClass characterClass = CharacterClass.Warrior;
        
        [SimpleUIFormat("Level {0}")]
        public int level = 1;
        
        [SimpleUIFormat("Gold: {0:N0}")]
        public int gold = 0;    
        
        [SimpleUIPath("Stats/Details/ExperienceBar")]
        [SimpleUIFormat("XP: {0}/1000")]
        public int experience = 0;
        
        [SimpleUIIgnore]
        public float cachedDamage;
    }

    public enum CharacterClass
    {
        Warrior,
        Mage,
        Rogue,
        Cleric
    }

    public class SimpleUIDemo : MonoBehaviour
    {
        [SerializeField]private SimpleUIView _view;
        private CharacterSheet _character;

        void Start()
        {
            if (_view == null)
            {
                Debug.LogError("[SimpleUIDemo] SimpleUIView component is missing!");
                return;
            }
            
            _character = new CharacterSheet
            {
                characterName = "Adventurer",
                health = 100,
                stamina = 0.855f,
                isAlive = true,
                characterClass = CharacterClass.Rogue,
                level = 5,
                gold = 12500,
                experience = 750,
                cachedDamage = 0f
            };
            
            _view.UpdateUI(_character);
            
            Debug.Log("[SimpleUIDemo] Demo started. Press Space to simulate damage, R to heal.");
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                TakeDamage(15);
            }
            
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                Heal(20);
            }
            
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            {
                AddGold(Random.Range(10, 100));
            }
            
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                AddExperience(50);
            }

            if (Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame)
            {
                _character.stamina -= .05f;
                _view.UpdateUI();
            }

            if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            {
                Debug.Log($"[SimpleUIDemo] Current POCO state:\n" +
                          $"  characterClass: {_character.characterClass}\n" +
                          $"  stamina: {_character.stamina}\n" +
                          $"  isAlive: {_character.isAlive}");
            }
        }

        void TakeDamage(int damage)
        {
            if (!_character.isAlive) return;
            
            _character.health = Mathf.Max(0, _character.health - damage);
            
            if (_character.health == 0)
            {
                _character.isAlive = false;
                Debug.Log("[SimpleUIDemo] Character died!");
            }
            
            _view.UpdateUI();
        }

        void Heal(int amount)
        {
            if (!_character.isAlive)
            {
                Debug.Log("[SimpleUIDemo] Cannot heal a dead character!");
                return;
            }
            
            _character.health = Mathf.Min(100, _character.health + amount);
            _view.UpdateUI();
        }

        void AddGold(int amount)
        {
            _character.gold += amount;
            Debug.Log($"[SimpleUIDemo] Found {amount} gold!");
            _view.UpdateUI();
        }

        void AddExperience(int amount)
        {
            _character.experience += amount;
            
            if (_character.experience >= 1000)
            {
                _character.level++;
                _character.experience = 0;
                Debug.Log($"[SimpleUIDemo] Level up! Now level {_character.level}");
            }
            
            _view.UpdateUI();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.Label("=== SimpleUI Demo Controls ===", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold });
            GUILayout.Label("Space - Take Damage (15 HP)");
            GUILayout.Label("R - Heal (20 HP)");
            GUILayout.Label("G - Add Gold (random)");
            GUILayout.Label("E - Add Experience (50 XP)");
            GUILayout.Label("\nTry editing values in Inspector!");
            GUILayout.EndArea();
        }
    }
}
