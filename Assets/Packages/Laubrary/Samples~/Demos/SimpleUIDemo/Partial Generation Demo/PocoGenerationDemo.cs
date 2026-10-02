namespace Laubrary.SimpleUI.Demo
{
    [SimpleUI]
    [GenerateSimpleUIProperties(GenerateMode.All)]
    public partial class PocoGenerationDemo : SimpleUIPoco
    {
        private int _health = 100;
        private float _stamina = 1f;
        private bool _isAlive = true;
        private CharacterClass _characterClass = CharacterClass.Warrior;
        
        [SimpleUIFormat("Level {0}")]
        private int _level = 1;
        
        [SimpleUIFormat("Gold: {0:N0}")]
        private int _gold = 0;
        
        [SimpleUIPath("Stats/Details/ExperienceBar")]
        [SimpleUIFormat("XP: {0}/1000")]
        private int _experience = 0;
        
        [SimpleUIPath("CharacterNameLabel")]
        [SimpleUIPath("CharacterNameInput")]
        private string _characterName = "Adventurer";
        
        [IgnorePropertyGeneration]
        [SimpleUIIgnore]
        public float cachedDamage;
    }
}
