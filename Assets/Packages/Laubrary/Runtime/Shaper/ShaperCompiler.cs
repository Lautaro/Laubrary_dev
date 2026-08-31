using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// Flattens the authored tree into a <see cref="ShaperProgram"/>. Run once per frame-time, never per
    /// sample.
    ///
    /// It does three things the reference app does not, and each fixes a named defect:
    /// <list type="number">
    /// <item>It accumulates the full root→leaf inverse transform per leaf, so a leaf is evaluated by mapping
    /// the canvas point straight into that leaf's own local space with one precomputed 2×3. No point stack,
    /// no per-node re-transform.</item>
    /// <item>It stores, per leaf, <c>distanceScale = σ_min(M)</c> of the accumulated forward linear part, and
    /// the leaf's local distance is multiplied by it on the way out. The reference app's <c>evalShape</c>
    /// returns the child's raw value across a transform, which is the whole of its 5.00× over-report at
    /// <c>scale.x = 0.2</c>.</item>
    /// <item>It emits post-order RPN, so evaluation is a flat loop over ops against a small value stack.</item>
    /// </list>
    /// </summary>
    public static class ShaperCompiler
    {
        struct Box
        {
            public bool valid;
            public float minX, minY, maxX, maxY;

            public static Box Invalid => default;

            public static Box FromCentre(float cx, float cy, float hw, float hh)
                => new Box { valid = true, minX = cx - hw, minY = cy - hh, maxX = cx + hw, maxY = cy + hh };

            public Box Union(in Box o)
            {
                if (!valid) return o;
                if (!o.valid) return this;
                return new Box
                {
                    valid = true,
                    minX = Mathf.Min(minX, o.minX), minY = Mathf.Min(minY, o.minY),
                    maxX = Mathf.Max(maxX, o.maxX), maxY = Mathf.Max(maxY, o.maxY),
                };
            }

            public Box Intersect(in Box o)
            {
                if (!valid || !o.valid) return Invalid;
                float nx = Mathf.Max(minX, o.minX), ny = Mathf.Max(minY, o.minY);
                float xx = Mathf.Min(maxX, o.maxX), xy = Mathf.Min(maxY, o.maxY);
                if (nx > xx || ny > xy) return Invalid;
                return new Box { valid = true, minX = nx, minY = ny, maxX = xx, maxY = xy };
            }

            public Box Grow(float m)
            {
                if (!valid) return this;
                return new Box { valid = true, minX = minX - m, minY = minY - m, maxX = maxX + m, maxY = maxY + m };
            }

            public float CentreX => (minX + maxX) * 0.5f;
            public float CentreY => (minY + maxY) * 0.5f;
            public float HalfW => (maxX - minX) * 0.5f;
            public float HalfH => (maxY - minY) * 0.5f;

            /// <summary>
            /// Carry this box through an affine map by the absolute-value corner sum, producing the smallest
            /// axis-aligned box that contains the mapped one.
            ///
            /// Used ONLY with a RELATIVE (child-to-parent) matrix when folding the node-local support box of
            /// FC-1.5a. That restriction is the whole point: applied with the ACCUMULATED forward it would
            /// reproduce the canvas box's rotation-dependent inflation, which is exactly what the local box
            /// exists to avoid. A child rotated inside its bag genuinely does change the bag's local extent, so
            /// the inflation is correct at this scope; the node's OWN rotation never enters, so its own box is
            /// stable.
            /// </summary>
            public Box Map(in ShaperMatrix m)
            {
                if (!valid) return this;
                float cx = CentreX, cy = CentreY, hw = HalfW, hh = HalfH;
                float nx = m.m00 * cx + m.m01 * cy + m.m02;
                float ny = m.m10 * cx + m.m11 * cy + m.m12;
                float nhw = Mathf.Abs(m.m00) * hw + Mathf.Abs(m.m01) * hh;
                float nhh = Mathf.Abs(m.m10) * hw + Mathf.Abs(m.m11) * hh;
                return FromCentre(nx, ny, nhw, nhh);
            }
        }

        struct Emitted
        {
            public float bound;
            public Box box;
            public ShaperSweepAxis sweepAxis;

            /// <summary>The support box in the emitting node's OWN local frame (FC-1.5a).</summary>
            public Box localBox;
            /// <summary>This node's own transform block, i.e. the map from its local frame to its parent's.</summary>
            public ShaperMatrix localToParent;
            /// <summary>The accumulated root→node inverse, for the fill stage's Stamped anchor (FC-1.5).</summary>
            public ShaperMatrix inverse;
            public bool invertible;

            /// <summary>
            /// The worst ratio <c>σ_max / σ_min</c> of the accumulated forward map anywhere in this subtree —
            /// the factor by which the published distance can UNDER-report the true canvas distance.
            ///
            /// <b>Why it exists, and what it is for.</b> Every node publishes <c>σ_min · d_local</c>
            /// (<see cref="ShaperMatrix.SigmaMin"/>), which is a conservative under-estimate: on an
            /// anisotropically scaled member a REPORTED distance of <c>r</c> is reached as far as
            /// <c>r · σ_max/σ_min</c> canvas pixels from the boundary along the stretched axis. So any operator
            /// that grows the support box by a distance expressed in the FIELD's units — <c>Shell</c>'s
            /// thickness, a joining border's reach (BD-2.4) — must grow the CANVAS box by that distance times
            /// this factor, or the box excludes real samples. Measured before this was carried: an Outward
            /// border of reach 8 on a member scaled (2.0, 0.5) put 808 samples of its own silhouette OUTSIDE
            /// the declared box, overshooting it by 23.5 px.
            ///
            /// It is a MAX over the subtree rather than the node's own transform, because a bag with an
            /// identity transform inherits its child's anisotropy: the same measurement on a bag wrapping that
            /// member reproduced the same 808 samples and the same 23.5 px.
            ///
            /// Read through <see cref="Spread"/>, never directly: <c>Emitted</c> is a struct, so an
            /// unset field is 0, and a 0 here would silently multiply every box growth to nothing.
            /// </summary>
            public float spreadRaw;
            /// <summary>Never below 1. See <see cref="spreadRaw"/> for why the property and not the field.</summary>
            public float Spread => spreadRaw >= 1f ? spreadRaw : 1f;

            /// <summary>
            /// <c>σ_min</c> of the accumulated forward map at THIS node — the number that converts a canvas
            /// distance into this node's own local units. Published so a strip can grow its node-local anchor
            /// box the way <see cref="EmitShell"/> already grows a shell's (BD-2.4 / FC-1.5a).
            /// Read through <see cref="SigmaMin"/>, for the same struct-default reason as above.
            /// </summary>
            public float sigmaMinRaw;
            /// <summary>Never zero or negative — it is divided by. See <see cref="sigmaMinRaw"/>.</summary>
            public float SigmaMin => sigmaMinRaw > 1e-9f ? sigmaMinRaw : 1f;
        }

        class State
        {
            public List<ShaperOp> ops = new List<ShaperOp>();
            public ShaperProgram program = new ShaperProgram();
            public int depth;
            public int maxDepth;
            public float phase01;
            public uint seed;

            public void Push(int n = 1) { depth += n; if (depth > maxDepth) maxDepth = depth; }
            public void Pop(int n = 1) { depth -= n; }
        }

        /// <summary>
        /// Compile a tree for one normalised frame time, in the layer frame.
        ///
        /// Kept EXACTLY as shipped — signature, defaults and behaviour — because the field audit and every
        /// existing caller use it. It now delegates to the <c>parentForward</c> overload with
        /// <see cref="ShaperMatrix.Identity"/>, which is the value it used to hardcode inline.
        /// </summary>
        public static ShaperProgram Compile(ShaperNode root, float phase01 = 0f, uint seed = 0u)
            => Compile(root, ShaperMatrix.Identity, phase01, seed);

        /// <summary>
        /// Compile a tree for one normalised frame time, seeded with a starting forward matrix.
        ///
        /// <b>The one addition the fill stage requires of the shape stage (FILL-CONTRACT FC-3.9).</b> Resolving
        /// a fill-owning node's own coverage means evaluating that node's subtree standalone, and
        /// <c>ShaperEvaluator.Distance</c> returns only <c>stack[0]</c> (<c>ShaperEvaluator.cs:113</c>) while
        /// <c>ShaperOp</c> carries no tap — so an intermediate node's coverage cannot be extracted from a
        /// whole-tree program. <c>Compile</c> already accepts ANY node as a root, which makes one program per
        /// fill-owning node nearly free; the only catch was that it seeded Identity, so a subtree compiled
        /// standalone landed in its own local frame rather than the layer frame. This overload takes the
        /// accumulated forward of the node's PARENT, which is the same thing <c>EmitNode</c> already accepts.
        ///
        /// The route not taken was tapping the RPN stack at named ops. It is cheaper at evaluation time — one
        /// walk instead of k — but it costs a per-op tap index, a tap output array and a rule about what the
        /// stack holds at each op, and it couples the fill stage to the RPN layout. This route needs one
        /// parameter instead of a mechanism, each program is independently cacheable under R7's rules, and each
        /// program carries its own support box so a fill's paint pass is bounded by the pixels it can affect.
        /// The honest cost: k fill-owning nodes means k+1 field evaluations at a sample covered by all of them.
        /// </summary>
        public static ShaperProgram Compile(ShaperNode root, in ShaperMatrix parentForward,
                                            float phase01 = 0f, uint seed = 0u)
        {
            var st = new State { phase01 = Mathf.Clamp01(phase01), seed = seed };
            st.program.phase01 = st.phase01;

            Emitted top;
            if (root == null || !root.enabled)
            {
                EmitEmpty(st);
                top = new Emitted
                {
                    bound = 1f,
                    box = Box.Invalid,
                    sweepAxis = ShaperSweepAxis.Radial,
                    localBox = Box.Invalid,
                    localToParent = ShaperMatrix.Identity,
                    inverse = ShaperMatrix.Identity,
                    invertible = false,
                    spreadRaw = 1f,
                    sigmaMinRaw = 1f,
                };
            }
            else
            {
                top = EmitNode(root, parentForward, st, true);
            }

            st.program.ops = st.ops.ToArray();
            st.program.stackDepth = Mathf.Max(1, st.maxDepth);
            st.program.bound = top.bound;
            if (top.box.valid)
            {
                st.program.supportCx = top.box.CentreX;
                st.program.supportCy = top.box.CentreY;
                st.program.supportHalfW = top.box.HalfW;
                st.program.supportHalfH = top.box.HalfH;
            }

            // BD-2.4: the factor by which a distance expressed in the FIELD's units must be multiplied to bound
            // the same distance in CANVAS units, and this root's own σ_min. Both are published so that a strip
            // compiled OUTSIDE the compiler (ShaperBorder.CompileStrip, the unjoined path) grows the same two
            // boxes by the same two numbers the compiler would have used.
            st.program.supportSpread = top.Spread;
            st.program.rootSigmaMin = top.SigmaMin;

            // FC-1.5a: the node-local support box and the accumulated inverse, for the fill stage's anchor.
            st.program.rootInverse = top.inverse;
            st.program.rootInvertible = top.invertible;
            st.program.hasLocalSupport = top.localBox.valid;
            if (top.localBox.valid)
            {
                st.program.localSupportCx = top.localBox.CentreX;
                st.program.localSupportCy = top.localBox.CentreY;
                st.program.localSupportHalfW = top.localBox.HalfW;
                st.program.localSupportHalfH = top.localBox.HalfH;
            }
            return st.program;
        }

        static void EmitEmpty(State st)
        {
            st.ops.Add(new ShaperOp { kind = ShaperOpKind.Empty, bound = 1f, distanceScale = 1f });
            st.Push();
        }

        /// <param name="isRoot">
        /// True only for the node <see cref="Compile"/> was handed. It is needed for exactly one reason —
        /// BD-3.7's refusal of a border on a Subtract MEMBER, where a root's <see cref="ShaperNode.mode"/> is
        /// ignored data and must not silence the root's own border.
        /// </param>
        static Emitted EmitNode(ShaperNode node, in ShaperMatrix parentForward, State st, bool isRoot = false)
        {
            var block = node.transform ?? new ShaperTransformBlock();
            ShaperMatrix localToParent = block.ToMatrix();
            ShaperMatrix forward = ShaperMatrix.Mul(parentForward, localToParent);
            bool invertible = forward.TryInvert(out ShaperMatrix inverse);
            forward.SingularValues(out float sigmaMin, out float sigmaMax);

            Emitted e;
            if (!invertible || sigmaMin <= 1e-9f)
            {
                // A singular linear part publishes the empty field and is flagged. It must not divide.
                // EVERY offender is counted; only the first supplies the name.
                st.program.singularTransformCount++;
                if (!st.program.hasSingularTransform)
                {
                    st.program.hasSingularTransform = true;
                    st.program.singularNode = node.name;
                }
                EmitEmpty(st);
                e = new Emitted { bound = 1f, box = Box.Invalid, sweepAxis = ShaperSweepAxis.Radial,
                                  localBox = Box.Invalid, spreadRaw = 1f, sigmaMinRaw = 1f };
            }
            else if (node.kind == ShaperNodeKind.Primitive)
            {
                e = EmitLeaf(node, forward, inverse, sigmaMin, st);
                // BD-2.4: a leaf publishes σ_min · d_local, so a reported distance r is reached as far as
                // r · σ_max/σ_min canvas pixels out. That ratio is this subtree's whole under-report factor.
                e.spreadRaw = sigmaMax / sigmaMin;
            }
            else
            {
                e = EmitBag(node, forward, st);
                // EmitBag already carries the MAX over its members: a bag with an identity transform still
                // inherits an anisotropic child's under-report, which is the case that first exposed this.
            }

            // σ_min at THIS node, for the node-local box growth a strip needs (FC-1.5a).
            e.sigmaMinRaw = sigmaMin;

            e = EmitSweep(node, forward, inverse, sigmaMin, e, st);
            e = EmitShell(node, sigmaMin, e, st);
            e = EmitBorderJoin(node, isRoot, e, st);

            // Carried on the way out so a parent bag can fold this node's LOCAL box into its own frame, and so
            // the top-level Compile can publish the accumulated inverse for the fill anchor (FC-1.5a).
            e.localToParent = localToParent;
            e.inverse = invertible ? inverse : ShaperMatrix.Identity;
            e.invertible = invertible && sigmaMin > 1e-9f;
            return e;
        }

        static Emitted EmitLeaf(ShaperNode node, in ShaperMatrix forward, in ShaperMatrix inverse,
                                float sigmaMin, State st)
        {
            ShaperBakedPrimitive baked = ShaperPrimitives.Bake(node.primitive, st.phase01, st.seed);

            var op = new ShaperOp
            {
                kind = ShaperOpKind.Leaf,
                primitive = baked.kind,
                count = baked.count,
                p0 = baked.p0, p1 = baked.p1, p2 = baked.p2, p3 = baked.p3,
                p4 = baked.p4, p5 = baked.p5, p6 = baked.p6, p7 = baked.p7,
                p8 = baked.p8, p9 = baked.p9, p10 = baked.p10, p11 = baked.p11,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                bound = baked.bound,
            };

            // Canvas support box: the local half-extent box carried through the forward corner map.
            float hw = Mathf.Abs(forward.m00) * baked.halfExtentX + Mathf.Abs(forward.m01) * baked.halfExtentY;
            float hh = Mathf.Abs(forward.m10) * baked.halfExtentX + Mathf.Abs(forward.m11) * baked.halfExtentY;
            Box box = Box.FromCentre(forward.m02, forward.m12, hw, hh);

            op.boxCx = box.CentreX; op.boxCy = box.CentreY;
            op.boxHalfW = box.HalfW; op.boxHalfH = box.HalfH;

            st.ops.Add(op);
            st.Push();
            return new Emitted
            {
                bound = baked.bound,
                box = box,
                sweepAxis = baked.sweepAxis,
                // FC-1.5a: the primitive's OWN declared half-extents, centred on its own origin, with no
                // transform applied. These are fixed before any transform touches them, which is precisely what
                // makes the fill anchor rotation-invariant and scale-stable.
                localBox = Box.FromCentre(0f, 0f, baked.halfExtentX, baked.halfExtentY),
            };
        }

        static Emitted EmitBag(ShaperNode node, in ShaperMatrix forward, State st)
        {
            // The accumulator starts EMPTY. That single fact is what makes R1's leading-member rule fall out
            // with no special case: min(Empty, d) == d, max(Empty, −d) == Empty, max(Empty, d) == Empty.
            EmitEmpty(st);

            float bound = 1f;
            Box box = Box.Invalid;
            // The same fold, in the BAG's own local frame: each member's local box carried up through that
            // member's own transform block only (FC-1.5a). The bag's own transform never enters, so the bag's
            // local box does not change when the bag rotates.
            Box localBox = Box.Invalid;
            bool anyAxis = false;
            ShaperSweepAxis axis = ShaperSweepAxis.Radial;
            bool axisAgrees = true;
            bool first = true;
            float spread = 1f;                       // BD-2.4, the max over the members; see the fold below.

            var children = node.children;
            int n = children != null ? children.Count : 0;
            for (int i = 0; i < n; i++)
            {
                ShaperNode child = children[i];
                if (child == null || !child.enabled) continue;

                if (first)
                {
                    first = false;
                    if (child.mode != ShaperCombineMode.Add)
                    {
                        // EVERY offending bag is counted; only the first supplies the name. Recording just the
                        // first meant a tree with three offenders surfaced one, which defeats the flag.
                        st.program.leadingNonAddCount++;
                        if (!st.program.hasLeadingNonAdd)
                        {
                            st.program.hasLeadingNonAdd = true;
                            st.program.leadingNonAddNode = child.name;
                        }
                    }
                }

                Emitted ce = EmitNode(child, forward, st, false);

                if (!anyAxis) { axis = ce.sweepAxis; anyAxis = true; }
                else if (axis != ce.sweepAxis) axisAgrees = false;

                var blend = child.blend ?? new ShaperBlend();
                float width = Mathf.Max(0f, blend.width);
                float strength = Mathf.Clamp01(blend.carveStrength);

                Box combined;
                Box childLocal = ce.localBox.Map(ce.localToParent);
                Box combinedLocal;
                switch (child.mode)
                {
                    case ShaperCombineMode.Add:
                        combined = box.Union(ce.box);
                        combinedLocal = localBox.Union(childLocal);
                        break;
                    case ShaperCombineMode.Intersect:
                        combined = box.Intersect(ce.box);
                        combinedLocal = localBox.Intersect(childLocal);
                        break;
                    default:
                        combined = box;                 // Subtract never enlarges the accumulated box
                        combinedLocal = localBox;
                        break;
                }

                // The soft-subtract band is authored as a fraction of the operands' own size, matching the
                // reference app's reach = 0.55 · min(width, height).
                Box reachBox = box.Union(ce.box);
                float reach = reachBox.valid
                    ? 0.55f * Mathf.Min(reachBox.HalfW * 2f, reachBox.HalfH * 2f)
                    : 0f;

                st.ops.Add(new ShaperOp
                {
                    kind = ShaperOpKind.Combine,
                    mode = child.mode,
                    p0 = width,
                    p1 = ShaperOps.BlendExponent(blend.sharpness),
                    p2 = strength,
                    p3 = Mathf.Max(0f, reach),
                    bound = ShaperBound.Combine(child.mode, bound, ce.bound, width, strength),
                    distanceScale = 1f,
                    boxCx = combined.valid ? combined.CentreX : 0f,
                    boxCy = combined.valid ? combined.CentreY : 0f,
                    boxHalfW = combined.valid ? combined.HalfW : 0f,
                    boxHalfH = combined.valid ? combined.HalfH : 0f,
                });
                st.Pop();

                bound = ShaperBound.Combine(child.mode, bound, ce.bound, width, strength);
                box = combined;
                localBox = combinedLocal;
                // BD-2.4: the bag inherits the WORST under-report factor of any member. A max is conservative
                // for every mode: Add's union needs the worst of the two, and both Subtract's and Intersect's
                // results are SUBSETS of an operand, so a box bounded for the union bounds them too.
                if (ce.Spread > spread) spread = ce.Spread;
            }

            return new Emitted
            {
                bound = bound,
                box = box,
                sweepAxis = (anyAxis && axisAgrees) ? axis : ShaperSweepAxis.Radial,
                localBox = localBox,
                spreadRaw = spread,
            };
        }

        static Emitted EmitSweep(ShaperNode node, in ShaperMatrix forward, in ShaperMatrix inverse,
                                 float sigmaMin, Emitted child, State st)
        {
            var sweep = node.sweep;
            if (sweep == null || !sweep.enabled) return child;

            var op = new ShaperOp
            {
                kind = ShaperOpKind.Sweep,
                sweepAxis = child.sweepAxis,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                bound = ShaperBound.Sweep(child.bound),
                boxCx = child.box.valid ? child.box.CentreX : 0f,
                boxCy = child.box.valid ? child.box.CentreY : 0f,
                boxHalfW = child.box.valid ? child.box.HalfW : 0f,
                boxHalfH = child.box.valid ? child.box.HalfH : 0f,
            };

            if (child.sweepAxis == ShaperSweepAxis.Radial)
            {
                float extent = sweep.extentDegrees;
                if (extent >= 360f)
                {
                    op.p5 = 1f;   // identity — the evaluator returns the child's float untouched
                }
                else
                {
                    float s = sweep.startDegrees * Mathf.Deg2Rad;
                    float e = Mathf.Max(0f, extent) * Mathf.Deg2Rad;
                    float end = s + e;
                    // Half-plane on the counter-clockwise side of the start ray, negative inside:
                    //   −cross(d_s, p) = sin(s)·x − cos(s)·y
                    op.p0 = Mathf.Sin(s); op.p1 = -Mathf.Cos(s);
                    // ...and on the clockwise side of the end ray: cross(d_e, p) = cos(e)·y − sin(e)·x
                    op.p2 = -Mathf.Sin(end); op.p3 = Mathf.Cos(end);
                    op.p4 = e > Mathf.PI ? 1f : 0f;   // past a half-turn the wedge is a union, not an intersection
                }
            }
            else
            {
                // The child's extent along its own local X, taken conservatively by carrying the canvas
                // support box back down through this node's inverse.
                float lo = 0f, hi = 0f;
                if (child.box.valid)
                {
                    float cx = inverse.m00 * child.box.CentreX + inverse.m01 * child.box.CentreY + inverse.m02;
                    float hw = Mathf.Abs(inverse.m00) * child.box.HalfW + Mathf.Abs(inverse.m01) * child.box.HalfH;
                    lo = cx - hw; hi = cx + hw;
                }
                float start = Mathf.Clamp01(sweep.startFraction);
                float extent = Mathf.Clamp01(sweep.extentFraction);
                if (start <= 0f && start + extent >= 1f)
                {
                    op.p5 = 1f;   // identity
                }
                else
                {
                    float span = hi - lo;
                    op.p0 = lo + span * start;
                    op.p1 = lo + span * Mathf.Min(1f, start + extent);
                }
            }

            st.ops.Add(op);
            // A sweep carves the child away; it never enlarges either box, canvas or local.
            return new Emitted { bound = op.bound, box = child.box, sweepAxis = child.sweepAxis,
                                 localBox = child.localBox,
                                 // A sweep is a max against a half-plane in the SAME rescaled units, so it
                                 // neither improves nor worsens the under-report factor: carried through.
                                 spreadRaw = child.spreadRaw, sigmaMinRaw = child.sigmaMinRaw };
        }

        static Emitted EmitShell(ShaperNode node, float sigmaMin, Emitted child, State st)
        {
            var shell = node.shell;
            if (shell == null || !shell.enabled) return child;   // identity by construction: nothing is emitted

            float thickness = Mathf.Max(0f, shell.thickness);
            Box box = child.box;
            Box localBox = child.localBox;
            if (shell.alignment != ShaperShellAlignment.Inward)
            {
                // BD-2.4's argument, applied to the operator the border stage reuses: the canvas box must grow
                // by the thickness measured in CANVAS units, and the field's units are canvas units only when
                // the map is isotropic. Growing by `thickness` alone under-bounded an Outward shell on a member
                // scaled (2.0, 0.5) by 23.5 px over 808 samples — measured, T-0107 verification.
                box = box.Grow(thickness * child.Spread);
                // Thickness is authored in CANVAS pixels, and the local box is in local units, so the growth
                // must be divided by the accumulated σ_min that maps local distance to canvas
                // (ShaperProgram.distanceScale). Guarded rather than clamped: a singular node never reaches
                // here — EmitNode publishes the empty field for it and flags — but a near-singular σ_min would
                // otherwise blow the box up, and an over-large ANCHOR box only under-uses the ramp, whereas a
                // divide by zero would NaN every sample.
                localBox = localBox.Grow(sigmaMin > 1e-9f ? thickness / sigmaMin : 0f);
            }

            st.ops.Add(new ShaperOp
            {
                kind = ShaperOpKind.Shell,
                shellAlignment = shell.alignment,
                p0 = thickness,
                // NO second early-out here. `thickness <= 0 → return the child untouched` used to sit in this
                // slot, and it was wrong twice over: spec §7 declares exactly ONE identity, `enabled == false`,
                // and the value it produced was not merely undeclared but backwards. A wall thinned to nothing
                // is an EMPTY interior — |d| − 0 = |d| ≥ 0 for Centred, and max(d, −d − 0) = |d| for both
                // Inward and Outward — so dragging thickness to zero must make the wall vanish. What the
                // early-out did instead was make the whole solid REAPPEAR, bit-identical to no shell at all.
                distanceScale = 1f,
                bound = ShaperBound.Shell(child.bound),
                boxCx = box.valid ? box.CentreX : 0f,
                boxCy = box.valid ? box.CentreY : 0f,
                boxHalfW = box.valid ? box.HalfW : 0f,
                boxHalfH = box.valid ? box.HalfH : 0f,
            });

            return new Emitted { bound = ShaperBound.Shell(child.bound), box = box,
                                 sweepAxis = child.sweepAxis, localBox = localBox,
                                 spreadRaw = child.spreadRaw, sigmaMinRaw = child.sigmaMinRaw };
        }

        /// <summary>
        /// BORDER-CONTRACT BD-2.2 and BD-2.4 — a joining border's DILATION of the node's published field.
        ///
        /// <b>Why this lives in the compiler and not in the border stage.</b> "Joined" means the rest of the tree
        /// SEES the outline: a bag containing the node fuses with it, a mask made from the layer includes it. The
        /// only place a node's field is visible to its parent is the fold, so the dilation has to be an
        /// instruction in the node's own subtree, emitted at the point its content is finished — after
        /// <see cref="EmitSweep"/> and <see cref="EmitShell"/>, before it folds into the bag. Emitted here it
        /// composes automatically, and the node's OWN standalone program (the one
        /// <see cref="ShaperFillResolver"/> compiles per owner) and every ancestor's program publish the same
        /// number with no second rule about which is authoritative.
        ///
        /// <b>Three identities, all structural rather than arithmetic</b> — nothing is emitted at all when:
        /// the border is null, disabled or zero-width (BD-1.5, decided by
        /// <see cref="ShaperBorder.Resolve"/>); the author opted out of joining (BD-2.3, "drawn, not counted");
        /// or the reach is zero, which is every Inward border. BD-1.5 requires the zero-width case to be BITWISE
        /// identical to no border at all, and an emitted <c>d − 0</c> would be identical only by the arithmetic's
        /// good behaviour, which is exactly what that clause declines to rely on.
        ///
        /// <b>BD-3.7's refusal is applied here too.</b> A Subtract MEMBER may not own a border, so it may not
        /// dilate either — a subtractor that quietly grew by its own outline width would carve a bigger hole than
        /// the author sees anywhere on screen, and the refusal diagnostic
        /// (<see cref="ShaperFillDocument.hasSubtractBorder"/>) would name a border that nonetheless changed the
        /// silhouette. The resolver refuses the OWNER for the same node on the same test, so the two halves
        /// cannot disagree.
        ///
        /// <b>The box growth is the whole of BD-2.4, and it touches ONE of the two boxes.</b> <c>box</c> — the
        /// conservative CULLING box — grows by the reach on all four sides, because a bound that excludes real
        /// samples is a correctness failure. <c>localBox</c> — the node-local ANCHOR box a fill normalises
        /// against (FC-1.5) — is returned untouched, because growing it would rescale every gradient on the node
        /// the moment an outline appeared and rescale it again on every frame the width animated. Enabling an
        /// outline must never repaint the thing it outlines. Contrast <see cref="EmitShell"/> directly above,
        /// which grows BOTH — correctly, because a shell REPLACES the node's field and its band genuinely is the
        /// node's new extent, whereas a border keeps the node and adds a strip beside it.
        /// </summary>
        static Emitted EmitBorderJoin(ShaperNode node, bool isRoot, Emitted child, State st)
        {
            if (node.border == null) return child;
            if (!isRoot && node.mode == ShaperCombineMode.Subtract) return child;   // BD-3.7

            ShaperResolvedBorder border = ShaperBorder.Resolve(node.border, st.phase01, st.seed);
            if (!border.Joins) return child;

            // BD-2.4 — the CULLING box grows by the reach measured in CANVAS units, which is the reach times
            // this subtree's under-report factor (Emitted.Spread). Growing by the bare reach is what a reader
            // of BD-2.4 writes first and it is an UNDER-bound: on a member scaled (2.0, 0.5), reach 8, it left
            // 808 samples of the dilated silhouette outside the declared box, overshooting by 23.5 px. On an
            // isotropic node Spread is exactly 1 and this is the bare reach, bit for bit.
            Box box = child.box.Grow(border.reach * child.Spread);

            st.ops.Add(ShaperBorder.JoinOp(border.reach, child.bound,
                                           box.valid ? box.CentreX : 0f,
                                           box.valid ? box.CentreY : 0f,
                                           box.valid ? box.HalfW : 0f,
                                           box.valid ? box.HalfH : 0f));

            // bound unchanged: adding a constant does not change a gradient.
            // localBox unchanged: BD-2.4, and it is the half of this method most easily got wrong.
            return new Emitted { bound = child.bound, box = box,
                                 sweepAxis = child.sweepAxis, localBox = child.localBox,
                                 spreadRaw = child.spreadRaw, sigmaMinRaw = child.sigmaMinRaw };
        }
    }
}
