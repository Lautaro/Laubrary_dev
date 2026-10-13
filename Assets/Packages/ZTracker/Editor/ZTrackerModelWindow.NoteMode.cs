using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // How notes follow one another on a synth (T-0627, T-0628): None / Glide / Legato / Both, the glide time, and, for
    // Legato, what the held note does with a new note's velocity.
    public sealed partial class ZTrackerModelWindow
    {
        void BuildNoteMode(VisualElement root, InstrumentParameters q)
        {
            bool glide = q.glideEnabled, legato = q.legatoEnabled;
            int mode = (glide ? 1 : 0) | (legato ? 2 : 0);
            var notes = InstrumentChoice("Notes", mode, new[] { "None", "Glide", "Legato", "Both" },
                "Glide slides each new note's pitch from the previous note. Legato lets an overlapping note take over the held one: its envelope keeps running and its pitch slides to the new note. Both does both; None starts every note on its own pitch.",
                v => { q.glideEnabled = (v & 1) != 0; q.legatoEnabled = (v & 2) != 0; q.glideLegato = false; if (q.legatoEnabled) q.legatoSlides = true; }, "synth-note-mode");
            // A Legato saved before legato slid still jumps; its slide time reads 0 until it is edited.
            bool jumps = legato && !glide && !q.legatoSlides;
            string glideTip = glide && legato ? "Time a slide takes to reach the new note, both for a new note after the previous one and for an overlapping (legato) note. The pitch moves evenly and arrives exactly at this time."
                : glide ? "Time each new note's pitch takes to slide from the previous note to its own. The pitch moves evenly and arrives exactly at this time."
                : "Time an overlapping note's pitch takes to slide from the held note to the new one; 0 jumps straight there. The pitch moves evenly and arrives exactly at this time.";
            var glideTime = Named(Z.MicroSlider("Glide ms", (jumps ? 0 : q.glideSeconds) * 1000, 0, 2000, glideTip + " Double-click resets to " + (new InstrumentParameters().glideSeconds * 1000).ToString("0") + " ms.",
                v => InstrumentEdit("Glide ms", () => { q.glideSeconds = v / 1000; if (q.legatoEnabled) q.legatoSlides = true; }), 140, defaultValue: new InstrumentParameters().glideSeconds * 1000, decimals: 0), "synth-glideSeconds");
            glideTime.style.display = mode == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            root.Add(Flow(notes, glideTime));
            if (!legato) return;
            var defaults = new InstrumentParameters();
            var velocity = InstrumentChoice("Velocity", (int)q.legatoVelocity, new[] { "Follow", "Ignore", "Slide" },
                "What a legato note does with the new note's velocity (its volume column or key velocity): Follow jumps to it, Ignore keeps the held note's level, Slide moves there over Slide ms.",
                v => q.legatoVelocity = (LegatoVelocity)v, "synth-legato-velocity");
            var slide = Named(Z.MicroSlider("Slide ms", q.legatoVelocitySeconds * 1000, 0, 2000, "Time the held note's level takes to move to the new note's velocity. Double-click resets to " + (defaults.legatoVelocitySeconds * 1000).ToString("0") + " ms.",
                v => InstrumentEdit("Legato slide ms", () => q.legatoVelocitySeconds = v / 1000), 130, defaultValue: defaults.legatoVelocitySeconds * 1000, decimals: 0), "synth-legato-velocity-slide");
            slide.style.display = q.legatoVelocity == LegatoVelocity.Slide ? DisplayStyle.Flex : DisplayStyle.None;
            var scale = Named(Z.MicroSlider("Scale %", q.legatoVelocityScale * 100, 0, 100, "How much of the velocity change applies: 100% reaches the new note's velocity, 50% goes half way, 0% keeps the held level. Double-click resets to 100%.",
                v => InstrumentEdit("Legato velocity scale", () => q.legatoVelocityScale = v / 100), 120, defaultValue: defaults.legatoVelocityScale * 100, decimals: 0), "synth-legato-velocity-scale");
            scale.style.display = q.legatoVelocity == LegatoVelocity.Ignore ? DisplayStyle.None : DisplayStyle.Flex;
            var scope = InstrumentChoice("Legato", q.legatoPoly ? 1 : 0, new[] { "Mono", "Poly" },
                "Mono: an overlapping note in the same column takes over the held note. Poly: also, while any note of this instrument is held on the track (a chord across columns, overlapping lines), a new note does not restart the envelopes, modulation or vibrato; it joins them where they are and differs only in pitch. With no note held, a note starts normally.",
                v => q.legatoPoly = v == 1, "synth-legato-scope");
            root.Add(Flow(scope, velocity, slide, scale));
        }
    }
}
