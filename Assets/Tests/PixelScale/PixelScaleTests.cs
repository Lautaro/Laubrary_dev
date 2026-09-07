using Laubrary.PixelScale;
using NUnit.Framework;
using UnityEngine;

namespace Laubrary.PixelScale.Tests
{
    public class PixelScaleTests
    {
        PixelScaleProjectSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<PixelScaleProjectSettings>();
            _settings.pixelsPerUnit = 16;
            _settings.targetResolution = new Vector2Int(320, 200);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        [Test]
        public void DefaultSettings_DeriveExpectedCameraSizeAndUpscales()
        {
            Assert.That(PixelScale.OrthographicCameraSize(_settings), Is.EqualTo(6.25f));
            Assert.That(PixelScale.IntegerUpscale(800, _settings), Is.EqualTo(4));
            Assert.That(PixelScale.IntegerUpscale(1080, _settings), Is.EqualTo(5));
        }
    }
}
