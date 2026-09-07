import json, urllib.request
B = "http://127.0.0.1:8778"


def post(path, body):
    req = urllib.request.Request(B + path, data=json.dumps(body).encode("utf-8"),
                                 headers={"Content-Type": "application/json; charset=utf-8"})
    try:
        r = urllib.request.urlopen(req)
        print(path, r.status, r.read()[:80])
    except urllib.error.HTTPError as e:
        print(path, "ERR", e.code, e.read()[:300])


for i in (5, 6, 7, 8, 9, 10):
    post("/api/task/todo/check", {"project": "Laubrary_Dev", "id": "T-0239", "todoIndex": i, "checked": True, "actor": "agent"})
for i in range(1, 8):
    post("/api/task/todo/check", {"project": "Laubrary_Dev", "id": "T-0245", "todoIndex": i, "checked": True, "actor": "agent"})

W = r"D:\UNITY\Laubrary Dev\.agenthq\workspace"

post("/api/task/todo/message", {
    "project": "Laubrary_Dev", "id": "T-0239", "todoIndex": 8, "actor": "agent", "blocking": True,
    "text": "Owner decision: which owns the muzzle flash - the Zoe Fire event (SHIPPED) or the weapon's own muzzle slot? "
            "Trade-off: weapon-owned means the same gun looks right on any character and each weapon can carry its own distinct flash "
            "(the Scattergun still does, so it currently double-flashes when active: its own flash plus the Fire event's); "
            "event-owned means the flash is configurable in the Zoe window per the palette rule (a character shows things only through "
            "its declared list) and one flash per character regardless of weapon. Both paths now rotate and mirror correctly, so flipping "
            "is a two-field data change (see the handover details)."})

