import io
p = 'Assets/ChunksMock/Editor/ChunksMockWindow.cs'
s = io.open(p, encoding='utf-8').read()


def rep(old, new, n=1):
    global s
    assert s.count(old) == n, "MISS(%d!=%d): %r" % (s.count(old), n, old[:90])
    s = s.replace(old, new, n)


# carry the playhead across a rebuild, exactly as the scroll offset is carried
rep(
"""        ScrollView leftPane;
        Vector2 carriedScroll;
""",
"""        ScrollView leftPane;
        Vector2 carriedScroll;
        float carriedSeconds;
""")

rep(
"""            if (leftPane != null) carriedScroll = leftPane.scrollOffset;
            leftPane = null;
        }""",
"""            if (leftPane != null) carriedScroll = leftPane.scrollOffset;
            if (tracks != null) carriedSeconds = tracks.Seconds;
            leftPane = null;
        }""")

rep(
"""            tracks = new MockTimingTracks(recipe);""",
"""            tracks = new MockTimingTracks(recipe, carriedSeconds);""")

rep(
"""            public MockTimingTracks(MockRecipe recipe)
            {
                this.recipe = recipe;""",
"""            /// Where the playhead is. The host reads it back before a rebuild and hands it to the next
            /// instance, so toggling a capability does not throw the playhead back to zero.
            public float Seconds => seconds;

            public MockTimingTracks(MockRecipe recipe, float seconds)
            {
                this.recipe = recipe;
                this.seconds = Mathf.Max(0f, seconds);""")

# the leading and trailing tick labels have to be pulled inside the bar or they are clipped
rep(
"""                    var l = new Label(t.ToString("0.00") + "s") { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.top = 0f;
                    l.style.left = x - 20f;
                    l.style.width = 40f;""",
"""                    var l = new Label(t.ToString("0.00") + "s") { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.top = 0f;
                    // Pulled inside the bar rather than centred on its tick: the first and last numbers are
                    // the ones a reader needs most, and half of "0.00s" hanging off the edge reads as ".00s".
                    l.style.left = Mathf.Clamp(x - 20f, 0f, Mathf.Max(0f, w - 40f));
                    l.style.width = 40f;""")

rep(
"""                playLabel.text = seconds.ToString("0.00") + "s";
                playLabel.style.left = X(seconds) - 22f;
                playLabel.style.width = 44f;""",
"""                playLabel.text = seconds.ToString("0.00") + "s";
                playLabel.style.left = Mathf.Clamp(X(seconds) - 22f, 0f, Mathf.Max(0f, BarWidth - 44f));
                playLabel.style.width = 44f;""")

io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print("patch3 ok")
