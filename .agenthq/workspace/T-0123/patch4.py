import io
p = 'Assets/ChunksMock/Editor/ChunksMockWindow.cs'
s = io.open(p, encoding='utf-8').read()


def rep(old, new, n=1):
    global s
    assert s.count(old) == n, "MISS(%d!=%d): %r" % (s.count(old), n, old[:90])
    s = s.replace(old, new, n)


# the control itself needs a tooltip - the section header's "?" explains the section, not the bar
rep(
"""                this.recipe = recipe;
                this.seconds = Mathf.Max(0f, seconds);
                style.flexDirection = FlexDirection.Column;""",
"""                this.recipe = recipe;
                this.seconds = Mathf.Max(0f, seconds);
                tooltip = "One lane per time-occupying capability, all on the same clock. A lane starts at its capability's Delay and runs for its Duration, so lanes that overlap run together. Click or drag anywhere to move the playhead.";
                style.flexDirection = FlexDirection.Column;""")

# a clipped band name gets an ellipsis rather than being cut mid-word
rep(
"""                    l.style.fontSize = 9f;
                    l.style.overflow = Overflow.Hidden;""",
"""                    l.style.fontSize = 9f;
                    l.style.overflow = Overflow.Hidden;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.style.textOverflow = TextOverflow.Ellipsis;""")

io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print("patch4 ok")