post("/api/task/handover", {
    "project": "Laubrary_Dev", "id": "T-0239",
    "text": "ProtoGuy now fires ONE muzzle flash, at the painted muzzle of the gun, turned to point the way the bullet goes and mirrored for a "
            "left-facing shot. The extra flash at a default spot is gone: it was a second copy that had always been authored on top of the "
            "first, and only one of them had been moved.",
    "details": "How to use this: in the Zoe window, an effect card's Rotate row has None / Face event direction / Fixed angle, a Direction pick, "
               "an Offset slider and a 'Mirror left' switch. Face event direction + Event Direction is what the Fire flash uses; Mirror left keeps "
               "a left-facing shot from being drawn upside down.\n\n"
               "Shipped ownership: the Zoe Fire event owns the flash (the gun's own muzzle slot is empty). To flip to the weapon-owned model "
               "instead: set ProtoGuy Gun's Muzzle effect back to the Directional Blast Plus flash, and delete the Pyre + Chunks row from "
               "ProtoGuy's Fire event (keep the Body SpriteFx relight row). Both paths rotate and mirror the same way now.\n\n"
               "Known: the Scattergun still carries its own weapon-owned flash, so switching to it in the demo shows two flashes (its own plus "
               "the Fire event's) until the ownership question is decided.\n\n"
               "Verified by eye: three Play-mode frames attached - fire right, fire left, fire up-left - each shows a single flame at the barrel "
               "pointing with the shot.\n"
               "Verified by probe: 13 edit-mode tests passed (0 failed), run by reflection rather than the Test Runner per the project rule; one "
               "test pins ProtoGuy to exactly one blast per Fire.\n"
               "Not verified: a human has not dragged the new Zoe controls yet.",
    "technicalDetails": "Root cause of the rejection: WeaponDef.muzzle (ProtoGuy Gun) and the Zoe Fire event's PyreChunksFx row both spawned "
                        "Driectional Blast Plus at the painted Muzzle meta point; the first attempt moved only the event row to BodyPart+offset. "
                        "Fix: D:\\UNITY\\Laubrary Dev\\Assets\\Demos\\ProtoGuyDemo\\ProtoGuy Gun.asset muzzle cleared; "
                        "D:\\UNITY\\Laubrary Dev\\Assets\\Demos\\ProtoGuyDemo\\ProtoGuy.asset Fire row = MetaPoint/Muzzle, EventDirection, "
                        "FaceEventDirection, flipWithFacing. Second real bug found: "
                        "D:\\UNITY\\Laubrary Dev\\Assets\\Packages\\Laubrary\\Runtime\\Zoetrope\\ZoeSpawner.cs only added WeaponMuzzleCue when "
                        "def.muzzle was set, so a weapon without a muzzle slot never raised the Fire state at all - the cue is now always added. "
                        "Orientation is resolved once in EventContext.ResolveOrientation "
                        "(D:\\UNITY\\Laubrary Dev\\Assets\\Packages\\Laubrary\\Runtime\\Zoetrope\\EventContext.cs): rotation NaN/Fixed/aim+offset, "
                        "flip = pointing-left when rotated (cos < -1e-4) else body facing; FxEntry.rotation defaults to None; ICombatFx gained "
                        "PlayOriented(pos, aimDeg, flipX) and a flipX param on PlayFollowable; WeaponMuzzleCue.PlayMuzzleFx uses it. Pyre-side maths "
                        "lives in PyreAnchor (T-0245). Editor: Zoe card rotation row rebuilt (Offset/Angle MicroSliders, per-mode Mirror-left "
                        "tooltip, IEffectOrientationHint note), and the MetaPoint Layer picker now lists Point AND Vector layers across a composite "
                        "view via WeaponAttachmentLibrary.FindMuzzleLayerCandidates (it showed '(none authored)' for ProtoGuy before). Tests: "
                        "D:\\UNITY\\Laubrary Dev\\Assets\\Tests\\ZoeCharacter\\EventContextResolverTests.cs (DirectionalFxEntryTests.cs deleted as a "
                        "duplicate). Probe script kept at D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0239\\probe_fire.cs. Nothing committed.",
    "proof": [
        {"checked": True, "text": "Compile clean: EditorUtility.scriptCompilationFailed == false and PyreAnchor / ZuiVectorMarker / IEffectOrientationHint resolve via eval"},
        {"checked": True, "text": "13/13 edit-mode tests pass (PyreAnchorTests 6, EventContextResolverTests 7) via the reflection harness"},
        {"checked": True, "text": "Play mode: right / left / up-left shots each show exactly one blast at the muzzle (rotZ 270 / 91+mirror / 52+mirror)"},
        {"checked": True, "text": "ZuiAudit on the Zoe window: no findings on the touched card (3 pre-existing over-width buttons elsewhere: Preview in Mirage, Paint Muzzle x2)"}],
    "attachments": [W + r"\T-0239\fire_right_crop.png", W + r"\T-0239\fire_left_crop.png", W + r"\T-0239\fire_upleft_crop.png",
                    W + r"\T-0239\fire_right.png", W + r"\T-0239\fire_left.png", W + r"\T-0239\fire_upleft.png",
                    W + r"\T-0245\zoe_fire_card.png"]})
post("/api/task/status", {"project": "Laubrary_Dev", "id": "T-0239", "status": "done"})

