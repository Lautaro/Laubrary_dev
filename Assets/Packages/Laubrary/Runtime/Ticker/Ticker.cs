using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Cookbook;

namespace Laubrary.LaubraryTicker
{
    /// <summary>
    /// Implement this interface to receive a Tick() call every frame via Ticker.Register().
    /// Always call Ticker.Unregister() when the object is no longer needed to avoid memory leaks.
    /// </summary>
    public interface ITickable
    {
        void Tick();
    }

    /// <summary>
    /// A persistent MonoSingleton that drives the update loop for non-MonoBehaviour types.
    ///
    /// Provides two subscription models:
    /// - Event-based: subscribe to OnUpdate / OnLateUpdate / OnFixedUpdate for simple callbacks.
    /// - ITickable: register/unregister structured objects that implement ITickable.
    ///
    /// Both models are driven by Unity's native Update, LateUpdate, and FixedUpdate.
    /// The singleton is created automatically on first access and survives scene loads.
    /// </summary>
    public class Ticker : MonoSingleton<Ticker>
    {
        /// <summary>Fires every frame during Update, before ITickable objects are ticked.</summary>
        public static event Action OnUpdate;

        /// <summary>Fires every frame during LateUpdate.</summary>
        public static event Action OnLateUpdate;

        /// <summary>Fires every frame during FixedUpdate.</summary>
        public static event Action OnFixedUpdate;

        private readonly List<ITickable> _tickables = new();

        public override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize() => _ = I;

        /// <summary>Registers an ITickable to receive Tick() calls every Update frame.</summary>
        public static void Register(ITickable tickable) => I._RegisterInternal(tickable);

        /// <summary>Unregisters an ITickable so it no longer receives Tick() calls.</summary>
        public static void Unregister(ITickable tickable) => I._UnregisterInternal(tickable);

        private void _RegisterInternal(ITickable tickable)
        {
            if (!_tickables.Contains(tickable))
                _tickables.Add(tickable);
        }

        private void _UnregisterInternal(ITickable tickable)
        {
            _tickables.Remove(tickable);
        }

        void Update()
        {
            OnUpdate?.Invoke();

            for (int i = _tickables.Count - 1; i >= 0; i--)
            {
                if (_tickables[i] != null)
                    _tickables[i].Tick();
                else
                    _tickables.RemoveAt(i);
            }
        }

        void LateUpdate() => OnLateUpdate?.Invoke();

        void FixedUpdate() => OnFixedUpdate?.Invoke();

        // -------------------------------------------------------------------------
        // Timer
        // -------------------------------------------------------------------------

        /// <summary>
        /// A serializable duration tracker. Create one with a duration and query it each frame.
        /// Fields Duration and StartTime are public for Unity inspector serialization.
        /// </summary>
        [Serializable]
        public class Timer
        {
            /// <summary>Total duration in seconds.</summary>
            public float Duration;

            /// <summary>Time.time value recorded at creation.</summary>
            public float StartTime;

            /// <summary>Seconds elapsed since the timer was created.</summary>
            public float Elapsed => Time.time - StartTime;

            /// <summary>Seconds remaining until the timer expires. Clamped to zero.</summary>
            public float Remaining => Mathf.Max(0f, Duration - Elapsed);

            /// <summary>Remaining time as a 0–1 fraction (1 = just started, 0 = expired).</summary>
            public float RemainingNormalized => Duration > 0f ? Remaining / Duration : 0f;

            /// <summary>Elapsed time as a 0–1 fraction (0 = just started, 1 = expired).</summary>
            public float ElapsedNormalized => Duration > 0f ? Mathf.Min(1f, Elapsed / Duration) : 1f;

            /// <summary>True once elapsed time meets or exceeds the duration.</summary>
            public bool IsExpired => Elapsed >= Duration;

            public Timer(float duration)
            {
                Duration = duration;
                StartTime = Time.time;
            }
        }
    }
}
