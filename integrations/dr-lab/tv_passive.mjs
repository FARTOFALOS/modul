// Only read existing panes. CDP attachment does not activate a tab.
export function paneExpression(symbol, count, need5 = false) {
 return `(() => {
  const out=[], wanted=${JSON.stringify(symbol)}, bare=wanted.split(':').pop();
  try { const ws=window.TradingViewApi._chartWidgetCollection.getAll();
   for(let k=0;k<ws.length;k++)try {
    const ms=ws[k].model().mainSeries(), si=ms.symbolInfo(), b=ms.bars(), interval=String(ms.interval());
    const feed=si&&String(si.pro_name||si.full_name||si.ticker||si.name||'');
    if(!si||!${JSON.stringify(need5 ? ['5'] : ['5','1'])}.includes(interval)||ms.isLoading()||!b||b.size()<50)continue;
    if(feed!==wanted&&feed!==bare)continue;
    const bars=[],end=b.lastIndex();
    for(let i=Math.max(b.firstIndex(),end-${count}+1);i<=end;i++){const v=b.valueAt(i);if(v)bars.push([v[0],v[1],v[2],v[3],v[4]]);}
    if(bars.length)out.push({feed,interval,pane:k,bars});
   }catch(_){}
  }catch(_){} return out;
 })()`;
}
export async function collectPassive(conn, targets, symbol, count, need5=false) {
 const candidates=[];
 for(const t of targets){
  if(t.type!=='page'||!/tradingview\.com\/chart/i.test(t.url||''))continue;
  try{await conn.reconnectTo(t.id);
   for(const p of await conn.evaluate(paneExpression(symbol,count,need5))||[])
    if(p.bars.every(b=>b.length===5&&b.every(Number.isFinite)))candidates.push({...p,target:t.id});
  }catch(_){}
 }
 candidates.sort((a,b)=>Number(b.interval)-Number(a.interval)||b.bars.at(-1)[0]-a.bars.at(-1)[0]||b.bars.length-a.bars.length);
 if(!candidates.length)return {success:false,switched:false,error:`Нет готовой панели ${symbol} ${need5?'M5':'M5/M1'} в открытых окнах TradingView. Графики не изменены.`};
 return {success:true,symbol,...candidates[0],switched:false};
}
