using System.Collections.Generic;
using UnityEngine;
using Laubrary.Choreographer;
using Laubrary.Combat2D;
using Laubrary.Zoetrope;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// Keeps a fixed number of Floating Disc Zoes alive, flying, and replaced as they are shot down.
    ///
    /// Flight is NOT hand-rolled here. Every disc transform is registered as a dancer on a Laubrary
    /// <see cref="ChoreographyPlayer"/>, so the authored <see cref="Choreography"/> asset owns the motion —
    /// the same pattern ArenaDemo uses. Bespoke per-demo movement code would be exactly the "solve in native
    /// code what Laubrary is for" bypass the project rules forbid; movement belongs to a Laubrary module.
    ///
    /// What this script DOES decide is pacing: how many discs, and when a destroyed one comes back. That is
    /// game logic, not toolkit logic, which is why it lives in the demo rather than in the package.
    ///
    /// A slot is a dancer index. Replacing the Transform in <c>targets[i]</c> re-uses that dancer's place in
    /// the choreography, so the flock stays evenly spread instead of bunching up as discs are shot down.
    /// </summary>
    public class FlyingDiscSpawner : MonoBehaviour
    {
        [Tooltip("The Zoe spawned for each disc. Its own hit/death rows own what a hit and a kill look like — " +
                 "this script never spawns an effect itself.")]
        public Zoe discDef;

        [Tooltip("Authored flight path. Every disc is a dancer on this one choreography.")]
        public Choreography choreography;

        [Tooltip("How many discs are in the air at once.")]
        [Min(1)] public int discCount = 6;

        [Tooltip("World span the normalised choreography path is mapped onto.")]
        public Vector2 worldSize = new(14f, 7f);

        [Tooltip("Seconds between a disc being cleaned up and its replacement appearing.")]
        [Min(0f)] public float respawnDelay = 1.5f;

        [Tooltip("Health each spawned disc gets, overriding the Zoe's own value. 0 = leave the Zoe's value alone. " +
                 "Set low so a disc dies in a few shots and the demo actually shows deaths.")]
        [Min(0f)] public float overrideHealth = 30f;

        ChoreographyPlayer _player;
        readonly List<float> _slotReadyAt = new();

        void Start()
        {
            if (discDef == null) { Debug.LogError("FlyingDiscSpawner: discDef not assigned.", this); enabled = false; return; }

            _player = gameObject.AddComponent<ChoreographyPlayer>();
            _player.choreography = choreography;
            _player.anchor = transform;
            _player.worldSize = worldSize;
            _player.speed = 1f;
            _player.applyFacing = false;      // a disc reads as a disc from any angle; facing would just spin it
            _player.playOnEnable = true;
            _player.targets = new List<Transform>();

            for (int i = 0; i < Mathf.Max(1, discCount); i++)
            {
                _player.targets.Add(null);
                _slotReadyAt.Add(0f);
                FillSlot(i);
            }

            // playOnEnable already fired before this component existed — start it explicitly, as ArenaSpawner does.
            _player.Play();
        }

        void Update()
        {
            for (int i = 0; i < _player.targets.Count; i++)
            {
                if (_player.targets[i] != null) continue;                  // still flying
                if (_slotReadyAt[i] == 0f) { _slotReadyAt[i] = Time.time + respawnDelay; continue; }
                if (Time.time >= _slotReadyAt[i]) FillSlot(i);
            }
        }

        void FillSlot(int i)
        {
            var go = ZoeSpawner.SpawnCharacter(discDef, transform.position, transform);
            go.name = "Disc " + i;

            // Overridden AFTER spawn rather than by editing the shared Zoe asset — a demo must never retune a
            // ScriptableObject every other scene also loads.
            if (overrideHealth > 0f)
            {
                var hp = go.GetComponent<Health>();
                if (hp != null) { hp.maxHealth = overrideHealth; hp.Revive(overrideHealth); }
            }

            _player.targets[i] = go.transform;
            _slotReadyAt[i] = 0f;
        }
    }
}
