using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.AssetKit.Editor;
using Object = UnityEngine.Object;

namespace Laubrary.Chunks.Editor
{
    /// The first dedicated authoring window for Chunks — until now ChunkSpec only had a plain CustomEditor
    /// (ChunkSpecEditor, a bare-bones inspector-augmentation). Same AssetKit base every other Laubrary tool
    /// uses (browse/create/duplicate/rename/delete for free), fields grouped to mirror ChunkSpec's own
    /// [Header] sections. No live burst preview yet (Pyre's own Play/Scrub transport is a bigger, separate
    /// piece of work) — this is the field editor, a real first step, not the final shape.
    public class ChunkWindow : LaubraryAssetWindow<ChunkSpec>
    {
        [MenuItem("Laubrary/Chunks")]
        public static void Open() => GetWindow<ChunkWindow>("Chunks");

        /// Same entry-point shape as PyreWindow.OpenFor/MirageWindow.OpenFor — lets a LauAssetField's Edit
        /// button jump straight into this ChunkSpec's own editor.
        public static void OpenFor(ChunkSpec spec)
        {
            var w = GetWindow<ChunkWindow>("Chunks");
            if (spec != null) w.SetAsset(spec);
        }

        protected override string TypeLabel => "Chunk";
        protected override string NewAssetName => "Chunks";
        protected override string DefaultFolder => "Assets/Chunks";

        readonly Dictionary<Object, Texture2D> _visualThumbs = new Dictionary<Object, Texture2D>();

        protected override void DrawAsset(ChunkSpec c)
        {
            Undo.RecordObject(c, "Edit Chunk Spec");

            ZUI.Label("Emission", ZUI.ZTextStyle.Header);
            var emitForm = ZUI.Form();
            var countRow = ZUI.Row("Count Min / Max");
            countRow.Add(60f, ZUI.IntField(() => c.countMin, v => c.countMin = Mathf.Max(0, v)));
            countRow.Add(60f, ZUI.IntField(() => c.countMax, v => c.countMax = Mathf.Max(c.countMin, v)));
            emitForm.Add(countRow);
            var speedRow = ZUI.Row("Speed Min / Max");
            speedRow.Add(60f, ZUI.FloatField(() => c.speedMin, v => c.speedMin = Mathf.Max(0f, v)));
            speedRow.Add(60f, ZUI.FloatField(() => c.speedMax, v => c.speedMax = Mathf.Max(c.speedMin, v)));
            emitForm.Add(speedRow);
            emitForm.Add("Direction °", 60f, ZUI.FloatField(() => c.directionDeg, v => c.directionDeg = v));
            emitForm.Add("Spread °", 60f, ZUI.FloatField(() => c.spreadDeg, v => c.spreadDeg = Mathf.Clamp(v, 0f, 180f)));
            emitForm.Add("Upward Bias", 60f, ZUI.FloatField(() => c.upwardBias, v => c.upwardBias = v));
            emitForm.Draw();

            ZUI.VerticalSpace();
            ZUI.Label("Physics", ZUI.ZTextStyle.Header);
            var physForm = ZUI.Form();
            physForm.Add("Gravity", 60f, ZUI.FloatField(() => c.gravity, v => c.gravity = Mathf.Max(0f, v)));
            physForm.Add("Drag", 60f, ZUI.FloatField(() => c.drag, v => c.drag = Mathf.Clamp(v, 0f, 20f)));
            var angRow = ZUI.Row("Angular Speed Min / Max");
            angRow.Add(60f, ZUI.FloatField(() => c.angularSpeedMin, v => c.angularSpeedMin = Mathf.Max(0f, v)));
            angRow.Add(60f, ZUI.FloatField(() => c.angularSpeedMax, v => c.angularSpeedMax = Mathf.Max(c.angularSpeedMin, v)));
            physForm.Add(angRow);
            physForm.Add("Face Velocity", 60f, ZUI.Toggle(() => c.faceVelocity, v => c.faceVelocity = v));
            physForm.Draw();

            ZUI.VerticalSpace();
            ZUI.Label("Life / Look", ZUI.ZTextStyle.Header);
            var lifeForm = ZUI.Form();
            var lifeRow = ZUI.Row("Life Min / Max (s)");
            lifeRow.Add(60f, ZUI.FloatField(() => c.lifeMin, v => c.lifeMin = Mathf.Max(0.01f, v)));
            lifeRow.Add(60f, ZUI.FloatField(() => c.lifeMax, v => c.lifeMax = Mathf.Max(c.lifeMin, v)));
            lifeForm.Add(lifeRow);
            var sizeRow = ZUI.Row("Size Min / Max");
            sizeRow.Add(60f, ZUI.FloatField(() => c.sizeMin, v => c.sizeMin = Mathf.Max(0.001f, v)));
            sizeRow.Add(60f, ZUI.FloatField(() => c.sizeMax, v => c.sizeMax = Mathf.Max(c.sizeMin, v)));
            lifeForm.Add(sizeRow);
            lifeForm.Draw();

            using (ZUI.HRow())
            {
                GUILayout.Label("Size over life", GUILayout.Width(90));
                c.sizeOverLife = EditorGUILayout.CurveField(c.sizeOverLife, GUILayout.Height(18));
            }
            using (ZUI.HRow())
            {
                GUILayout.Label("Alpha over life", GUILayout.Width(90));
                c.alphaOverLife = EditorGUILayout.CurveField(c.alphaOverLife, GUILayout.Height(18));
            }
            using (ZUI.HRow())
            {
                GUILayout.Label("Colour over life", GUILayout.Width(90));
                c.colorOverLife = EditorGUILayout.GradientField(c.colorOverLife);
            }

            ZUI.VerticalSpace();
            using (ZUI.HRow())
            {
                ZUI.Label("Sprites", ZUI.ZTextStyle.Header);
                ZUI.HelpIcon("Leave empty to use a procedural tinted pixel-square instead.");
            }
            SerializedObject so = new SerializedObject(c);
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("sprites"), true);
            so.ApplyModifiedProperties();
            var ppuForm = ZUI.Form();
            ppuForm.Add("Pixels/Unit", 60f, ZUI.FloatField(() => c.pixelsPerUnit, v => c.pixelsPerUnit = Mathf.Max(1f, v)));
            ppuForm.Draw();

