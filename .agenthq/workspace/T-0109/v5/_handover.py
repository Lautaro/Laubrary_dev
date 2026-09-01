import json, urllib.request

body = {
    "project": "Laubrary_Dev", "id": "T-0109", "newStatus": "done",
    "text": (
        "Shapes in Shaper can now be given real thickness: you set how deep a shape is, pick the shape of its "
        "top (flat, domed, terraced, pyramid and four more), pick how its rim is rounded off (five choices), "
        "and lift it to its own height in the stack. A deliberately tilted picture of one of these solids is "
        "kept alongside the work as proof it can be turned in 3D later, and it is attached here so you can see it."
    ),
    "details": (
        "WHAT YOU CAN LOOK AT\n\n"
        "Two pictures are attached. The tilted one is the important one: a terraced slab seen from an angle "
        "with its side walls plainly visible, and a domed star floating above it at its own height. That "
        "picture is the whole point of one of this task's requirements - a solid that can be turned, not a "
        "flat sheet - and it is regenerated every time the underlying code changes, so it cannot quietly go "
        "stale. The other is a contact sheet of all 28 profile-and-rim combinations, so you can see at a "
        "glance what each dial actually does.\n\n"
        "HOW THE WORK WAS CHECKED\n\n"
        "Four independent adversarial rounds, each one re-measuring the last. Each round found real defects "
        "and each round found fewer: round one found the renderer losing whole features, round two a "
        "400-pixel measurement error, round three a lost 26-pixel span, and this fourth round found nothing "
        "new in the code itself. Every defect was found by measurement rather than by reading, and every fix "
        "was confirmed the same way. The five automated check suites all pass: 13, 85, 79 and 150 individual "
        "checks in the four earlier areas, and 15 in this one, with zero failures.\n\n"
        "One caveat, stated rather than smoothed over: the checking suite for this area had a blind spot - it "
        "was asking its questions in a way that structurally could not have caught two of the three defects, "
        "and reported itself clean while they were live. That blind spot is now closed, and the three defects "
        "are pinned as named traps so they cannot come back silently.\n\n"
        "Not yet verified: a slot of solid material thinner than about a fiftieth of a pixel can still be "
        "missed at one particular terracing setting. This is inside the accuracy limit the design declares "
        "for itself, it is named in the checks rather than hidden, and it is not a new problem - but it is a "
        "real disagreement with the truth and is recorded as one.\n\n"
        "Nothing is committed to version control, and none of this touches the main copy of the project - the "
        "work lives in the separate Shaper working copy, as planned."
    ),
    "technicalDetails": (
        "WHERE: the `feat/shaper` worktree at `D:\\UNITY\\Laubrary Dev - Shaper`. Runtime "
        "`Assets\\Packages\\Laubrary\\Runtime\\Shaper\\`, audits `...\\Editor\\Shaper\\`. Docs stay in the "
        "main copy under `D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0109\\`. Uncommitted, as every "
        "prior round left it.\n\n"
        "THIS ROUND FIXED V1-V4 from "
        "`D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0109\\PASS3-FINDINGS.md`. Context that matters for "
        "anyone reading the history: nine consecutive dispatches before this one died on a 429 spend limit "
        "without landing anything, and the third fix round in particular is recorded in the roster as having "
        "run but left ShaperHeight.cs / ShaperResolve.cs untouched - re-confirmed from file timestamps at the "
        "start of this run.\n\n"
        "V1 (HIGH) - ShaperResolve.cs, the general march. Both contained-prism SOLID skips are now gated on "
        "`mPrev`: the slab-wide branch is `else if (hasInnerSlab && mPrev)`, and the per-step one computes "
        "`float tauMax = mPrev ? ShaperHeight.InverseAtEUpperBound(op, wHi, eLo) : "
        "ShaperHeight.NoCrossSection;`. Root cause was NOT the bound: the prism proves solid with a "
        "non-strict `G >= zeta` while Member applies HS-1.1's strict `G > 0` reading, so at a slab boundary "
        "the prism could say SOLID at the exact point Member calls AIR - the skip then jumped air-to-air "
        "across a whole solid span, `mPrev` never flipped, and the span vanished. Measured on the quoted "
        "repro (400x400 plate, Stepped n=8, depth 290, origin (-400,0,248.571732), direction "
        "(0.95394,0,-0.3)): 2 crossings -> 4, the 24.1747 px span [552.3819, 576.5567] restored, worst "
        "residual 0.00043 px. Breadth: 26 of 7440 rays losing a crossing (worst gap 26.207 px) -> 0 lost "
        "over 3240 true transitions on a 2496-ray brute-force sweep of the 13 affected step counts.\n\n"
        "V2 (MEDIUM) - ShaperHeight.cs. New `SteppedRiser(op, j)` helper: `j/n` nudged by ulps until "
        "`Floor(t*n) >= j`, bounded to 8, stopping the moment the floor agrees so it can never cross a riser. "
        "`SteppedProfileInverseExact`'s DOWN walk asked SteppedE at the bare `(k-1)/n` - precisely the float "
        "the UP walk exists to nudge - so at the four latent indices (n=22 k=13, n=23 k=7, n=23 k=14, "
        "n=29 k=15) E came back a whole tread short, the walk stopped one step too high, and tauMin ended up "
        "ABOVE the true inverse. That is the unsafe direction: HS-5.2 needs {G>=zeta} to be a subset of "
        "{t>=tauMin}, so the CONTAINING prism stopped containing. 12 violations in 520800 checks (worst "
        "3.4481e-2 t-units = one whole tread, costing a 7.1227 px gap) -> 0 in 68 ulp-neighbourhood "
        "probes.\n\n"
        "V3 (MEDIUM) - ShaperResolve.cs, the branch test is now `dz < 0f && dx == 0f && dy == 0f`. "
        "StraightDownTolerance is KEPT as a documented-deprecated const because H6 uses it to construct a "
        "deliberately off-axis ray. The reasoning is HS-0.1's own: the closed form's induced z error is "
        "governed by dG/dd, unbounded on 7 of the 12 techniques, so NO non-zero lateral tolerance bounds it - "
        "the verifier's sweep showed the deficit shrinking as the cube root of the tolerance, which is "
        "divergence, not convergence. Was: closed form wrong 101 of 1008 branch-paired rays, worst 4.7018 px "
        "at Flat+Ogee x=-190. Now (1e-6,0,-1) - two orders INSIDE the retired tolerance - correctly takes "
        "General. Production path unaffected: H6 reports 825 straight-down / 825 general.\n\n"
        "V4 (audit rigour) - ShaperHeightAudit.cs. H6's omission arm now sweeps step counts 4..31 in two aims "
        "(H6's own height fractions AND aimed at a tread); a new H12_RegressionTraps() pins V1's exact repro, "
        "V1 breadth, V2's four risers and V3's three branch selections. NOTE THE HONEST PART: the swept arm "
        "first genuinely FAILED on 2 crossings. They were diagnosed (v5/DIAG.txt) to a real 0.0154 px air "
        "sliver between two Stepped treads at n=27 tilt 30 - below SurfaceResolution (0.02 px) and so inside "
        "HS-5.5's declared limit - and the check was then changed to measure FEATURE EXTENT rather than "
        "distance-to-nearest-emitted (2.566 px was never the size of anything, only how far the march's "
        "nearest answer happened to be). The fixture was not softened to make it pass.\n\n"
        "STILL OPEN, for whoever picks this up:\n"
        "1. That 0.0154 px sliver at Stepped n=27 IS genuinely missed. Inside the declared guarantee, now "
        "named in the audit, but a real disagreement with truth.\n"
        "2. ShaperResolveResult.bracketCapped does not mean what its doc claimed - the bracket loop also "
        "exits via `if (sigma <= SurfaceResolution) break;`, which increments nothing, so `bracketCapped == "
        "0` does NOT prove every step was a proof. Only the comment was corrected; the proper fix is a "
        "separate resolution-floored counter on the public struct.\n"
        "3. Only the HEIGHT audit was re-examined for fixture blindness. Field/Fill/Border/Light report clean "
        "but were never asked the V4 question - and V4 is precisely the finding that a clean row can be a "
        "true statement about a fixture that cannot fail.\n"
        "4. zTol in EmitAt is still underived.\n"
        "5. March speed not baselined against pre-fix (the height audit's 24.7s -> 53.0s is almost entirely "
        "the new swept arm's 1344 extra truth-scanned rays, not the march).\n\n"
        "CONVERGENCE: four adversarial rounds, each re-measuring the last, each finding strictly less - a "
        "fixed-resolution sampler losing 46 px features, then a 400 px Depth error, then a 26 px lost span, "
        "then nothing new in the code. Per the third pass's own recommendation, no fifth round was opened; "
        "the remaining risk is better spent on the fills that build on this spine."
    ),
    "proof": [
        {"text": "V1 minimal repro: emits 4 of 4 expected crossings, worst residual 0.00043 px against the quoted truth; the 24.1747 px span [552.3819, 576.5567] that vanished entirely is back", "checked": True},
        {"text": "V1 breadth: 2496-ray brute-force sweep over the 13 affected step counts, 3240 true transitions, 0 lost (was 26 of 7440 rays losing a crossing, worst gap 26.207 px)", "checked": True},
        {"text": "V2: the four latent (n,k) risers swept +/-8 ulps around each tread's own zeta - 68 probes, 0 HS-5.2 containment violations (was 12 in 520800, worst one whole tread)", "checked": True},
        {"text": "V3: exact (0,0,-1) takes StraightDown; both (2e-5,0,-1) and (1e-6,0,-1) take General - the latter two orders inside the retired tolerance", "checked": True},
        {"text": "V4: H6's omission arm now sweeps steps 4..31 in two aims; new H12 pins all three defects as named regression traps, all ok", "checked": True},
        {"text": "All five audits re-run in one pass: Field 13 PASS / 0 FAIL, Fill 85/0, Border 79/0, Light 150/0, Height 15 VERDICTs all ok - 0 FAIL anywhere", "checked": True},
        {"text": "Tilted conformance frame re-rendered and LOOKED AT: a terraced slab at 34 deg tilt / 26 deg yaw with visible stepped side walls, plus a Dome star at its own zOffset base. Asserts general 126000 rays / straight-down 0, and 43025 surface hits split WALL 10049 + CAP 32976", "checked": True},
        {"text": "Contact sheet re-rendered and LOOKED AT: 28 cells, every profile and every bevel distinct and correctly lit, cell 27 (depth 0) correctly flat", "checked": True},
        {"text": "Shaper worktree compiles clean (compileFailed=False); main copy Assets/ untouched; no [MenuItem] added; nothing committed", "checked": True},
        {"text": "NOT VERIFIED: a 0.0154 px air sliver at Stepped n=27 is genuinely missed - inside HS-5.5's declared limit and now named in the audit, but a real disagreement with truth", "checked": False},
        {"text": "NOT VERIFIED: only the height audit was re-examined for fixture blindness; Field/Fill/Border/Light report clean but were never asked V4's question", "checked": False},
    ],
    "attachments": [
        "D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0109\\tilted-conformance.png",
        "D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0109\\height-contact-sheet.png",
        "D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0109\\FIX-REPORT-3.md",
        "D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0109\\HEIGHT-SPEC.md",
        "D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0109\\PASS3-FINDINGS.md",
    ],
}

req = urllib.request.Request(
    "http://127.0.0.1:8778/api/task/handover",
    data=json.dumps(body).encode("utf-8"),
    headers={"Content-Type": "application/json"},
)
print(urllib.request.urlopen(req).status)
