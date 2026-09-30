// Stepped driver for the phase 3 capture harness (AHQ T-0551).
//
// Why a queue and not a loop: opening, closing and resizing an editor window are all requests that Unity only
// completes when it next pumps its own loop. A single scripted call that opens a window and immediately
// photographs it therefore photographs the PREVIOUS window at the PREVIOUS size - which showed up as every
// capture being one step stale. Sleeping does not help, because the sleep blocks the very loop that would
// apply the resize.
//
// So each external call does exactly one step: finish the window opened last time, then open the next one.
// Control returns to Unity in between, which is when the open/close/resize actually happens.

using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace Laubrary.UISeparationPhase3 {

    public static class Phase3Run {

        public sealed class Job {
            public string Type;        // editor window type name, or null for the inspector route
            public string Component;   // component type name when Type is null
            public string Label;
            public float Width, Height;
            public string Asset;
            public string Field;       // optional: an int field to set before capturing (a list selection)
            public int Value;
        }

        private static readonly List<Job> s_jobs = new List<Job>();
        private static int s_next;
        private static EditorWindow s_open;
        private static string s_openLabel;
        private static readonly StringBuilder s_log = new StringBuilder();

        /// <summary>Clears the queue. Call before enqueueing a run.</summary>
        public static string Reset() {
            s_jobs.Clear(); s_next = 0; s_open = null; s_openLabel = null; s_log.Length = 0;
            return "queue cleared";
        }

        public static string Add(string type, string label, float width, float height, string asset) {
            s_jobs.Add(new Job { Type = type, Label = label, Width = width, Height = height, Asset = string.IsNullOrEmpty(asset) ? null : asset });
            return s_jobs.Count + " queued";
        }

        /// <summary>Queues a state that first puts the window's list selection somewhere, so a picked row can be
        /// photographed rather than only the resting list.</summary>
        public static string AddWithField(string type, string label, float width, float height, string asset, string field, int value) {
            s_jobs.Add(new Job { Type = type, Label = label, Width = width, Height = height,
                                 Asset = string.IsNullOrEmpty(asset) ? null : asset, Field = field, Value = value });
            return s_jobs.Count + " queued";
        }

        public static string AddInspector(string componentType, string label, float width, float height) {
            s_jobs.Add(new Job { Component = componentType, Label = label, Width = width, Height = height });
            return s_jobs.Count + " queued";
        }

        /// <summary>
        /// Gives Unity one more pump on the window that is currently open without advancing the queue. Some
        /// windows - the property editor used for the Cabinets inspector - need a second pass before their
        /// contents exist.
        /// </summary>
        public static string Nudge() {
            if (s_open == null) return "nothing open";
            s_open.Focus(); s_open.Repaint();
            return "nudged " + s_openLabel + " (" + s_open.rootVisualElement.childCount + " root children)";
        }

        /// <summary>
        /// One step. Returns "done" once the queue is exhausted and the last window has been captured.
        /// </summary>
        public static string Step() {
            if (s_open != null) {
                try {
                    s_log.Append(Phase3Capture.DumpStyles(s_open, s_openLabel)).Append('\n');
                    s_log.Append(Phase3Capture.Shoot(s_open, s_openLabel)).Append('\n');
                } catch (System.Exception ex) {
                    s_log.Append("FAIL capture ").Append(s_openLabel).Append(": ").Append(ex.Message).Append('\n');
                } finally {
                    s_open.Close(); s_open = null; s_openLabel = null;
                    Phase3Capture.CloseInspectorFixture();
                }
            }

            while (s_next < s_jobs.Count) {
                Job job = s_jobs[s_next++];
                try {
                    s_open = job.Type != null
                        ? Phase3Capture.Open(job.Type, job.Width, job.Height, job.Asset)
                        : Phase3Capture.OpenInspector(job.Component, job.Width, job.Height);
                    s_openLabel = job.Label;
                    if (job.Field != null) Phase3Capture.SetIntField(s_open, job.Field, job.Value);
                    return "opened " + job.Label + " (" + s_next + "/" + s_jobs.Count + ")";
                } catch (System.Exception ex) {
                    s_log.Append("FAIL open ").Append(job.Label).Append(": ").Append(ex.Message).Append('\n');
                }
            }
            return "done\n" + s_log;
        }
    }
}
