using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GV = UnityEditor.Experimental.GraphView;

namespace Laubrary.GraphViewKit
{
    // ── A universal reroute/waypoint node for ANY GraphView ───────────────────────────────────────────────────
    // A tiny drag-able bend point for a wire (the classic Shader-Graph/Blueprint "redirect" node), with no
    // dependency on any particular graph/node/asset model — just GV.Node + GV.Port. Drop one into any
    // UnityEditor.Experimental.GraphView.GraphView-based tool to get:
    //
    //   • Two direction-agnostic connector slots. Both start neutral (either accepts either direction). The
    //     first wire that lands on one locks it to that direction and forces the other slot to the opposite;
    //     the lock persists even if one side later disconnects, and only clears once BOTH slots are empty again
    //     (GraphView bakes a Port's Direction at creation with no way to change it, so this is implemented as
    //     two overlapping Port objects per slot with only the "live" one enabled/visible at a time).
    //   • Click (not drag) a slot's connector to flip that slot's Orientation between Horizontal/Vertical,
    //     changing the bezier tangent a wire takes leaving that connector (Orientation is also baked in at
    //     Port creation, so this destroys and recreates that slot's ports, preserving whatever edge was
    //     attached by handing the replacement back to the host via EdgeReplaced).
    //
    // The host GraphView owns persistence and edge bookkeeping; this class only manages its own four Port
    // objects and tells the host (via events) whenever something it can't do itself needs doing:
    //   - RefreshRole() — call after any external edge connect/disconnect that may touch this node's ports.
    //   - EdgeReplaced  — fires when an orientation click rebuilds a slot; AddElement the given edge and wire up
    //                     whatever bookkeeping (double-click handlers, save triggers) your host needs.
    //   - Changed       — fires after any internal topology change (role lock/unlock, orientation flip) so the
    //                     host can re-save / re-render viz.
    public class UniversalRerouteNode : GV.Node
    {
        public const float Height = 16f;
        public const float HalfWidth = 18f;   // each slot gets its own half — squeezing both ports into one
                                                // narrow spot made their connectors overlap into a stray glyph.
        public static readonly Vector2 Size = new Vector2(HalfWidth * 2f, Height);
        const float ClickDragThreshold = 4f;   // px of movement between mouse-down/up before it counts as a drag, not a click

        public enum Role { Neutral, AInBOut, AOutBIn }
        public Role CurrentRole { get; private set; } = Role.Neutral;

        GV.Port _aIn, _aOut, _bIn, _bOut;
        bool _aVertical, _bVertical;

        Vector2 _mouseDownPos;
        bool _tracking;

        public event Action<GV.Edge> EdgeReplaced;
        public event Action Changed;

        public UniversalRerouteNode()
        {
            AddToClassList("lau-graph-reroute");
            // A reroute also serves non-Loom hosts, so it owns the sheet its pill chrome needs.
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Packages/Laubrary/Zui/Toolkit/ZuiFoundationToolShell.uss")
                ?? AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.lautaro.arino.laubrary/Zui/Toolkit/ZuiFoundationToolShell.uss");
            if (sheet != null) styleSheets.Add(sheet);
            titleContainer.AddToClassList("lau-graph-reroute__hidden");
            extensionContainer.AddToClassList("lau-graph-reroute__hidden");

            // The default Node chrome reserves a title-row's worth of height above the port row, so the port's
            // actual connector (what a wire anchors to) sits well above this pill's visual center. Pin every
            // layer to the same fixed band, centered, so the connector lines up with the visible shape.
            Squash(mainContainer, fullWidth: true);
            Squash(inputContainer, fullWidth: false);
            Squash(outputContainer, fullWidth: false);

            BuildSlotPorts(slotA: true, GV.Orientation.Horizontal);
            BuildSlotPorts(slotA: false, GV.Orientation.Horizontal);

            // Observe mouse down/up WITHOUT stopping propagation on down — GraphView's own SelectionDragger
            // needs that same mouse-down to start a real drag. We only act (and only then stop propagation) on
            // mouse-up, and only if the pointer barely moved — i.e. it was a click, not a completed drag.
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseUpEvent>(OnMouseUp);

            RefreshRole();
            RefreshExpandedState();
            RefreshPorts();
        }

        // ── Public port access for a host wiring/loading connections ──────────────────────────────────────
        // Whichever of a slot's two sub-ports is currently the "live" one for its locked role (or, when
        // Neutral, either — both are equally valid, so just hand back the input-typed one as the default).
        public GV.Port SlotAActivePort => CurrentRole == Role.AOutBIn ? _aOut : _aIn;
        public GV.Port SlotBActivePort => CurrentRole == Role.AInBOut ? _bOut : _bIn;

        // Every port this node owns, for host code that needs to check "does any edge touch this node"
        // (e.g. finding what's attached before deleting it) without caring about the current role.
        public IEnumerable<GV.Port> AllPorts { get { yield return _aIn; yield return _aOut; yield return _bIn; yield return _bOut; } }

        // Given a port that arrived AT this node, return the port on the OTHER slot to continue a chain-walk.
        public GV.Port OtherSlotPort(GV.Port arrivedAt)
        {
            bool viaA = arrivedAt == _aIn || arrivedAt == _aOut;
            return viaA ? SlotBActivePort : SlotAActivePort;
        }

