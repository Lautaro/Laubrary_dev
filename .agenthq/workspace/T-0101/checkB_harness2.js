const { chromium } = require('C:/Users/Lauta/AppData/Local/Temp/pwtest/node_modules/playwright-core');
const fs = require('fs');
const OUT = 'D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0101/checkB';
const URL = 'file:///D:/CODEZ/AgentHQ/3D%20Shaper/.agenthq/attachments/T-0030/20260829T063646Z_retro_scifi_flyer_lab_draft6-1.html';
const PROS = [-8,-4,0,4,10,18,28];

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1400, height: 950 } });
  await page.goto(URL); await page.waitForTimeout(400);
  await page.evaluate(() => { window.requestAnimationFrame = () => 0; });
  await page.waitForTimeout(150);

  const run = async (label, seq, fill, view) => {
    await page.evaluate(({seq, fill, view}) => {
      state.mats.forEach(m => m.pro = 0);
      state.bgMode='color'; state.bgColor='#101722'; state.activeMat=0; state.view=view;
      state.layers=[ makeLayer({shape:'ellipse',w:36,h:36,z:1,fill:fill,edgeCoverage:0.30,
                                heightProfile:'round',bodyH:4,mode:'stretch',seq:seq}) ];
      renderLayers(); syncMaterialUI();
    }, {seq, fill, view});
    const frames = [];
    for (const pro of PROS) {
      const url = await page.evaluate((pro) => {
        const el=document.getElementById('matPro'); el.value=String(pro);
        el.dispatchEvent(new Event('input',{bubbles:true})); draw(0);
        const c=document.getElementById('preview');
        return { url:c.toDataURL('image/png'),
                 px:Array.from(c.getContext('2d').getImageData(0,0,256,256).data) };
      }, pro);
      frames.push(url);
      if (view==='height') fs.writeFileSync(`${OUT}/${label}_height_pro${pro>=0?'+':''}${pro}.png`,
        Buffer.from(url.url.split(',')[1],'base64'));
    }
    // strip
    const strip = await page.evaluate(async ({urls,pros}) => {
      const imgs = await Promise.all(urls.map(u=>new Promise(r=>{const i=new Image();i.onload=()=>r(i);i.src=u})));
      const c=document.createElement('canvas'); c.width=256*imgs.length; c.height=292;
      const g=c.getContext('2d'); g.imageSmoothingEnabled=false;
      g.fillStyle='#0b0e13'; g.fillRect(0,0,c.width,c.height);
      imgs.forEach((im,i)=>{g.drawImage(im,i*256,0);g.fillStyle='#edf2f7';g.font='bold 22px monospace';
        g.textAlign='center';g.fillText('pro '+(pros[i]>=0?'+'+pros[i]:pros[i]),i*256+128,282);
        g.strokeStyle='#2b3547';g.strokeRect(i*256+.5,.5,255,255)});
      return c.toDataURL('image/png');
    }, {urls:frames.map(f=>f.url), pros:PROS});
    if (view==='height') fs.writeFileSync(`${OUT}/checkB_${label}_HEIGHTVIEW_sweep.png`, Buffer.from(strip.split(',')[1],'base64'));

    // mean abs pixel delta vs pro=0
    const zero = frames[PROS.indexOf(0)].px;
    const deltas = frames.map(f => {
      let s=0,n=0; for(let i=0;i<zero.length;i+=4){ s+=Math.abs(f.px[i]-zero[i])+Math.abs(f.px[i+1]-zero[i+1])+Math.abs(f.px[i+2]-zero[i+2]); n+=3; }
      return (s/n).toFixed(2);
    });
    const changed = frames.map(f => {
      let c=0; for(let i=0;i<zero.length;i+=4){ if(Math.abs(f.px[i]-zero[i])>3||Math.abs(f.px[i+1]-zero[i+1])>3||Math.abs(f.px[i+2]-zero[i+2])>3) c++; }
      return (100*c/(256*256)).toFixed(1)+'%';
    });
    console.log(`${label}/${view} meanAbsDelta vs pro=0 : ` + PROS.map((p,i)=>`${p}:${deltas[i]}`).join('  '));
    console.log(`${label}/${view} %pixels changed      : ` + PROS.map((p,i)=>`${p}:${changed[i]}`).join('  '));
  };

  const SEQ_PAT=[0,0,1,1,2,2,3,1,0,0,1,2,3,2,1,1,0,0], SEQ_FLAT=new Array(18).fill(0);
  await run('patterned', SEQ_PAT, 1, 'pixel');
  await run('flat',      SEQ_FLAT, 0, 'pixel');
  await run('flat',      SEQ_FLAT, 0, 'height');
  await run('patterned', SEQ_PAT, 1, 'height');
  await browser.close(); console.log('DONE');
})().catch(e=>{console.error('FATAL',e);process.exit(1)});
