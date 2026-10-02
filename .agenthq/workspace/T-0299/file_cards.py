import urllib.request, json, sys

BASE = "http://127.0.0.1:8778"


def post(path, body):
    req = urllib.request.Request(
        BASE + path,
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        return urllib.request.urlopen(req).read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return "HTTP %s: %s" % (e.code, e.read().decode("utf-8")[:400])


cards = [
    {
        "project": "Laubrary_Dev",
        "node": "ShaperHarmony-2026-09-07",
        "title": "A24-1 New Pyre Plus.asset carries a corrupt previewLayerSel (2147483647) written by the open-time stomp that T-0299 removed",
        "priority": "medium",
        "description": (
            "`Assets/Pyre/New Pyre Plus.asset` line 18 reads `previewLayerSel: 2147483647` (int.MaxValue) on disk, "
            "on a spec with 1 layer. That is not an authored value: it is the sentinel PyreWindow.OnAssetChanged "
            "used to write into the asset on every load (`layerSel = int.MaxValue`, PyreWindow.cs:151 before "
            "T-0299), flushed to disk at a moment between that write and BuildAsset's clamp back to a real index.\n\n"
            "T-0299 removed the write, so the file is no longer harmful: measured live, binding this asset to the "
            "Pyre window now repairs the index to 0 in memory, `SelLayer` returns a valid layer, and the asset "
            "stays CLEAN (dirty=False) so nothing will flush it. But the corrupt value is still committed, so a "
            "reader of the file still sees nonsense and any future code that trusts the stored index without "
            "clamping would break on it.\n\n"
            "Measured 2026-09-08 in the Shaper worktree editor (dataPath D:/UNITY/Laubrary Dev - Shaper/Assets):\n"
            "- `New Pyre Plus.asset` previewLayerSel = 2147483647; `New Pyre Plus 1.asset` = 0; "
            "`Green Lantern.asset` = 0; `New Pyre Plus\u00a7.asset` = 1.\n"
            "- Before the fix, binding a byte-identical duplicate flipped it 2147483647 -> 0 and set dirty=True; "
            "the next domain reload then rewrote the whole 216435-byte file as 211993 bytes (a full ZuiGradient "
            "re-serialisation), with no user action at all. That is the T-0288 and T-0293 'opening Pyre "
            "republishes the asset' observation, root-caused.\n"
            "- After the fix: bind -> dirty=False, previewLayerSel repaired to 0 in memory, file untouched.\n\n"
            "OWNER DECISION: this is authored/serialized data on a committed asset, which the ShaperHarmony rules "
            "say is sacred and not for a task agent to rewrite. Either leave it (harmless now) or correct the one "
            "line to `previewLayerSel: 0`. Nothing else in the file needs touching."
        ),
    },
    {
        "project": "Laubrary_Dev",
        "node": "ShaperHarmony-2026-09-07",
        "title": "A24-2 The Tags section promises a browser filter that ZuiAssetWindow's own browser does not have",
        "priority": "medium",
        "description": (
            "Every ZuiAssetWindow (Shaper, Pyre, and every other subclass) shows a Tags section whose tooltip read "
            "'Tags for this asset \u2014 filterable in the browser.' (ZuiAssetWindow.cs:145). That browser does not "
            "filter by tags.\n\n"
            "Traced in source: `LauTagFilter` (Editor/AssetKit/LauTagFilter.cs) exists and works, but its ONLY "
            "callers are `LauAssetBrowser.cs:332` and `:338` \u2014 the older IMGUI browser. "
            "`ZuiAssetWindow.BuildBrowser()` (line 334) builds a header (`'<Type> library (N)'`), an "
            "'\u25b6 Animate all' toggle, a 'Refresh' button and the grid; `RefreshBrowse()` is "
            "`AssetLibrary<T>.Enumerate()` with no tag argument. No tag filter anywhere.\n\n"
            "Measured live 2026-09-08: tagging itself round-trips correctly (added 'A24AuditTag' to a document -> "
            "1 tag, removed -> 0, tag deleted, library back to its original 6 entries, no residue). With the "
            "browser open, a scan of every control in the window for the words 'tag' or 'filter' found nothing "
            "outside the Tags section itself.\n\n"
            "T-0299 corrected the tooltip so it no longer promises the filter (it now says the tags are shared "
            "with the rest of Laubrary and that this window's browser lists every asset and does not filter by "
            "them). The missing affordance is left for the owner: ShaperHarmony rule 6 forbids a task agent adding "
            "a new control. OWNER DECISION: add a tag filter to the ZuiAssetWindow grid (the filter logic already "
            "exists in LauTagFilter and only needs a control), or accept that tags in these windows are labels "
            "only."
        ),
    },
    {
        "project": "Laubrary_Dev",
        "node": "ShaperHarmony-2026-09-07",
        "title": "A24-3 PyreWindow sets no minSize, so its window can be resized to an unusable sliver",
        "priority": "low",
        "description": (
            "Measured live 2026-09-08: `PyreWindow.minSize` reads **50x50** \u2014 Unity's default floor, i.e. the "
            "window never sets one. Setting `position` to that minimum produced a real 124.4x50 window in which "
            "22 of 27 buttons were entirely outside the window's right edge.\n\n"
            "ShaperWindow by contrast declares `minSize = (820, 520)` (confirmed live in the same session), which "
            "is what made T-0296's clamp testable against a documented floor.\n\n"
            "This is separate from, and was found alongside, the Pyre splitter clamp T-0299 fixed: with the clamp "
            "in place the dial pane now tracks the window down correctly (verified at 900x700: pane clamped 1458 "
            "-> 640, 0 of 65 buttons off-screen; widened back to 1900 -> regrew to 1458, 0 of 27 off-screen), so "
            "Pyre degrades gracefully at any sane size. A minSize would simply stop the window reaching sizes "
            "where no layout can help.\n\n"
            "OWNER DECISION: give PyreWindow a minSize in the spirit of Shaper's 820x520, or leave it. Not done "
            "here because picking the number is an authoring decision, not a defect fix."
        ),
    },
]

for c in cards:
    print(c["title"][:60], "->", post("/api/task/create", c)[:300])
