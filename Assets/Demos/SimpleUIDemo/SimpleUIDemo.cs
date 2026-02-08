using UnityEngine;
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
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TakeDamage(15);
            }
            
            if (Input.GetKeyDown(KeyCode.R))
            {
                Heal(20);
            }
            
            if (Input.GetKeyDown(KeyCode.G))
            {
                AddGold(Random.Range(10, 100));
            }
            
            if (Input.GetKeyDown(KeyCode.E))
            {
                AddExperience(50);
            }

            if (Input.GetKeyDown(KeyCode.S))
            {
                _character.stamina -= .05f;
                _view.UpdateUI();
            }

            if (Input.GetKeyDown(KeyCode.L))
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
