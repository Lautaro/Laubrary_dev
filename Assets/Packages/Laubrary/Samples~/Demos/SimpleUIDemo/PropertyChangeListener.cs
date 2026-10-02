using UnityEngine;

namespace Laubrary.SimpleUI.Demo
{
    public class PropertyChangeListener : MonoBehaviour
    {
        private CharacterSheet _character;
        private SimpleUIView _view;

        void Start()
        {
            _view = GetComponent<SimpleUIView>();
            
            _character = new CharacterSheet
            {
                characterName = "Hero",
                health = 100,
                stamina = 1f,
                isAlive = true,
                characterClass = CharacterClass.Warrior,
                level = 1,
                gold = 0,
                experience = 0
            };

            SubscribeWithPropertyChanged();
            SubscribeWithValueChanged();

            _view.UpdateUI(_character);

            Debug.Log("[PropertyChangeListener] Listening for property changes. Try changing values in the Inspector!");
        }

        private void SubscribeWithPropertyChanged()
        {
            _character.PropertyChanged += (sender, e) =>
            {
                var character = sender as CharacterSheet;
                
                Debug.Log($"[PropertyChanged] {e.PropertyName} changed");

                if (e.PropertyName == nameof(character.health))
                {
                    if (character.health <= 20)
                    {
                        Debug.LogWarning($"⚠ Low health warning! Health is at {character.health}");
                    }
                    else if (character.health == 0)
                    {
                        Debug.LogError("💀 Character died!");
                    }
                }
            };
        }

        private void SubscribeWithValueChanged()
        {
            _character.ValueChanged += (sender, e) =>
            {
                var character = sender as CharacterSheet;
                Debug.Log($"[ValueChanged] {character.characterName}.{e.PropertyName}: {e.OldValue} → {e.NewValue}");

                if (e.PropertyName == nameof(character.gold))
                {
                    int goldChange = (int)e.NewValue - (int)e.OldValue;
                    if (goldChange > 0)
                        Debug.Log($"💰 Gained {goldChange} gold!");
                    else
                        Debug.Log($"💸 Spent {-goldChange} gold!");
                }

                if (e.PropertyName == nameof(character.level))
                {
                    Debug.Log($"🎉 Level up! {e.OldValue} → {e.NewValue}");
                }
                
                LogToAnalytics(character, e.PropertyName, e.NewValue);
            };
        }

        private void LogToAnalytics(CharacterSheet character, string propertyName, object value)
        {
            Debug.Log($"[Analytics] {character.characterName}.{propertyName} = {value}");
        }

        void OnDestroy()
        {
            if (_character != null)
            {
                _character.PropertyChanged -= null;
                _character.ValueChanged -= null;
            }
        }
    }
}
