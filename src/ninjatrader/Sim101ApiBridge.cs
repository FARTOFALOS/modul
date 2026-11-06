// Local named-pipe API. No sockets, credentials, live-account path or startup trades.
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.AddOns
{
 public class Sim101ApiBridge : AddOnBase
 {
  const string AccountName="Sim101",Symbol="MNQ DEC26",PipeName="MNQ-Sim101-Api-v1";
  volatile bool running;Thread worker;NamedPipeServerStream pipe;
  readonly string session=Guid.NewGuid().ToString("N");
  readonly Dictionary<string,Tuple<string,object>> responses=new Dictionary<string,Tuple<string,object>>();
  Order entry,exit;string fault="";
  HashSet<string> beforeEntry=new HashSet<string>();
  readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=131072};
  protected override void OnStateChange()
  {
   if(State==State.SetDefaults){Name="Sim101 Local API";Description="Same-Windows-user named pipe. Sim101 / MNQ only. No automatic entries.";}
   else if(State==State.Active){running=true;worker=new Thread(Run){IsBackground=true,Name="Sim101 local API"};worker.Start();}
   else if(State==State.Terminated){running=false;try{if(pipe!=null)pipe.Dispose();}catch{}}
  }
  Dictionary<string,object> Reply(bool ok,string message){return new Dictionary<string,object>{{"ok",ok},{"message",message},{"session",session},{"account",AccountName},{"instrument",Symbol},{"utc",DateTime.UtcNow.ToString("o")}};}
  void Run()
  {
   while(running)try
   {
    var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
    using(var server=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl))
    {
     pipe=server;server.WaitForConnection();if(!running)break;
     var reader=new StreamReader(server,Encoding.UTF8,false,4096,true);
     var read=System.Threading.Tasks.Task.Run(()=>ReadLimited(reader));
     if(!read.Wait(5000))continue;
     object answer;try{answer=Dispatch(json.Deserialize<Dictionary<string,object>>(read.Result));}catch(Exception e){answer=Reply(false,e.GetBaseException().Message);}
     var writer=new StreamWriter(server,new UTF8Encoding(false),4096,true){AutoFlush=true};writer.WriteLine(json.Serialize(answer));
    }
   }
   catch(Exception e){fault=e.GetBaseException().Message;if(running)Thread.Sleep(250);}
   pipe=null;
  }
  static string ReadLimited(StreamReader reader){var b=new StringBuilder();for(int i=0;i<16384;i++){int c=reader.Read();if(c<0||c=='\n')return b.ToString();b.Append((char)c);}throw new InvalidOperationException("Request exceeds 16 KiB");}
  static string Get(Dictionary<string,object> r,string k,string fallback=""){object v;return r.TryGetValue(k,out v)?Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture):fallback;}
  static bool Live(Order o){return o!=null&&o.OrderState!=OrderState.Filled&&o.OrderState!=OrderState.Cancelled&&o.OrderState!=OrderState.Rejected;}
  Account Sim(){lock(Account.All)return Account.All.SingleOrDefault(a=>a.Name==AccountName);}
  Position Position(Account a){lock(a.Positions)return a.Positions.FirstOrDefault(p=>p.Instrument.FullName==Symbol&&p.MarketPosition!=MarketPosition.Flat&&p.Quantity>0);}
  Order[] Orders(Account a){lock(a.Orders)return a.Orders.Where(o=>o.Instrument.FullName==Symbol).ToArray();}
  object Dispatch(Dictionary<string,object> r)
  {
   if(r==null)throw new ArgumentException("JSON object required");
   // Deny account/symbol before resolving ANY account or attempting a command.
   if(Get(r,"account",AccountName)!=AccountName)return Reply(false,"ACCOUNT_DENIED: only Sim101 is implemented");
   if(Get(r,"instrument",Symbol)!=Symbol)return Reply(false,"INSTRUMENT_DENIED: only MNQ DEC26 is implemented");
   var op=Get(r,"op","status");if(op!="status"&&op!="arm"&&op!="preview_entry"&&op!="place_entry"&&op!="close_test")return Reply(false,"OPERATION_DENIED");
   var a=Sim();if(a==null)return Reply(false,"Sim101 not loaded");
   if(op=="status")
   {
    var p=Position(a);var answer=Reply(true,"Ready; writes require this process session and explicit request id");
    answer["position"]=new{quantity=p==null?0:p.Quantity,side=p==null?"Flat":p.MarketPosition.ToString(),average=p==null?0:p.AveragePrice};
    answer["orders"]=Orders(a).Where(Live).Select(o=>new{id=o.OrderId,name=o.Name,state=o.OrderState.ToString(),action=o.OrderAction.ToString(),type=o.OrderType.ToString(),quantity=o.Quantity,filled=o.Filled,stop=o.StopPrice,limit=o.LimitPrice}).ToArray();
    answer["panel"]=MNQProtectionPanel.SimApiPanel(Symbol,false);answer["lastServiceFault"]=fault;return answer;
   }
   if(Get(r,"session")!=session)return Reply(false,"SESSION_REQUIRED: read status first; stale requests cannot execute after restart");
   var id=Get(r,"requestId");if(id.Length<8||id.Length>80)return Reply(false,"requestId must contain 8..80 characters");
   string fingerprint=json.Serialize(r.OrderBy(k=>k.Key).ToDictionary(k=>k.Key,k=>k.Value));Tuple<string,object> cached;
   if(responses.TryGetValue(id,out cached))return cached.Item1==fingerprint?cached.Item2:Reply(false,"REQUEST_ID_CONFLICT");
   if(responses.Count>=512)return Reply(false,"Request ledger full; no old ids are evicted");
   object result;try{result=Execute(r,op,a);}catch(Exception e){result=Reply(false,e.GetBaseException().Message);}
   responses.Add(id,Tuple.Create(fingerprint,result));return result;
  }
  object Execute(Dictionary<string,object> r,string op,Account a)
  {
   if(a.Name!=AccountName)throw new InvalidOperationException("SIM_ACCOUNT_INVARIANT");
   if(op=="arm"){if(Position(a)!=null)return Reply(false,"Arming via API requires flat Sim101; existing positions are not adopted");var armPanel=MNQProtectionPanel.SimApiPanel(Symbol,true);var d=Reply((bool)armPanel["found"],"Sim101 panel arming request");d["panel"]=armPanel;return d;}
   if(a.Connection==null||a.Connection.Status!=ConnectionStatus.Connected)return Reply(false,"Connection unavailable");
   if(op=="close_test")
   {
    if(entry==null||entry.OrderState!=OrderState.Filled)return Reply(false,"No filled API test entry owned by this server session");
    var p=Position(a);if(p==null)return Reply(true,"Already flat");
    if(Live(exit))return Reply(false,"Close already pending");
    if(p.Quantity!=1||p.MarketPosition!=(entry.OrderAction==OrderAction.Buy?MarketPosition.Long:MarketPosition.Short))return Reply(false,"Position changed outside API; automatic close denied");
    lock(a.Executions)if(a.Executions.Any(e=>e.Instrument.FullName==Symbol&&!beforeEntry.Contains(e.ExecutionId+"|"+e.OrderId)&&e.OrderId!=entry.OrderId))return Reply(false,"External execution occurred after API entry; close ownership is ambiguous");
    exit=a.CreateOrder(entry.Instrument,p.MarketPosition==MarketPosition.Long?OrderAction.Sell:OrderAction.BuyToCover,OrderType.Market,OrderEntry.Automated,TimeInForce.Day,1,0,0,"","SimAPI.Close",NinjaTrader.Core.Globals.MaxDate,null);a.Submit(new[]{exit});var d=Reply(true,"Close submitted, not yet confirmed filled");d["orderId"]=exit.OrderId;return d;
   }
   if(Get(r,"quantity","1")!="1")return Reply(false,"QUANTITY_DENIED: version 1 permits exactly one contract");
   var side=Get(r,"side").ToUpperInvariant();if(side!="BUY"&&side!="SELL")return Reply(false,"side must be BUY or SELL");
   if(Position(a)!=null||Orders(a).Any(Live))return Reply(false,"SIM_NOT_EMPTY: no existing position or working MNQ orders allowed");
   if(Live(entry)||Live(exit))return Reply(false,"API order is pending");
   var panel=MNQProtectionPanel.SimApiPanel(Symbol,false);
   if(!panel.ContainsKey("found")||!(bool)panel["found"]||!(bool)panel["armed"]||!(bool)panel["connected"])return Reply(false,"SIM_PROTECTION_NOT_ARMED: Sim101 must be selected in the active protection panel");
   if(Convert.ToDouble(panel["riskBudget"])!=300||Convert.ToDouble(panel["roundTripCommission"])!=2)return Reply(false,"PROTECTION_SETTINGS_MISMATCH: expected $300 and $2 fee");
   if(Convert.ToDouble(panel["quoteAgeMs"])>10000||Convert.ToDouble(panel["bid"])<=0||Convert.ToDouble(panel["ask"])<=0)return Reply(false,"STALE_QUOTES");
   var answer=Reply(true,op=="preview_entry"?"Validated preview only; no order sent":"Entry submitted; inspect status for fill and protective stop");answer["side"]=side;answer["quantity"]=1;answer["panel"]=panel;
   if(op=="preview_entry")return answer;
   if(Get(r,"confirm")!="SIM101_ONLY")return Reply(false,"Explicit confirm=SIM101_ONLY required");
   var instrument=Instrument.GetInstrument(Symbol);
   lock(a.Executions)beforeEntry=new HashSet<string>(a.Executions.Where(e=>e.Instrument.FullName==Symbol).Select(e=>e.ExecutionId+"|"+e.OrderId));
   entry=a.CreateOrder(instrument,side=="BUY"?OrderAction.Buy:OrderAction.SellShort,OrderType.Market,OrderEntry.Automated,TimeInForce.Day,1,0,0,"","SimAPI.Entry",NinjaTrader.Core.Globals.MaxDate,null);exit=null;a.Submit(new[]{entry});answer["orderId"]=entry.OrderId;return answer;
  }
 }
}
