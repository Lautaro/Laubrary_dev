using Unity.Collections;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Mathf = Laubrary.Audio.AudioMath;

namespace Laubrary.Audio {
    public readonly struct EnvPoint {
        public readonly float time, value, exponent;
        /// <summary>A random point's ellipse radii and bias, and the curve's value range its drawn value stays in (T-0483).</summary>
        public readonly float randomX, randomY, randomBias, yMin, yMax;
        public EnvPoint(float time, float value, float exponent) : this(time, value, exponent, 0f, 0f, 0.5f, float.MinValue, float.MaxValue) { }
        public EnvPoint(float time, float value, float exponent, float randomX, float randomY, float randomBias, float yMin, float yMax) {
            this.time = time; this.value = value; this.exponent = exponent;
            this.randomX = randomX; this.randomY = randomY; this.randomBias = randomBias; this.yMin = yMin; this.yMax = yMax;
        }
    }
}
