using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    // Direct-callable regression checks; no Test Runner or menu surface is needed.
    internal static class LauminationBuilderPreviewChecks
    {
        public static string Run()
        {
            int checks = 0;
            var box = new Rect(20, 30, 320, 220);
            var anchor = new Vector2(box.x + box.width * .5f, box.y + box.height * .78f);
            foreach (var bounds in new[]
            {
                new Rect(-20, -180, 40, 200), // tall, bottom-biased pivot
                new Rect(-400, -10, 600, 70), // rotated/offset content
                new Rect(-10, -10, 1000, 1000), // fit requires zoom below 1
                new Rect(-800, -1200, 850, 1400) // extreme pivot + large lead-in
            })
            {
                float scale = FramePreview.FitScale(box, bounds, anchor);
                var fitted = Rect.MinMaxRect(anchor.x + bounds.xMin * scale, anchor.y + bounds.yMin * scale,
                    anchor.x + bounds.xMax * scale, anchor.y + bounds.yMax * scale);
                Require(fitted.xMin >= box.xMin + 7.99f && fitted.xMax <= box.xMax - 7.99f &&
                    fitted.yMin >= box.yMin + 7.99f && fitted.yMax <= box.yMax - 7.99f, "Fit cropped content");
                checks++;
            }

            var texture = new Texture2D(16, 16);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, 0), 16);
            var version = ScriptableObject.CreateInstance<LauminaryVersion>();
            version.hideFlags = HideFlags.HideAndDontSave;
            var host = new GameObject("Builder preview regression") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            try
            {
                version.animations.Add(new Laumination
                {
                    name = "test", fps = 10,
                    frames = new List<Sprite> { sprite, sprite, sprite, sprite, sprite, sprite },
                    zonesEnabled = true,
                    zones = new List<AnimZone>
                    {
                        new AnimZone { name = "Start", startFrame = 0, endFrame = 1, behavior = ZoneBehavior.PlayThrough },
                        new AnimZone { name = "Hold", startFrame = 2, endFrame = 3, behavior = ZoneBehavior.Loop },
                        new AnimZone { name = "End", startFrame = 4, endFrame = 5, behavior = ZoneBehavior.PlayThrough }
                    }
                });
                var player = host.AddComponent<ZonedAnimationPlayer>();
                player.SetVersion(version);
                Require(player.Play("test"), "Phase clip did not start");
                player.Tick(.21f);
                Require(player.CurrentZoneName == "Hold", "Play-through did not enter the next phase"); checks++;
                player.Tick(1f);
                Require(player.CurrentZoneName == "Hold" && player.IsPlaying, "Loop phase did not hold"); checks++;
                player.Advance();
                Require(player.CurrentZoneName == "End" && player.CurrentFrame == 4, "Advance did not enter the next phase"); checks++;
                player.Tick(.21f);
                Require(!player.IsPlaying && player.CurrentFrame == 5, "Last phase did not finish on its last frame"); checks++;
                player.Play("test"); player.EnterAt("Hold");
                Require(player.CurrentZoneName == "Hold" && player.CurrentFrame == 2 && player.IsPlaying,
                    "Selected phase audition did not start at its first frame"); checks++;
                int entered = 0;
                player.OnFrameEntered += _ => entered++;
                Require(player.SeekFrame(3) && player.CurrentZoneName == "Hold" && player.CurrentFrame == 3 && entered == 0,
                    "Phase seek did not select its frame without callbacks"); checks++;
                player.Tick(.101f);
                Require(player.CurrentFrame == 2 && player.CurrentZoneName == "Hold", "Phase seek resumed from the old frame"); checks++;
                player.SeekFrame(4);
                Require(player.CurrentZoneName == "End", "Seek did not change the owning phase"); checks++;
                player.Stop(); player.SeekFrame(2);
                Require(!player.IsPlaying && player.CurrentFrame == 2, "Seek changed the stopped state"); checks++;
                var plain = new AnimationPlayback();
                plain.Play(version.animations[0]);
                int events = 0; plain.OnFrameEvent += (_, __) => events++;
                Require(plain.SeekFrame(4) && plain.Frame == 4 && events == 0, "Sequence seek failed");
                plain.Tick(.101f);
                Require(plain.Frame == 5, "Sequence seek resumed from the old frame"); checks++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(version);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return checks + " preview regression checks passed";
        }

        private static void Require(bool condition, string failure)
        {
            if (!condition) throw new InvalidOperationException(failure);
        }
    }
}
