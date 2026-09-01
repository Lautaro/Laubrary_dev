const {chromium}=require('C:/Users/Lauta/AppData/Local/Temp/pwtest/node_modules/playwright-core');
const fs=require('fs'),OUT='D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0101/checkB';
(async()=>{const b=await chromium.launch({headless:true});const p=await b.newPage();
await p.goto('about:blank');
const files=['flat_pro+4','flat_pro+10','flat_pro+18','flat_pro+28','patterned_pro+10','patterned_pro+18','patterned_pro+28'];
const data={};for(const f of files)data[f]='data:image/png;base64,'+fs.readFileSync(`${OUT}/${f}.png`).toString('base64');
const r=await p.evaluate(async(d)=>{
 const px=async u=>{const i=new Image();await new Promise(r=>{i.onload=r;i.src=u});
  const c=document.createElement('canvas');c.width=256;c.height=256;const g=c.getContext('2d');g.drawImage(i,0,0);
  return g.getImageData(0,0,256,256).data};
 const P={};for(const k in d)P[k]=await px(d[k]);
 const diff=(a,b)=>{let s=0,n=0,mx=0;for(let i=0;i<P[a].length;i+=4){for(let c=0;c<3;c++){const v=Math.abs(P[a][i+c]-P[b][i+c]);s+=v;n++;if(v>mx)mx=v}}return{mean:+(s/n).toFixed(3),max:mx}};
 return {
  'flat +4 vs +10':diff('flat_pro+4','flat_pro+10'),
  'flat +10 vs +18':diff('flat_pro+10','flat_pro+18'),
  'flat +18 vs +28':diff('flat_pro+18','flat_pro+28'),
  'pat +10 vs +18':diff('patterned_pro+10','patterned_pro+18'),
  'pat +18 vs +28':diff('patterned_pro+18','patterned_pro+28')};
},data);
console.log(JSON.stringify(r,null,1));await b.close()})();