            ZUI.VerticalSpace();
            ZUI.Label("Floor / Collision", ZUI.ZTextStyle.Header);
            var floorForm = ZUI.Form();
            floorForm.Add("Use Floor", 60f, ZUI.Toggle(() => c.useFloor, v => c.useFloor = v));
            if (c.useFloor)
            {
                floorForm.Add("Floor Y", 60f, ZUI.FloatField(() => c.floorY, v => c.floorY = v));
                floorForm.Add("Bounciness", 60f, ZUI.FloatField(() => c.bounciness, v => c.bounciness = Mathf.Clamp01(v)));
                floorForm.Add("Friction", 60f, ZUI.FloatField(() => c.floorFriction, v => c.floorFriction = Mathf.Clamp01(v)));
                floorForm.Add("Rest On Floor", 60f, ZUI.Toggle(() => c.restOnFloor, v => c.restOnFloor = v));
            }
            floorForm.Draw();

            ZUI.VerticalSpace();
            using (ZUI.HRow())
            {
                ZUI.Label("Sampled Pseudo-3D Debris", ZUI.ZTextStyle.Header);
                ZUI.HelpIcon("Cuts small chunks directly out of the exploding object's own sprite and tumbles them " +
                              "(squash + shade) instead of a flat/authored shape. Doesn't modify the source sprite " +
                              "itself — only reads pixels from it. Its texture needs Read/Write Enabled.");
            }
            using (ZUI.HRow())
            {
                GUILayout.Label("Sample Source", GUILayout.Width(90));
                c.sampleSource = (Sprite)EditorGUILayout.ObjectField(c.sampleSource, typeof(Sprite), false);
            }
            if (c.sampleSource != null)
            {
                var sampleForm = ZUI.Form();
                var pxRow = ZUI.Row("Sample Px Min / Max");
                pxRow.Add(60f, ZUI.IntField(() => c.samplePxMin, v => c.samplePxMin = Mathf.Max(1, v)));
                pxRow.Add(60f, ZUI.IntField(() => c.samplePxMax, v => c.samplePxMax = Mathf.Max(c.samplePxMin, v)));
                sampleForm.Add(pxRow);
                sampleForm.Add("Tumble", 60f, ZUI.Toggle(() => c.tumble, v => c.tumble = v));
                if (c.tumble)
                {
                    var tumbleRow = ZUI.Row("Tumble Speed Min / Max");
                    tumbleRow.Add(60f, ZUI.FloatField(() => c.tumbleSpeedMin, v => c.tumbleSpeedMin = Mathf.Max(0f, v)));
                    tumbleRow.Add(60f, ZUI.FloatField(() => c.tumbleSpeedMax, v => c.tumbleSpeedMax = Mathf.Max(c.tumbleSpeedMin, v)));
                    sampleForm.Add(tumbleRow);
                    sampleForm.Add("Shade Strength", 60f, ZUI.FloatField(() => c.tumbleShadeStrength, v => c.tumbleShadeStrength = Mathf.Clamp01(v)));
                }
                sampleForm.Draw();

                ZUI.VerticalSpace();
                ZUI.Label("Tint", ZUI.ZTextStyle.Subheader);
                using (ZUI.HRow())
                {
                    GUILayout.Label("Mode", GUILayout.Width(90));
                    c.tintMode = (ChunkTintMode)EditorGUILayout.EnumPopup(c.tintMode, GUILayout.Width(120));
                }
                if (c.tintMode != ChunkTintMode.None)
                {
                    using (ZUI.HRow())
                    {
                        GUILayout.Label("Colour", GUILayout.Width(90));
                        c.tintColor = EditorGUILayout.ColorField(c.tintColor, GUILayout.Width(120));
                    }
                    var tintValsForm = ZUI.Form();
                    tintValsForm.Add("Strength", 60f, ZUI.FloatField(() => c.tintStrength, v => c.tintStrength = Mathf.Clamp01(v)));
                    if (c.tintMode != ChunkTintMode.Whole)
                        tintValsForm.Add("Edge px", 60f, ZUI.IntField(() => c.edgeThicknessPx, v => c.edgeThicknessPx = Mathf.Max(1, v)));
                    tintValsForm.Draw();
                }
            }

