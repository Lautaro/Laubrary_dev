using System;
using System.Reflection;
using Laubrary.Rulesets;

namespace Laubrary.Story
{
    // A reference IRuleBridge implementation over Laubrary.Rulesets' own RulesHost. Since both Rulesets and
    // Story are canonical Laubrary modules, this bridge needs no game-specific knowledge at all — rules are
    // addressed by their type name, parameters by public field name via reflection. Written in the
    // Laubrary.Story namespace and kept dependency-free (only Rulesets + reflection) so it's copy-paste
    // portable into Runtime/Story once proven here.
    public class RulesHostRuleBridge : IRuleBridge
    {
        static GameRule Find(string ruleId)
        {
            var host = RulesHost.Active;
            if (host == null || host.Rules == null || string.IsNullOrEmpty(ruleId)) return null;
            for (int i = 0; i < host.Rules.Count; i++)
            {
                var r = host.Rules[i];
                if (r != null && r.GetType().Name == ruleId) return r;
            }
            return null;
        }

        public bool IsActive(string ruleId) { var r = Find(ruleId); return r != null && r.Active; }

        public void SetActive(string ruleId, bool on) { var r = Find(ruleId); if (r != null) r.Active = on; }

        public object GetParam(string ruleId, string field)
        {
            var r = Find(ruleId);
            if (r == null || string.IsNullOrEmpty(field)) return null;
            var fi = r.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            return fi?.GetValue(r);
        }

        public void SetParam(string ruleId, string field, object value)
        {
            var r = Find(ruleId);
            if (r == null || string.IsNullOrEmpty(field)) return;
            var fi = r.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (fi == null) return;
            try { fi.SetValue(r, Convert.ChangeType(value, fi.FieldType)); } catch { /* best-effort */ }
        }

        public IDisposable Inject(object serializedRule, bool replaceSingleton)
        {
            var host = RulesHost.Active;
            var rule = serializedRule as GameRule;
            if (host == null || rule == null) return new Noop();

            GameRule replaced = null;
            bool replacedPrevActive = false;
            if (replaceSingleton && !rule.AllowMultiple)
            {
                for (int i = 0; i < host.Rules.Count; i++)
                {
                    var ex = host.Rules[i];
                    if (ex != null && ex.GetType() == rule.GetType() && ex.Active)
                    {
                        replaced = ex; replacedPrevActive = true; ex.Active = false; break;
                    }
                }
            }

            rule.Active = true;
            host.AddLive(rule);

            return new Patch(host, rule, replaced, replacedPrevActive);
        }

        sealed class Noop : IDisposable { public void Dispose() { } }

        sealed class Patch : IDisposable
        {
            readonly RulesHost _h; readonly GameRule _r, _replaced; readonly bool _prev;
            public Patch(RulesHost h, GameRule r, GameRule replaced, bool prev) { _h = h; _r = r; _replaced = replaced; _prev = prev; }
            public void Dispose()
            {
                if (_r != null)
                {
                    _r.Active = false;
                    if (_h != null) _h.Remove(_r);
                }
                if (_replaced != null) _replaced.Active = _prev;
            }
        }
    }
}
