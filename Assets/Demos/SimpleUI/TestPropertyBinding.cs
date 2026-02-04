using UnityEngine;
using Laubrary.SimpleUI;
using Laubrary.SimpleUI.Demo;

public class TestPropertyBinding : MonoBehaviour
{
    void Start()
    {
        var view = FindFirstObjectByType<SimpleUIView>();
        if (view == null)
        {
            Debug.LogError("SimpleUIView not found");
            return;
        }

        var character = new CharacterSheet
        {
            characterName = "Test Character",
            health = 100,
            stamina = 0.8f,
            isAlive = true,
            characterClass = CharacterClass.Warrior,
            level = 5,
            gold = 1000,
            experience = 500
        };

        view.UpdateUI(character);
        Debug.Log("Initial update complete - characterName should be 'Test Character'");

        character.characterName = "Updated Name";
        Debug.Log("Property changed to 'Updated Name' - should auto-refresh via Refresh()");
    }
}
