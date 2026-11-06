// Passive reads only: never activate or modify a TradingView chart.
import {pathToFileURL} from 'node:url';
import {homedir} from 'node:os';
import {join} from 'node:path';
import {collectPassive} from './tv_passive.mjs';
let conn;
try {
 const [symbol,n,mode]=process.argv.slice(2),count=Math.min(Number(n||500),3000);
 if(!symbol||!Number.isInteger(count)||count<1)throw new Error('symbol and positive integer count required');
 conn=await import(pathToFileURL(join(process.env.TV_MCP_DIR||join(homedir(),'Claude','tradingview-mcp'),'src','connection.js')).href);
 const targets=await(await fetch(`http://${conn.CDP_HOST}:${conn.CDP_PORT}/json/list`)).json();
 console.log(JSON.stringify(await collectPassive(conn,targets,symbol,count,mode==='need5')));
} catch(e){console.log(JSON.stringify({success:false,switched:false,error:String(e.message||e)}));}
finally{if(conn)await conn.disconnect().catch(()=>{});}
