using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Laubrary.Pyre.Tests
{
    /// The DATA layer under the ZUI ramp control (T-0064). ZuiRampControl itself is an editor VisualElement and is
    /// exercised by hand; what CAN regress silently is PyreRamp's IZuiRamp implementation — a wrong sort, a shifted
    /// index or a drifted Eval would corrupt authored ramps without a compile error. In particular the last test is
    /// the byte/behaviour-parity guard: IZuiRamp.Eval must be the SAME function the renderer and the Kiln parity
    /// harness call (PyreParityDump samples t = 0, 0.1 … 1.0 — the same eleven points used here).
    public class PyreRampControlTests
    {
        static PyreRamp Ramp3() => new PyreRamp
        {
            space = PyreRampSpace.LinearLight,
            stops =
            {
                new PyreRampStop(0f, new Color(0.10f, 0.02f, 0.01f, 0.20f)),
                new PyreRampStop(0.5f, new Color(0.90f, 0.35f, 0.05f, 0.80f)),
                new PyreRampStop(1f, new Color(1f, 0.99f, 0.93f, 1f)),
            },
        };

        static readonly float[] Sweep = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };

        static int B(float c) => Mathf.RoundToInt(Mathf.Clamp01(c) * 255f);

        [Test]
        public void PyreRamp_SpeaksIZuiRamp_AndEveryMemberRoundTrips()
        {
            var ramp = Ramp3();
            IZuiRamp r = ramp;   // fails to compile if the interface is ever dropped — that IS the assertion

            Assert.That(r.Count, Is.EqualTo(3));

            r.SetPos(1, 0.42f);
            Assert.That(r.GetPos(1), Is.EqualTo(0.42f).Within(1e-6f));
            Assert.That(ramp.stops[1].pos, Is.EqualTo(0.42f).Within(1e-6f), "writes through to the serialized stop");

            var c = new Color(0.2f, 0.4f, 0.6f, 0.33f);
            r.SetColor(1, c);
            Assert.That(r.GetColor(1), Is.EqualTo(c));
            Assert.That(ramp.stops[1].color, Is.EqualTo(c));

            // BlendMode is a view onto `space` and nothing else — no new serialized field.
            Assert.That(r.BlendModeNames, Is.Not.Null);
            Assert.That(r.BlendModeNames.Length, Is.EqualTo(2));
            Assert.That(r.BlendMode, Is.EqualTo((int)PyreRampSpace.LinearLight));
            r.BlendMode = (int)PyreRampSpace.Srgb;
            Assert.That(ramp.space, Is.EqualTo(PyreRampSpace.Srgb));
            Assert.That(r.BlendMode, Is.EqualTo((int)PyreRampSpace.Srgb));
            r.BlendMode = 99;   // clamped, never an undefined enum value on a serialized asset
            Assert.That((int)ramp.space, Is.EqualTo(1));
            r.BlendMode = -5;
            Assert.That((int)ramp.space, Is.EqualTo(0));
        }

        [Test]
        public void Insert_KeepsStopsSorted_AndReturnsTheNewIndex()
        {
            var ramp = Ramp3();
            IZuiRamp r = ramp;

            int mid = r.Insert(0.25f, Color.red);
            Assert.That(mid, Is.EqualTo(1));
            Assert.That(r.Count, Is.EqualTo(4));
            Assert.That(r.GetPos(mid), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(r.GetColor(mid), Is.EqualTo(Color.red));

            // Equal positions keep insertion order: a new stop lands AFTER the twin already sitting there, so
            // inserting at 0 into [0, 0.25, 0.5, 1] returns index 1, not 0.
            int front = r.Insert(0f, Color.green);
            Assert.That(front, Is.EqualTo(1));
            Assert.That(r.GetColor(1), Is.EqualTo(Color.green));
            int back = r.Insert(1f, Color.blue);
            Assert.That(back, Is.EqualTo(r.Count - 1), "a stop at the top end appends");

            for (int i = 1; i < r.Count; i++)
                Assert.That(r.GetPos(i), Is.GreaterThanOrEqualTo(r.GetPos(i - 1)), $"stop {i} is out of order");
        }

        [Test]
        public void Insert_AtTheColourTheRampAlreadyHasThere_LeavesEvalUnchanged()
        {
            // The property that makes double-click-to-insert safe: adding a handle on what was already being drawn
            // must not change the picture. Exact equality is not available — the inserted stop round-trips through
            // linear->sRGB->linear — so this is a tight tolerance, not a bitwise claim.
            foreach (var space in new[] { PyreRampSpace.LinearLight, PyreRampSpace.Srgb })
            {
                var ramp = Ramp3();
                ramp.space = space;
                IZuiRamp r = ramp;

                var before = new List<Color>();
                foreach (var t in Sweep) before.Add(r.Eval(t));

                const float at = 0.37f;
                r.Insert(at, r.Eval(at));

                for (int i = 0; i < Sweep.Length; i++)
                {
                    var got = r.Eval(Sweep[i]);
                    var want = before[i];
                    Assert.That(got.r, Is.EqualTo(want.r).Within(2e-3f), $"{space} r @ {Sweep[i]}");
                    Assert.That(got.g, Is.EqualTo(want.g).Within(2e-3f), $"{space} g @ {Sweep[i]}");
                    Assert.That(got.b, Is.EqualTo(want.b).Within(2e-3f), $"{space} b @ {Sweep[i]}");
                    Assert.That(got.a, Is.EqualTo(want.a).Within(1e-5f), $"{space} a @ {Sweep[i]}");
                }
            }
        }

        [Test]
        public void RemoveAt_RemovesTheRightStop()
        {
            var ramp = Ramp3();
            IZuiRamp r = ramp;
            var keptFirst = r.GetColor(0);
            var keptLast = r.GetColor(2);

            r.RemoveAt(1);

            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r.GetColor(0), Is.EqualTo(keptFirst));
            Assert.That(r.GetColor(1), Is.EqualTo(keptLast));
            Assert.That(r.GetPos(0), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(r.GetPos(1), Is.EqualTo(1f).Within(1e-6f));

            // Out-of-range is a no-op rather than an exception: the control holds indices across rebuilds.
            Assert.DoesNotThrow(() => r.RemoveAt(7));
            Assert.DoesNotThrow(() => r.RemoveAt(-1));
            Assert.That(r.Count, Is.EqualTo(2));
        }

        [Test]
        public void AnEmptyRamp_IsLegal_AndEvalDoesNotThrow()
        {
            // Pyre's soot ramp ships with ZERO stops on purpose ("no soot"). The control must never auto-seed one,
            // so the data layer has to survive being empty.
            var ramp = new PyreRamp();
            IZuiRamp r = ramp;

            Assert.That(r.Count, Is.EqualTo(0));
            Assert.That(ramp.IsEmpty, Is.True);
            foreach (var t in Sweep) Assert.DoesNotThrow(() => r.Eval(t));

            // ...and it must be recoverable from empty through the same insert the "+" button uses.
            int i = r.Insert(0.5f, Color.cyan);
            Assert.That(i, Is.EqualTo(0));
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(r.Eval(0.5f), Is.EqualTo(Color.cyan));

            r.RemoveAt(0);
            Assert.That(r.Count, Is.EqualTo(0), "removing the last stop is allowed — empty is a real state");
        }

        [Test]
        public void IZuiRampEval_IsExactlyEvaluate_ForEveryShippedPreset()
        {
            // Parity guard. The eleven sample points are the ones PyreParityDump uses (Editor/Pyre/Parity/
            // PyreParityDump.cs), so a drift here would be the same drift the Kiln harness reports.
            var presets = new (string name, PyreRamp ramp)[]
            {
                ("Ember", PyreRampPresets.Ember()),
                ("OrbFrost", PyreRampPresets.OrbFrost()),
                ("PlasmaIon", PyreRampPresets.PlasmaIon()),
            };

            foreach (var (name, ramp) in presets)
            {
                IZuiRamp r = ramp;
                Assert.That(r.Count, Is.EqualTo(ramp.stops.Count), $"{name} stop count");
                Assert.That(r.BlendMode, Is.EqualTo((int)ramp.space), $"{name} blend mode");

                foreach (var t in Sweep)
                {
                    var want = ramp.Evaluate(t);
                    var got = r.Eval(t);
                    Assert.That(got.r, Is.EqualTo(want.r), $"{name} r @ {t}");
                    Assert.That(got.g, Is.EqualTo(want.g), $"{name} g @ {t}");
                    Assert.That(got.b, Is.EqualTo(want.b), $"{name} b @ {t}");
                    Assert.That(got.a, Is.EqualTo(want.a), $"{name} a @ {t}");
                }

                // Every stop still evaluates to itself — no blending happens ON a stop. Compared at 8-bit like
                // PlusShadeTests does, since the linear-light path round-trips through pow() and is not bit-exact.
                for (int i = 0; i < r.Count; i++)
                {
                    var c = r.Eval(r.GetPos(i));
                    var s = ramp.stops[i].color;
                    Assert.That((B(c.r), B(c.g), B(c.b)), Is.EqualTo((B(s.r), B(s.g), B(s.b))), $"{name} stop {i}");
                    Assert.That(c.a, Is.EqualTo(s.a).Within(1e-5f), $"{name} stop {i} alpha");
                }
            }
        }
    }
}
