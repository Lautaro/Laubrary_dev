namespace Laubrary.SimpleUI.Demo
{
    [SimpleUI]
    public class CharacterSheet : SimpleUIPoco
    {
        private string _characterName = "Adventurer";
        private int _health = 100;
        private float _stamina = 1f;
        private bool _isAlive = true;
        private CharacterClass _characterClass = CharacterClass.Warrior;
        private int _level = 1;
        private int _gold = 0;
        private int _experience = 0;
        
        [SimpleUIPath("CharacterNameLabel")]
        [SimpleUIPath("CharacterNameInput")]
        public string characterName
        {
            get => _characterName;
            set => SetField(ref _characterName, value);
        }
        
        public int health
        {
            get => _health;
            set => SetField(ref _health, value);
        }
        
        public float stamina
        {
            get => _stamina;
            set => SetField(ref _stamina, value);
        }
        
        public bool isAlive
        {
            get => _isAlive;
            set => SetField(ref _isAlive, value);
        }
        
        public CharacterClass characterClass
        {
            get => _characterClass;
            set => SetField(ref _characterClass, value);
        }
        
        [SimpleUIFormat("Level {0}")]
        public int level
        {
            get => _level;
            set => SetField(ref _level, value);
        }
        
        [SimpleUIFormat("Gold: {0:N0}")]
        public int gold
        {
            get => _gold;
            set => SetField(ref _gold, value);
        }
        
        [SimpleUIPath("XP/Details/ExperienceBar")]
        [SimpleUIFormat("XP: {0}/1000")]
        public int experience
        {
            get => _experience;
            set => SetField(ref _experience, value);
        }
        
        [SimpleUIIgnore]
        public float cachedDamage;
    }
}
