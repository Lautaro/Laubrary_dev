using System;
using System.Collections.Generic;

namespace Laubrary.Story
{
    // ── Player-communication data model ────────────────────────────────────────────────────────────
    // The Story layer emits a Message; the game's IMessagePresenter renders it. An option-less Message is a
    // plain toast / banner / confirm; a Message carrying ≥2 Options is a Decision. The Story never touches a
    // concrete UI toolkit — that's the presenter's job — so IMGUI / uGUI / UI Toolkit is a presenter swap.

    public enum MessageKind { Toast, Banner, Popup, Dialogue }

    [Serializable]
    public class DecisionOption
    {
        public string Id;
        public string Label;
        public bool Enabled = true;

        public DecisionOption() { }
        public DecisionOption(string id, string label) { Id = id; Label = label; }
    }

    public class Message
    {
        public string Title;
        public string Body;
        public MessageKind Kind = MessageKind.Popup;
        public List<DecisionOption> Options = new List<DecisionOption>();

        public bool HasOptions => Options != null && Options.Count > 0;
    }

    // The game supplies this. Present shows a Message and, when the player resolves it, calls onResult with
    // the chosen Option.Id (or null for an option-less confirm/dismiss). Must invoke onResult on the main
    // thread (e.g. from OnGUI / Update).
    public interface IMessagePresenter
    {
        void Present(Message message, Action<string> onResult);
        void ShowBanner(string text);
        void HideBanner();
    }

    // The game supplies this over its Rules system. The Story layer only knows rules by string id (the rule's
    // type name in TrueEye) and field name — it never references concrete rule types, which is what keeps the
    // whole layer game-agnostic and reusable from Laubrary.
    public interface IRuleBridge
    {
        bool IsActive(string ruleId);
        object GetParam(string ruleId, string field);
        void SetActive(string ruleId, bool on);
        void SetParam(string ruleId, string field, object value);
        // Inject a bespoke, already-built rule instance into the live host. Returns a handle that removes it
        // again (and restores any single-instance rule it replaced) when disposed.
        IDisposable Inject(object serializedRule, bool replaceSingleton);
    }

    // Optional: lets a WaitConditionPage ask the game a yes/no or numeric question ("gold>=70") without the
    // Story layer knowing anything about the game's world. Games that don't need polled conditions can leave
    // this null and rely on events (Notifyer) instead.
    public interface IConditionSource
    {
        bool Evaluate(string expression);
        float Value(string key);
    }

    // Runtime state store for a run: flags, counters, the option the player picked, etc. Scene-scoped by
    // default; a campaign-scoped Journal can be saved by the game (deferred). Plain runtime object — not
    // serialized into the Screenplay asset.
    public class Journal
    {
        readonly Dictionary<string, string> _m = new Dictionary<string, string>();

        public bool Has(string key) => key != null && _m.ContainsKey(key);
        public void Set(string key, object value) { if (key != null) _m[key] = value?.ToString() ?? ""; }
        public void Clear(string key) { if (key != null) _m.Remove(key); }

        public string GetString(string key, string def = "") => key != null && _m.TryGetValue(key, out var v) ? v : def;
        public int GetInt(string key, int def = 0) => key != null && _m.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;
        public float GetFloat(string key, float def = 0f) => key != null && _m.TryGetValue(key, out var v) && float.TryParse(v, out var n) ? n : def;
        public bool GetBool(string key, bool def = false)
            => key != null && _m.TryGetValue(key, out var v) ? (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)) : def;

        public IReadOnlyDictionary<string, string> All => _m;
    }
}
