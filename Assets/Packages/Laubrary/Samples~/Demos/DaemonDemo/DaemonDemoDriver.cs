using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Daemon;

namespace Laubrary.Demos.DaemonDemo
{
    // Scene driver for the Daemon demo. On Start it spawns a Daemon-brained Colosseum enemy (via DaemonEnemyDemo)
    // that chases a target which patrols back and forth. The Brain (DemoBrain.asset) runs Entry → Status → Wait →
    // Seek ⇄ Shoot: the enemy walks toward the target (Seek) and, when it gets within attackRange, switches to its
    // Shoot state (and back to Seek when the target escapes). The enemy's SetStatus label is surfaced as a colour
    // change so the state machine is visible without any font/UI: white while walking, yellow while attacking.
    public class DaemonDemoDriver : MonoBehaviour
    {
        [Header("Authored Brain + Behaviours (DemoBrain.asset / DemoBehaviours.asset)")]
        public Brain brain;
        public BehaviourSet behaviours;

        [Header("Optional factions (not required — this demo shows the FSM, not damage)")]
        public Faction enemyFaction;

        [Header("Layout")]
        public Vector3 enemySpawn = new Vector3(-4f, 0f, 0f);
        public float attackRange = 3f;
        public float patrolHalfWidth = 5f;
        public float patrolSpeed = 3f;

        Transform _target;
        SpriteRenderer _enemySprite;
        Color _walkColor = Color.white;
        Color _attackColor = Color.yellow;

        void Start()
        {
            // The thing the enemy chases: a green square that ping-pongs left↔right.
            var target = new GameObject("PatrolTarget");
            target.transform.position = new Vector3(patrolHalfWidth, 0f, 0f);
            Paint(target, new Color(0.4f, 0.9f, 0.4f), 0.6f);
            _target = target.transform;

            // The Daemon enemy: red square driven by the brain.
            var enemy = DaemonEnemyDemo.Spawn(enemySpawn, enemyFaction, _target,
                                              brain, behaviours, maxHealth: 30f, attackRange: attackRange);
            _enemySprite = Paint(enemy, new Color(0.9f, 0.35f, 0.35f), 0.7f);

            // Surface the brain's current state (SetStatus) as a colour so it's visible with no UI.
            var body = enemy.GetComponent<ColosseumAgentBody>();
            if (body != null)
                body.onAnim = status =>
                {
                    if (_enemySprite != null)
                        _enemySprite.color = status == "Attack" ? _attackColor : _walkColor;
                };
        }

        void Update()
        {
            if (_target == null) return;
            float x = Mathf.PingPong(Time.time * patrolSpeed, patrolHalfWidth * 2f) - patrolHalfWidth;
            _target.position = new Vector3(x, _target.position.y, _target.position.z);
        }

        // Give a GameObject a flat unit-square sprite so the demo is visible without any art assets.
        static SpriteRenderer Paint(GameObject go, Color tint, float size)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = UnitSquare();
            sr.color = tint;
            sr.sortingOrder = 10;
            go.transform.localScale = Vector3.one * size;
            return sr;
        }

        static Sprite _square;
        static Sprite UnitSquare()
        {
            if (_square == null)
            {
                var tex = new Texture2D(8, 8) { filterMode = FilterMode.Point };
                var px = new Color[64];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                tex.SetPixels(px); tex.Apply();
                _square = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
                _square.name = "DaemonDemoUnitSquare";
            }
            return _square;
        }
    }
}