            ZUI.VerticalSpace();
            ZUI.Label("Animated Content", ZUI.ZTextStyle.Header);
            ZUI.Label("Every chunk plays this instead of a static/procedural sprite. Loses to Sample source if that's also set.", ZUI.ZTextStyle.Subtle);
            LauAssetField.Draw(c.animationSource, picked => c.animationSource = picked, typeof(IChunkAnimation),
                _visualThumbs, c.name, "Assets/Chunks/AnimationSources");

            ZUI.VerticalSpace();
            using (ZUI.HRow())
            {
                ZUI.Label("Hit Detection", ZUI.ZTextStyle.Header);
                ZUI.HelpIcon("Cheap circle-approximation only, not pixel-perfect — gives each chunk a trigger " +
                              "CircleCollider2D + a Combat2D Hitbox while it's alive.");
            }
            var hitForm = ZUI.Form();
            hitForm.Add("Enabled", 60f, ZUI.Toggle(() => c.useHitDetection, v => c.useHitDetection = v));
            if (c.useHitDetection)
            {
                hitForm.Add("Damage", 60f, ZUI.FloatField(() => c.hitDamage, v => c.hitDamage = Mathf.Max(0f, v)));
                hitForm.Add("Radius Scale", 60f, ZUI.FloatField(() => c.hitRadiusScale, v => c.hitRadiusScale = Mathf.Clamp(v, 0.1f, 3f)));
            }
            hitForm.Draw();

            ZUI.VerticalSpace();
            ZUI.Label("Trail", ZUI.ZTextStyle.Header);
            ZUI.Label("A puff (e.g. a Pyre fire→smoke blast) spawned at each chunk's own position on a timer while it flies.", ZUI.ZTextStyle.Subtle);
            LauAssetField.Draw(c.trailSource, picked => c.trailSource = picked, typeof(IChunkTrailSource),
                _visualThumbs, c.name, "Assets/Chunks/TrailSources");
            if (c.trailSource != null)
            {
                var trailForm = ZUI.Form();
                trailForm.Add("Interval (s)", 60f, ZUI.FloatField(() => c.trailInterval, v => c.trailInterval = Mathf.Max(0.01f, v)));
                trailForm.Draw();
            }

            if (GUI.changed) EditorUtility.SetDirty(c);
        }
    }
}
