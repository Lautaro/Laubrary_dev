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
        
        [SimpleUIPath("XP/Details/ExperienceBar")]
        [SimpleUIFormat("XP: {0}/1000")]
        public int experience = 0;
        
        [SimpleUIIgnore]
        public float cachedDamage;
    }
}