        // Call after any external edge connect/disconnect touching this node (the host's graphViewChanged is
        // the natural place) so the locked role reacts to wires the user dragged by hand.
        public void RefreshRole()
        {
            bool anyConnected = _aIn.connected || _aOut.connected || _bIn.connected || _bOut.connected;
            var prev = CurrentRole;
            if (!anyConnected) CurrentRole = Role.Neutral;
            else if (CurrentRole == Role.Neutral)
            {
                if (_aIn.connected || _bOut.connected) CurrentRole = Role.AInBOut;
                else if (_aOut.connected || _bIn.connected) CurrentRole = Role.AOutBIn;
            }
            ApplyRoleVisibility();
            if (prev != CurrentRole) Changed?.Invoke();
        }

        void ApplyRoleVisibility()
        {
            bool neutral = CurrentRole == Role.Neutral;
            SetSub(_aIn, neutral || CurrentRole == Role.AInBOut);
            SetSub(_aOut, neutral || CurrentRole == Role.AOutBIn);
            SetSub(_bIn, neutral || CurrentRole == Role.AOutBIn);
            SetSub(_bOut, neutral || CurrentRole == Role.AInBOut);
        }

        static void SetSub(GV.Port p, bool active)
        {
            p.SetEnabled(active);
            p.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Click (not drag) a slot's connector to toggle its orientation ─────────────────────────────────
        void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0) return;
            _mouseDownPos = evt.mousePosition;
            _tracking = true;
            // deliberately no StopPropagation — the drag manipulator needs this same event
        }

        void OnMouseUp(MouseUpEvent evt)
        {
            if (!_tracking) return;
            _tracking = false;
            if (evt.button != 0) return;
            if (Vector2.Distance(_mouseDownPos, evt.mousePosition) > ClickDragThreshold) return;   // was a drag

            bool slotA = evt.localMousePosition.x < Size.x / 2f;
            ToggleOrientation(slotA);
            evt.StopPropagation();
        }

        void ToggleOrientation(bool slotA)
        {
            if (slotA) _aVertical = !_aVertical; else _bVertical = !_bVertical;
            var orientation = (slotA ? _aVertical : _bVertical) ? GV.Orientation.Vertical : GV.Orientation.Horizontal;

            var container = slotA ? inputContainer : outputContainer;
            var oldIn = slotA ? _aIn : _bIn;
            var oldOut = slotA ? _aOut : _bOut;

            // Orientation is baked in at Port.Create, same as Direction — preserve whatever edge is live on
            // this slot (at most one, both sub-ports are Capacity.Single) by handing a replacement back to the
            // host once the new ports exist.
            GV.Edge existingEdge = null; GV.Port otherEnd = null; bool otherEndIsOutput = false;
            foreach (var e in oldIn.connections) { existingEdge = e; otherEnd = e.output; otherEndIsOutput = true; break; }
            if (existingEdge == null)
                foreach (var e in oldOut.connections) { existingEdge = e; otherEnd = e.input; otherEndIsOutput = false; break; }

            if (existingEdge != null)
            {
                existingEdge.output?.Disconnect(existingEdge);
                existingEdge.input?.Disconnect(existingEdge);
                existingEdge.RemoveFromHierarchy();
            }
            container.Remove(oldIn); container.Remove(oldOut);

            BuildSlotPorts(slotA, orientation);

            if (existingEdge != null && otherEnd != null)
            {
                var replacement = otherEndIsOutput ? otherEnd.ConnectTo(slotA ? _aIn : _bIn) : (slotA ? _aOut : _bOut).ConnectTo(otherEnd);
                EdgeReplaced?.Invoke(replacement);
            }

            RefreshRole();
            RefreshPorts();
            Changed?.Invoke();
        }

        void BuildSlotPorts(bool slotA, GV.Orientation orientation)
        {
            var container = slotA ? inputContainer : outputContainer;
            var inPort = MakePort(GV.Direction.Input, slotA, orientation);
            var outPort = MakePort(GV.Direction.Output, slotA, orientation);
            container.Add(inPort);
            container.Add(outPort);
            if (slotA) { _aIn = inPort; _aOut = outPort; } else { _bIn = inPort; _bOut = outPort; }
        }

        // Both sub-ports at a slot must render at the SAME edge of the pill regardless of which one is
        // currently active (or both, when neutral) — slot A always at the node's left tip, slot B at its right
        // tip — so we override the connector's default direction-based alignment explicitly. Orientation only
        // changes the wire's bezier tangent at this connector; it does not move the connector itself.
        static GV.Port MakePort(GV.Direction dir, bool slotA, GV.Orientation orientation)
        {
            var p = GV.Port.Create<GV.Edge>(orientation, dir, GV.Port.Capacity.Single, typeof(bool));
            p.portName = "";
            p.AddToClassList("lau-graph-reroute__port");
            p.AddToClassList(slotA ? "lau-graph-reroute__port--a" : "lau-graph-reroute__port--b");
            var lbl = p.Q<Label>();
            if (lbl != null) lbl.AddToClassList("lau-graph-reroute__hidden");
            return p;
        }

        static void Squash(VisualElement e, bool fullWidth)
        {
            e.AddToClassList("lau-graph-reroute__band");
            e.AddToClassList(fullWidth ? "lau-graph-reroute__band--main" : "lau-graph-reroute__band--slot");
        }
    }
}
