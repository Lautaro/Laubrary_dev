"""One-time pilot baseline: independent original controls/styles plus source hashes."""
import hashlib
import json
import pathlib
import shutil
import re
import uuid

root = pathlib.Path(__file__).resolve().parents[2]
package = root / 'Assets/Packages/Laubrary'
dest = root / 'Assets/Editor/UISeparationPilot/Baseline'
manifest_path = root / 'Documentation/UISeparation/baseline-manifest.json'
if manifest_path.exists():
    raise SystemExit('Baseline already frozen; do not overwrite it.')
dest.mkdir(parents=True, exist_ok=True)
controls = ['ZuiToggleButton', 'ZuiMicroSlider', 'ZuiMicroMinMax', 'ZuiSkinSlider', 'ZuiSkinBandSliders', 'ZuiSkinRangeSlider', 'ZuiSkinMinMax', 'ZuiSkinEnvelope']
for name in controls:
    source = package / ('Zui/Toolkit/' + name + '.cs')
    text = source.read_text(encoding='utf-8-sig')
    text = 'using Laubrary.Zui;\n' + text.replace('namespace Laubrary.Zui', 'namespace Laubrary.Zui.PilotBaseline', 1)
    (dest / (name + '.cs')).write_text(text, encoding='utf-8')
shutil.copyfile(package / 'Zui/Toolkit/ZuiToolkit.uss', dest / 'BaselineToolkit.uss')
shutil.copytree(package / 'Editor/Zounds/Uitk/Skin', dest / 'Skin', ignore=shutil.ignore_patterns('*.meta'))
for source_meta in (package / 'Editor/Zounds/Uitk/Skin').glob('*.png.meta'):
    settings = source_meta.read_text(encoding='utf-8-sig')
    settings = re.sub(r'^guid: [0-9a-f]+$', 'guid: ' + uuid.uuid4().hex, settings, flags=re.MULTILINE)
    (dest / 'Skin' / source_meta.name).write_text(settings, encoding='utf-8')
shutil.copyfile(package / 'Editor/Zounds/Uitk/ZoundsUitk.uss', dest / 'BaselineZoundsLayout.uss')
files = {}
for tree in ['Assets/Packages/Laubrary', 'Packages', 'ProjectSettings']:
    for path in sorted((root / tree).rglob('*')):
        if path.is_file():
            files[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
manifest_path.write_text(json.dumps({'source_project': 'D:/UNITY/Laubrary Dev', 'snapshot_commit': '9eb3d9c', 'copy_project': str(root), 'controls': controls, 'files': files}, indent=2), encoding='utf-8')
print(f'Frozen {len(controls)} controls, original styles/textures and {len(files)} file hashes.')
