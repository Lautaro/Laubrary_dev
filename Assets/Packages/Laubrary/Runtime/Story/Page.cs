using System.Collections.Generic;
using UnityEngine;
using Laubrary.Loom;

namespace Laubrary.Story
{
    // Everything a Page needs while it runs. The Runner fills this in per step and points Bookmark at the
    // bookmark currently being advanced. (Bookmark is Loom's moving cursor — a Fork spawns more.)
    public class StoryContext : IGraphContext
    {
        public StoryRunner Runner;
        public Screenplay Screenplay;
        public Journal Journal;
        public IRuleBridge Rules;
        public IMessagePresenter Presenter;
        public IConditionSource Conditions;
        public Bookmark Bookmark { get; set; }

        public T GetLocal<T>(string key, T def = default)
            => Bookmark != null && Bookmark.Locals.TryGetValue(key, out var v) && v is T t ? t : def;
        public void SetLocal(string key, object value) { if (Bookmark != null) Bookmark.Locals[key] = value; }
        public void ClearLocal(string key) { if (Bookmark != null) Bookmark.Locals.Remove(key); }
    }

    // A node in a Screenplay graph. Subclass to add behaviour; games can add their own Page types (they're
    // [SerializeReference] polymorphic, like rules), e.g. a TrueEye page that injects a bespoke GameRule.
    //
    // Lifecycle while a Bookmark is on this Page:
    //   Enter(ctx) → returns the OUTPUT PORT to advance along, or null to PARK (wait).
    //   Tick(ctx)  → polled each frame while parked; return a port to advance, or null to keep waiting.
    //   Exit(ctx)  → called once just before advancing away (after a parked wait resolves).
    // Special return values: Page.Stop ends this bookmark; Page.All takes every outgoing edge (Fork).
    [System.Serializable]
    public abstract class Page : IGraphNode<StoryContext>
    {
        [SerializeField] string m_id;
        public string Id { get => m_id; set => m_id = value; }

        public string Title;
        public Vector2 GraphPos; // editor canvas position

        // Sentinels (Loom values) — kept as Page.Stop / Page.All so existing pages read unchanged.
        public const string Stop = GraphPort.Stop;
        public const string All = GraphPort.All;

        static readonly string[] DefaultPorts = { "out" };
        public virtual IReadOnlyList<string> Ports => DefaultPorts;

        public abstract string Enter(StoryContext ctx);
        public virtual string Tick(StoryContext ctx) => null;
        public virtual void Exit(StoryContext ctx) { }

        // A one-line, value-aware summary (editor / debug). Mirrors GameRule.Describe.
        public virtual string Describe() => GetType().Name;
    }
}
