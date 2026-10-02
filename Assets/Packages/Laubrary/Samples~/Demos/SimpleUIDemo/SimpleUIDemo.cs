using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.SimpleUI;

namespace Laubrary.SimpleUI.Demo
{
    public class SimpleUIDemo : MonoBehaviour
    {
        [SerializeField] private SimpleUIView _charachterSheetView;
        [SerializeField] private SimpleUIView _guiView;
        private CharacterSheet _character;
        private SmallPoco _guiPoco;
        void Start()
        {
            _guiPoco = new SmallPoco();

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


            //_character.PropertyChanged += (sender, args) =>
            //{
            //    Debug.Log($"[SimpleUIDemo] Property '{args.PropertyName}' changed to: " +
            //              $"{_character.GetType().GetProperty(args.PropertyName)?.GetValue(_character)}");
            //};

            _character.ValueChanged += (sender, args) =>
            {
                Debug.Log($"[SimpleUIDemo] Value '{args.PropertyName}' changed from{args.OldValue} to: {args.NewValue}");
            };

            _charachterSheetView.UpdateUI(_character);
            _guiView.UpdateUI(_guiPoco);

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
                _charachterSheetView.UpdateUI();
            }

            if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            {
                Debug.Log($"[SimpleUIDemo] Current POCO state:\n" +
                          $"  characterClass: {_character.characterClass}\n" +
                          $"  stamina: {_character.stamina}\n" +
                          $"  isAlive: {_character.isAlive}");
            }

            _guiPoco.UpdateTime();
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

            _charachterSheetView.UpdateUI();
        }

        void Heal(int amount)
        {
            if (!_character.isAlive)
            {
                Debug.Log("[SimpleUIDemo] Cannot heal a dead character!");
                return;
            }

            _character.health = Mathf.Min(100, _character.health + amount);
            _charachterSheetView.UpdateUI();
        }

        void AddGold(int amount)
        {
            _character.gold += amount;
            Debug.Log($"[SimpleUIDemo] Found {amount} gold!");
            _charachterSheetView.UpdateUI();
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

            _charachterSheetView.UpdateUI();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 100, 400, 350));
            var style = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Normal };
            GUI.color = Color.cyan;
            GUILayout.Label("=== SimpleUI Demo Controls ===", style);
            GUILayout.Label("Space - Take Damage (15 HP)", style);
            GUILayout.Label("R - Heal (20 HP)", style);
            GUILayout.Label("G - Add Gold (random)", style);
            GUILayout.Label("E - Add Experience (50 XP)", style);
            GUILayout.Label("\nTry editing values in Inspector!", style);
            GUILayout.EndArea();
        }
    }
}
