using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Laubrary.Story
{
    // ── The built-in, game-agnostic Page palette ────────────────────────────────────────────────────────
    // Games extend this with bespoke pages (e.g. TrueEye's InjectRulePage). Each page below depends only on
    // the StoryContext interfaces (presenter / rule-bridge / conditions / journal) + Notifyer, never on a game.

    // Where a Screenplay begins. Pure pass-through.
    [System.Serializable]
    public class EntryPage : Page
    {
        public override string Enter(StoryContext ctx) => "out";
        public override string Describe() => "Entry";
    }

    // Terminates the bookmark that reaches it.
    [System.Serializable]
    public class EndPage : Page
    {
        static readonly string[] NoPorts = new string[0];
        public override IReadOnlyList<string> Ports => NoPorts;
        public override string Enter(StoryContext ctx) => Stop;
        public override string Describe() => "End";
    }

    // Show a one-way Message (toast / banner / popup). Popups optionally block until dismissed.
    [System.Serializable]
    public class MessagePage : Page
    {
        [TextArea] public string Body;
        public MessageKind Kind = MessageKind.Popup;
        public bool WaitForDismiss = true;

        public override string Enter(StoryContext ctx)
        {
            if (ctx.Presenter == null) return "out";
            var msg = new Message { Title = Title, Body = Body, Kind = Kind };
            if (!WaitForDismiss || Kind == MessageKind.Toast || Kind == MessageKind.Banner)
            {
                ctx.Presenter.Present(msg, _ => { });
                return "out";
            }
            var bm = ctx.Bookmark;
            bm.Locals[Id + ":done"] = false;
            ctx.Presenter.Present(msg, _ => bm.Locals[Id + ":done"] = true);
            return null;
        }

        public override string Tick(StoryContext ctx) => ctx.GetLocal(Id + ":done", false) ? "out" : null;
        public override string Describe() => $"Message: \"{Title}\"";
    }

    // Show a Message with options and branch on the player's pick. Each Option.Id is an output port.
    [System.Serializable]
    public class DecisionPage : Page
    {
        [TextArea] public string Body;
        public MessageKind Kind = MessageKind.Dialogue;
        public List<DecisionOption> Options = new List<DecisionOption>();
        public string JournalKey; // optional: store the chosen Option.Id here

        public override IReadOnlyList<string> Ports
        {
            get
            {
                var l = new List<string>();
                if (Options != null) foreach (var o in Options) if (o != null) l.Add(o.Id);
                return l;
            }
        }

        public override string Enter(StoryContext ctx)
        {
            if (ctx.Presenter == null) return (Options != null && Options.Count > 0) ? Options[0].Id : Stop;
            var msg = new Message { Title = Title, Body = Body, Kind = Kind, Options = Options };
            var bm = ctx.Bookmark;
            bm.Locals[Id + ":pick"] = null;
            ctx.Presenter.Present(msg, id => bm.Locals[Id + ":pick"] = id);
            return null;
        }

        public override string Tick(StoryContext ctx)
        {
            var pick = ctx.GetLocal<string>(Id + ":pick", null);
            if (string.IsNullOrEmpty(pick)) return null;
            if (!string.IsNullOrEmpty(JournalKey)) ctx.Journal?.Set(JournalKey, pick);
            return pick;
        }

        public override string Describe() => $"Decision: \"{Title}\" ({Options?.Count ?? 0} options)";
    }

    // Park until a Notifyer string event fires.
    [System.Serializable]
    public class WaitEventPage : Page
    {
        public string EventId;

        public override string Enter(StoryContext ctx)
        {
            var bm = ctx.Bookmark;
            bm.Locals[Id + ":fired"] = false;
            UnityAction h = () => bm.Locals[Id + ":fired"] = true;
            bm.Locals[Id + ":handler"] = h;
            Laubrary.Notifyer.Notifyer.Subscribe(EventId, h);
            return null;
        }

        public override string Tick(StoryContext ctx) => ctx.GetLocal(Id + ":fired", false) ? "out" : null;

        public override void Exit(StoryContext ctx)
        {
            var h = ctx.GetLocal<UnityAction>(Id + ":handler", null);
            if (h != null) Laubrary.Notifyer.Notifyer.Unsubscribe(EventId, h);
            ctx.ClearLocal(Id + ":handler");
        }

        public override string Describe() => $"Wait for event \"{EventId}\"";
    }

    // Park until the game's IConditionSource says the expression is true.
    [System.Serializable]
    public class WaitConditionPage : Page
    {
        public string Expression;
        public override string Enter(StoryContext ctx) => Check(ctx) ? "out" : null;
        public override string Tick(StoryContext ctx) => Check(ctx) ? "out" : null;
        bool Check(StoryContext ctx) => ctx.Conditions != null && ctx.Conditions.Evaluate(Expression);
        public override string Describe() => $"Wait until [{Expression}]";
    }

    // Park for N seconds (scaled time — pauses while the game is paused).
    [System.Serializable]
    public class DelayPage : Page
    {
        public float Seconds = 1f;
        public override string Enter(StoryContext ctx) { ctx.SetLocal(Id + ":t", 0f); return null; }
        public override string Tick(StoryContext ctx)
        {
            float t = ctx.GetLocal(Id + ":t", 0f) + Time.deltaTime;
            ctx.SetLocal(Id + ":t", t);
            return t >= Seconds ? "out" : null;
        }
        public override string Describe() => $"Delay {Seconds}s";
    }

    // Write a value into the Journal.
    [System.Serializable]
    public class SetJournalPage : Page
    {
        public string Key;
        public string Value;
        public override string Enter(StoryContext ctx) { ctx.Journal?.Set(Key, Value); return "out"; }
        public override string Describe() => $"Set {Key} = {Value}";
    }

    // Branch on a Journal value. Ports: "true" / "false".
    [System.Serializable]
    public class BranchPage : Page
    {
        public enum Comparison { Equals, NotEquals, Greater, GreaterEqual, Less, LessEqual }
        public string Key;
        public Comparison Op = Comparison.Equals;
        public string Value;

        static readonly string[] _ports = { "true", "false" };
        public override IReadOnlyList<string> Ports => _ports;

        public override string Enter(StoryContext ctx) => Eval(ctx) ? "true" : "false";

        bool Eval(StoryContext ctx)
        {
            var j = ctx.Journal;
            if (j == null) return false;
            if (Op == Comparison.Equals) return j.GetString(Key) == Value;
            if (Op == Comparison.NotEquals) return j.GetString(Key) != Value;
            float a = j.GetFloat(Key);
            float.TryParse(Value, out float b);
            switch (Op)
            {
                case Comparison.Greater: return a > b;
                case Comparison.GreaterEqual: return a >= b;
                case Comparison.Less: return a < b;
                case Comparison.LessEqual: return a <= b;
            }
            return false;
        }

        public override string Describe() => $"If {Key} {Op} {Value}";
    }

    // Mutate the live rules: enable/disable a rule or set one of its parameters. Each op is recorded as a
    // reversible undo on the Runner's patch stack (unwound on teardown). For injecting a whole bespoke rule
    // instance, see the game's own InjectRule page.
    [System.Serializable]
    public class PlotTwistPage : Page
    {
        public enum OpKind { Enable, Disable, SetParam }

        [System.Serializable]
        public class Op
        {
            public OpKind Kind;
            public string RuleId;   // rule type name (game-defined)
            public string Field;    // for SetParam
            public string Value;    // for SetParam
        }

        public List<Op> Ops = new List<Op>();

        public override string Enter(StoryContext ctx)
        {
            var br = ctx.Rules;
            if (br == null || Ops == null) return "out";
            foreach (var op in Ops)
            {
                if (op == null || string.IsNullOrEmpty(op.RuleId)) continue;
                switch (op.Kind)
                {
                    case OpKind.Enable:
                    {
                        bool prev = br.IsActive(op.RuleId);
                        br.SetActive(op.RuleId, true);
                        ctx.Runner?.PushUndo(() => br.SetActive(op.RuleId, prev));
                        break;
                    }
                    case OpKind.Disable:
                    {
                        bool prev = br.IsActive(op.RuleId);
                        br.SetActive(op.RuleId, false);
                        ctx.Runner?.PushUndo(() => br.SetActive(op.RuleId, prev));
                        break;
                    }
                    case OpKind.SetParam:
                    {
                        object prev = br.GetParam(op.RuleId, op.Field);
                        br.SetParam(op.RuleId, op.Field, op.Value);
                        ctx.Runner?.PushUndo(() => br.SetParam(op.RuleId, op.Field, prev));
                        break;
                    }
                }
            }
            return "out";
        }

        public override string Describe() => $"PlotTwist ({Ops?.Count ?? 0} rule op(s))";
    }

    // Take every outgoing edge at once (concurrent branches, each its own Bookmark).
    [System.Serializable]
    public class ForkPage : Page
    {
        public override string Enter(StoryContext ctx) => All;
        public override string Describe() => "Fork";
    }
}
