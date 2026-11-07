const fs=require('fs');
for (const f of process.argv.slice(2)) {
  const L=fs.readFileSync(f,'utf8').trim().split('\n').map(JSON.parse);
  const conns=new Set(L.map(x=>x.id));
  const sc=L.filter(x=>x.ev==='server-close');
  const srvFirst=sc.filter(x=>x.first==='server');
  const loss=sc.filter(x=>x.loss);
  const cliFirstPend=L.filter(x=>x.ev==='client-close'&&x.first==='client'&&(x.pendingToServer+x.serverWriteBuffered)>0);
  const by={}; for(const x of cliFirstPend){const k=(x.ws?'WS ':'')+x.lastReq.replace(/id=[^&]+/,'id=*'); by[k]=(by[k]||0)+1;}
  const cliFirstPendToClient=L.filter(x=>x.ev==='client-close'&&x.first==='client'&&x.pendingToClient>0).length;
  console.log(f, JSON.stringify({connections:conns.size, serverFirstCloses:srvFirst.length, serverFirstLossEvents:loss.length, lossBytes:loss.reduce((a,x)=>a+x.pendingToClient+x.clientWriteBuffered,0), clientFirstWithPendingToServer:cliFirstPend.length, byReq:by, clientFirstWithPendingToClient:cliFirstPendToClient}));
}