post("/api/task/handover", {
    "project": "Laubrary_Dev", "id": "T-0245",
    "text": "A Pyre can now carry an optional anchor - a point (its base, start or centre) or a point plus a facing arrow - set in the Pyre "
            "window and placed by clicking the preview. Anything that places the Pyre puts that point on the spawn spot and, with a facing, "
            "turns it the right way; Pyres without an anchor behave exactly as before.",
    "details": "Anchor vs origin: they should be ONE thing, and that is how it is built - a Pyre with no anchor keeps its implicit origin (the "
               "canvas centre); the moment you set an anchor, that point IS the origin everything pins to. There is no second point to keep in "
               "sync.\n\n"
               "How to use this: open the Pyre in its window, Canvas section, press Anchor. Choose Position or Vector. Click the preview to place "
               "the dot; drag the dot to move it; for a Vector, drag the arrowhead to aim. Every edit is undoable. The anchor does nothing by "
               "itself - it only tells placing systems how to treat the Pyre.\n\n"
               "First use: ProtoGuy's Directional Blast Plus now has a Vector anchor at the base of its flame pointing along the flame, and the "
               "Fire event points it along the shot.\n\n"
               "Verified by eye: the Pyre window capture attached shows the Anchor controls and the marker on the flame; the ProtoGuy frames on "
               "T-0239 show it in play.\n"
               "Verified by probe: 6 anchor-maths tests pass.\n"
               "Not verified: no human has dragged the marker in the Pyre window yet.",
    "technicalDetails": "Data: D:\\UNITY\\Laubrary Dev\\Assets\\Packages\\Laubrary\\Runtime\\Pyre\\Pyre.cs - anchorEnabled (false), anchorKind "
                        "(PyreAnchorKind.Position|Vector), anchorOrigin (0..1, bottom-left, same convention as Launimator's VectorMetaFrame), "
                        "anchorDirection (local, (1,0)=+X). Maths: D:\\UNITY\\Laubrary Dev\\Assets\\Packages\\Laubrary\\Runtime\\Pyre\\PyreAnchor.cs - "
                        "AnchorLocal, ForwardLocal (+X when no vector anchor), RotationDeg(forward, aimDeg, flipX) mirrors the forward before "
                        "solving so a left shot is flip + small angle, SpawnPosition(spawn, anchorLocal, rot, flip, scale) = spawn - R*S*anchor, "
                        "Place(transform, ...). Consumers: SpawnPyreFx and PyreChunksFx "
                        "(D:\\UNITY\\Laubrary Dev\\Assets\\Packages\\Laubrary\\Runtime\\ZoetropePyre\\) both call PyreAnchor.Place and implement "
                        "IEffectOrientationHint. Shared marker: D:\\UNITY\\Laubrary Dev\\Assets\\Packages\\Laubrary\\Zui\\Toolkit\\ZuiVectorMarker.cs "
                        "(Canvas/Data/Options, Draw, Handle with a beginEdit undo callback); LauminationBuilderWindow's DrawVectorOverlay / "
                        "HandleVectorInput now delegate to it (only middle-drag pan stays local). Pyre window: Canvas section Anchor toggle + "
                        "Position/Vector segmented (hidden-not-removed while off); overlay in PyreWindow.Preview.cs DrawAnchorOverlay after the "
                        "swarm overlay, Undo.RecordObject per gesture, no frame-cache invalidation. Authored anchor on "
                        "D:\\UNITY\\Laubrary Dev\\Assets\\Pyre\\Imported\\Driectional Blast Plus.asset: origin (0.5, 0.39), direction (0,1) - read "
                        "off a rendered contact sheet (attached). That asset's diff looks large only because Unity rewrote it with LF line endings; "
                        "the real change is the four anchor lines plus previewLayerSel. Tests: "
                        "D:\\UNITY\\Laubrary Dev\\Assets\\Tests\\Pyre\\PyreAnchorTests.cs. Nothing committed.",
    "proof": [
        {"checked": True, "text": "Compile clean; PyreAnchor and ZuiVectorMarker resolve via eval"},
        {"checked": True, "text": "PyreAnchorTests 6/6 pass (offset for rotations/mirror/scale round-trip, anchor-direction rotation right/left/diagonal, Place with a vector anchor)"},
        {"checked": True, "text": "Pyre window: Anchor toggle and Position/Vector control visible only with the anchor on; marker drawn on the preview (capture attached)"},
        {"checked": True, "text": "ZuiAudit on the Pyre window: 0 findings"}],
    "attachments": [W + r"\T-0245\pyre_anchor.png", W + r"\T-0245\window_pyre.png", W + r"\T-0245\blast_frames.png"]})
post("/api/task/status", {"project": "Laubrary_Dev", "id": "T-0245", "status": "done"})
