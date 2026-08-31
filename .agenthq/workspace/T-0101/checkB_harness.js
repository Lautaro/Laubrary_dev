const { chromium } = require('C:/Users/Lauta/AppData/Local/Temp/pwtest/node_modules/playwright-core');
const fs = require('fs');
const OUT = 'D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0101/checkB';
const URL = 'file:///D:/CODEZ/AgentHQ/3D%20Shaper/.agenthq/attachments/T-0030/20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html';
const PROS = [-8,-4,0,4,10,18,28];

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1400, height: 950 } });
  page.on('pageerror', e => console.log('PAGEERROR', e.message));
  await page.goto(URL);
  await page.waitForTimeout(500);

  // Halt the app's rAF loop so every frame is deterministic (draw(t) animates Plasma/Light).
  await page.evaluate(() => { window.__raf = window.requestAnimationFrame; window.requestAnimationFrame = () => 0; });
  await page.waitForTimeout(200);

  // Scene setup: identical for both sets except the test layer's seq + fill.
  const setup = (seq, fill) => {
    state.mats[0].pro = 0; state.mats[1].pro = 0; state.mats[2].pro = 0; state.mats[3].pro = 0;
    state.bgMode = 'color'; state.bgColor = '#101722';
    state.activeMat = 0;
    state.layers = [ makeLayer({ shape:'ellipse', w:36, h:36, x:0, y:0, z:1, fill:fill,
                                 edgeCoverage:0.30, heightProfile:'round', bodyH:4,
                                 mode:'stretch', seq:seq }) ];
    renderLayers(); syncMaterialUI();
    return true;
  };

  const shoot = async (label, pro) => {
    await page.evaluate((pro) => {
      const el = document.getElementById('matPro');
      el.value = String(pro);
      el.dispatchEvent(new Event('input', { bubbles: true })); // exactly what a drag fires
      draw(0); // fixed t => deterministic
    }, pro);
    const url = await page.evaluate(() => document.getElementById('preview').toDataURL('image/png'));
    const sign = pro >= 0 ? '+' + pro : String(pro);
    const f = `${OUT}/${label}_pro${sign}.png`;
    fs.writeFileSync(f, Buffer.from(url.split(',')[1], 'base64'));
    return url;
  };

  const results = {};
  // --- SET A: patterned default seq (multi-material border strip) ---
  await page.evaluate("const setup=" + setup.toString() + ';setup([0,0,1,1,2,2,3,1,0,0,1,2,3,2,1,1,0,0], 1);');
  results.patterned = [];
  for (const p of PROS) results.patterned.push(await shoot('patterned', p));

  // --- SET B: control, seq all one material (mat 0) AND fill = mat 0 ---
  await page.evaluate("const setup=" + setup.toString() + ';setup([0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], 0);');
  results.flat = [];
  for (const p of PROS) results.flat.push(await shoot('flat', p));

  // --- SET C: depth-test vs neighbouring layer ---
  await page.evaluate(() => {
    state.mats[0].pro=0;state.mats[1].pro=0;state.mats[2].pro=0;state.mats[3].pro=0;
    state.bgMode='color'; state.bgColor='#101722'; state.activeMat=0;
    state.layers = [
      makeLayer({shape:'ellipse',w:52,h:52,z:0,fill:1,edgeCoverage:0,heightProfile:'flat',bodyH:0,seq:[1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1]}),
      makeLayer({shape:'ellipse',w:36,h:36,z:1,fill:1,edgeCoverage:0.30,heightProfile:'round',bodyH:4,mode:'stretch',seq:[0,0,1,1,2,2,3,1,0,0,1,2,3,2,1,1,0,0]})
    ];
    renderLayers(); syncMaterialUI();
  });
  for (const p of [-8,0,28]) await shoot('depthtest', p);

  // --- strip image (patterned sweep in order) ---
  const stripUrl = await page.evaluate(async ({urls, pros}) => {
    const imgs = await Promise.all(urls.map(u => new Promise(r => { const i = new Image(); i.onload = () => r(i); i.src = u; })));
    const c = document.createElement('canvas'); c.width = 256 * imgs.length; c.height = 292;
    const g = c.getContext('2d'); g.imageSmoothingEnabled = false;
    g.fillStyle = '#0b0e13'; g.fillRect(0, 0, c.width, c.height);
    imgs.forEach((im, i) => {
      g.drawImage(im, i * 256, 0);
      g.fillStyle = '#edf2f7'; g.font = 'bold 22px monospace'; g.textAlign = 'center';
      g.fillText('pro ' + (pros[i] >= 0 ? '+' + pros[i] : pros[i]), i * 256 + 128, 282);
      g.strokeStyle = '#2b3547'; g.strokeRect(i * 256 + .5, .5, 255, 255);
    });
    return c.toDataURL('image/png');
  }, { urls: results.patterned, pros: PROS });
  fs.writeFileSync(`${OUT}/checkB_protrusion_sweep.png`, Buffer.from(stripUrl.split(',')[1], 'base64'));

  // flat control strip too
  const stripUrl2 = await page.evaluate(async ({urls, pros}) => {
    const imgs = await Promise.all(urls.map(u => new Promise(r => { const i = new Image(); i.onload = () => r(i); i.src = u; })));
    const c = document.createElement('canvas'); c.width = 256 * imgs.length; c.height = 292;
    const g = c.getContext('2d'); g.imageSmoothingEnabled = false;
    g.fillStyle = '#0b0e13'; g.fillRect(0, 0, c.width, c.height);
    imgs.forEach((im, i) => {
      g.drawImage(im, i * 256, 0);
      g.fillStyle = '#edf2f7'; g.font = 'bold 22px monospace'; g.textAlign = 'center';
      g.fillText('pro ' + (pros[i] >= 0 ? '+' + pros[i] : pros[i]), i * 256 + 128, 282);
      g.strokeStyle = '#2b3547'; g.strokeRect(i * 256 + .5, .5, 255, 255);
    });
    return c.toDataURL('image/png');
  }, { urls: results.flat, pros: PROS });
  fs.writeFileSync(`${OUT}/checkB_flat_control_sweep.png`, Buffer.from(stripUrl2.split(',')[1], 'base64'));

  // numeric diff: mean abs pixel delta vs pro=0, per set
  const diff = (urls) => {
    const bufs = urls.map(u => Buffer.from(u.split(',')[1], 'base64'));
    return bufs.map(b => b.length);
  };
  console.log('patterned png sizes', diff(results.patterned).join(','));
  console.log('flat png sizes', diff(results.flat).join(','));
  await browser.close();
  console.log('DONE');
})().catch(e => { console.error('FATAL', e); process.exit(1); });
