using UnityEngine;
using Laubrary.SimpleUI;

namespace Laubrary.SimpleUI.Examples
{
    [SimpleUI]
    public class PlayerData
    {
        public string playerName;
        public int health;
        public float stamina;
        public bool isDead;
        public PlayerRank rank;
        
        [SimpleUIFormat("XP: {0}")]
        public int experience;
        
        [SimpleUIIgnore]
        public float internalTimer;
    }

    public enum PlayerRank 
    { 
        Novice, 
        Veteran, 
        Master 
    }

    public class SimpleUIExample : MonoBehaviour
    {
        private SimpleUIView _view;
        private PlayerData _playerData;

        void Start()
        {
            _view = GetComponent<SimpleUIView>();
            
            _playerData = new PlayerData
            {
                playerName = "Hero",
                health = 100,
                stamina = 75.5f,
                isDead = false,
                rank = PlayerRank.Veteran,
                experience = 1250
            };
            
            _view.UpdateUI(_playerData);
        }

        void Update()
        {
            _playerData.internalTimer += Time.deltaTime;
            
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TakeDamage(10);
            }
            
            if (Input.GetKeyDown(KeyCode.R))
            {
                Heal(25);
            }
        }

        void TakeDamage(int damage)
        {
            _playerData.health = Mathf.Max(0, _playerData.health - damage);
            
            if (_playerData.health == 0)
            {
                _playerData.isDead = true;
            }
            
            _view.UpdateUI(_playerData);
        }

        void Heal(int amount)
        {
            if (!_playerData.isDead)
            {
                _playerData.health = Mathf.Min(100, _playerData.health + amount);
                _view.UpdateUI(_playerData);
            }
        }
    }
}
