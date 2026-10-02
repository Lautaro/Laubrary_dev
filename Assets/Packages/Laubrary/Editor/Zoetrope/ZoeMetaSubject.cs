using System;
using Laubrary.MetaMapper;
using Laubrary.MetaMapper.Editor;
using Laubrary.PreviewKit;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// ZOETROPE → METAMAPPER: how a Zoe becomes the SUBJECT of MetaMapper, so an author can draw a foot circle
    /// or a hurt box over the character's own picture. The twin of Cartographer's ClumpMetaSubject; MetaMapper
    /// knows nothing about Zoes, so this registers under <see cref="MetaSubjectKind.Other"/> by type.
    ///
    /// THE COORDINATE CONTRACT. A Zoe's metadata is in <see cref="MapSpace.LocalUnits"/>: one world unit of the
    /// character's own local frame, origin at its ROOT. The picture handed over is the view's first preview
    /// frame, which a sprite view draws on the root itself with the sprite's pivot at the root — so
    /// <c>originPx</c> is the pivot (inside the cropped picture) and <c>pixelsPerUnit</c> is the sprite's own.
    /// A mark the author drops on the pivot reads back as (0,0), exactly where the game's transform puts the
    /// character. A view that draws its art somewhere other than the root would need its own offset here.
    /// </summary>
    [InitializeOnLoad]
    public static class ZoeMetaSubject
    {
        static ZoeMetaSubject() => MetaSubjectProviders.RegisterOther(typeof(Zoe), ResolveVisual, ResolveSession);

        /// <summary>The serializable name MetaMapper re-opens a Zoe's metadata by (survives a domain reload).</summary>
        public static MetaSubjectRef RefFor(Zoe zoe)
            => new MetaSubjectRef { kind = MetaSubjectKind.Other, subject = zoe, key = "" };

        /// <summary>Open MetaMapper on this Zoe's embedded metadata.</summary>
        public static MetaMapperWindow Open(Zoe zoe, Action onChanged = null)
            => zoe == null ? null : MetaMapperWindow.OpenSubject(RefFor(zoe), onChanged);

        /// <summary>The data is the Zoe's own field and the undo target is the Zoe asset itself.</summary>
        static MetaEditSession ResolveSession(MetaSubjectRef r)
        {
            var zoe = r?.subject as Zoe;
            if (zoe == null) return null;
            // The space follows the SUBJECT KIND and is never hand-edited. Restating it is bookkeeping, not
            // authoring, so it is dirtied but not recorded for undo — and only while nothing is authored, because
            // changing the space under existing layers would reinterpret every number in them.
            if (zoe.meta == null)
            {
                zoe.meta = new MetaMapData { space = MapSpace.LocalUnits };
                EditorUtility.SetDirty(zoe);
            }
            else if (zoe.meta.space != MapSpace.LocalUnits && zoe.meta.LayerCount == 0)
            {
                zoe.meta.space = MapSpace.LocalUnits;
                EditorUtility.SetDirty(zoe);
            }
            return new MetaEditSession { data = zoe.meta, undoTarget = zoe };
        }

        static MetaSubjectVisual ResolveVisual(MetaSubjectRef r)
        {
            var zoe = r?.subject as Zoe;
            if (zoe == null) return null;
            string label = string.IsNullOrEmpty(zoe.displayName) ? zoe.name : zoe.displayName;

            var frames = zoe.view is IPreviewableView pv ? pv.PreviewFrames() : null;
            var sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            var tex = sprite != null ? PreviewTex.CropSprite(sprite) : null;
            if (tex == null)
            {
                // No art (a fresh Zoe, a view that cannot preview itself): a small blank canvas with the root
                // centred near its bottom, so a foot circle can still be authored where the feet will be.
                return MetaSubjectVisual.Blank(MapSpace.LocalUnits, new Vector2Int(2, 2), new Vector2(-1f, -0.5f),
                    label);
            }
            tex.hideFlags = HideFlags.HideAndDontSave;

            float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 16f;
            // The pivot is counted from the sprite RECT's corner; the picture is the TEXTURE rect, which an
            // atlas may have trimmed — the offset between the two keeps the root on the right pixel.
            Vector2 origin = sprite.pivot - (sprite.packed ? sprite.textureRectOffset : Vector2.zero);
            return new MetaSubjectVisual
            {
                frames = new[] { tex },
                ownsFrames = true,
                fps = 0f,
                originPx = origin,
                pixelsPerUnit = ppu,
                space = MapSpace.LocalUnits,
                refSize = new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(tex.width / ppu)),
                                         Mathf.Max(1, Mathf.CeilToInt(tex.height / ppu))),
                label = label,
            };
        }
    }
}
