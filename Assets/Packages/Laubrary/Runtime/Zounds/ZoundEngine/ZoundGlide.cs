namespace Laubrary.Zounds {

    /// <summary>
    /// One glide to a snapshot, as returned by <see cref="ZoundToken.GlideToSnapshot"/> (T-0498). It can glide back to exactly
    /// where the glide began -- its settings and the volume and pitch the play had then, not a fresh draw.
    ///
    /// Only the LATEST glide on a play can be reversed: once another glide has started, this handle is stale and gliding
    /// back does nothing (and says so by returning an invalid handle), because its starting point would mix with the newer
    /// glide's changes. A glide back is itself a glide and returns its own handle, so going back and forth is a chain of
    /// handles, and calling GlideBack twice on the same handle does nothing the second time. A handle also goes stale when
    /// its play ends or the token plays again. Allocation-free.
    /// </summary>
    public readonly struct ZoundGlide {

        readonly ZoundToken token;
        readonly int serial, run;
        readonly float seconds;

        internal ZoundGlide(ZoundToken token, int serial, int run, float seconds) {
            this.token = token; this.serial = serial; this.run = run; this.seconds = seconds;
        }

        /// <summary>Whether this is still the latest glide of a play that is still under way (so GlideBack would work).</summary>
        public bool isCurrent => token != null && token.glideSerial == serial && token.runCount == run && token.isRunning;

        /// <summary>
        /// Glides back to exactly where this glide began, over <paramref name="milliseconds"/> (below nought: as long as this
        /// glide took). Returns the glide back's own handle, or an invalid one when this handle is stale.
        /// </summary>
        public ZoundGlide GlideBack(float milliseconds = -1f) {
            if (!isCurrent) return default;
            return token.GlideBack(milliseconds < 0f ? seconds : milliseconds * 0.001f);
        }
    }
}
