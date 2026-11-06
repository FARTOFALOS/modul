// Local adaptation of the user's saved Pine scripts. Indicator only: no execution API.
// DR/IDR portions derived from DR/IDR V1.5 (TheMas7er and bmistiaen), MPL-2.0.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using System.Web.Script.Serialization;
using TvPort;
using Bar = TvPort.Bar;

namespace NinjaTrader.NinjaScript.Indicators
{
 public class TVPortSuite : Indicator
 {
  Dictionary<string,object> cfg;readonly object gate=new object();List<Mark> frame=new List<Mark>();Brush upBrush,downBrush;
  Dictionary<int,Aggregator> engines;RizMachine day,week,month;Cycle90Engine cycles;
  readonly SortedDictionary<DateTime,Bar> pending=new SortedDictionary<DateTime,Bar>();
  readonly Dictionary<DateTime,Bar> es=new Dictionary<DateTime,Bar>(),ym=new Dictionary<DateTime,Bar>();
  DateTime esWater=DateTime.MinValue,ymWater=DateTime.MinValue,lastSource=DateTime.MinValue;
  DateTime nowNY;TimeZoneInfo ny;List<Bar> drBars=new List<Bar>();Aggregator five;DateTime lastFive=DateTime.MinValue;Bar sourceLast;
  static readonly int[] mainTF={1,2,3,4,5,10,15,30,60,120,240,420};
  static readonly int[] blueTF={6,7,8,9,11,12,13,14,16,17,18,19,20,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46};
  const string R="RIZ v9.7 MTF - Raid Imbalance Zones",B="RIZ BLUE v1.0 — синие RIZ 2X+ (мульти-ТФ до 1 дня)",C="90min Cycle Extremes + SMT Diagonal v25.3 [Sync + Target + 369]",D="DR/IDR V1.5";
  [NinjaScriptProperty,Display(Name="Source NQ contract",GroupName="Sources",Order=0)]public string SourceContract{get;set;}
  [NinjaScriptProperty,Display(Name="ES contract",GroupName="Sources",Order=1)]public string ESContract{get;set;}
  [NinjaScriptProperty,Display(Name="YM contract",GroupName="Sources",Order=2)]public string YMContract{get;set;}
  [NinjaScriptProperty,Display(Name="RIZ 9.7",GroupName="Layers",Order=0)]public bool ShowRiz{get;set;}
  [NinjaScriptProperty,Display(Name="RIZ BLUE",GroupName="Layers",Order=1)]public bool ShowBlue{get;set;}
  [NinjaScriptProperty,Display(Name="90 minute cycles and SMT",GroupName="Layers",Order=2)]public bool ShowCycles{get;set;}
  [NinjaScriptProperty,Display(Name="DR / IDR",GroupName="Layers",Order=3)]public bool ShowDR{get;set;}
  [NinjaScriptProperty,Display(Name="TradingView appearance",GroupName="Appearance",Order=0)]public bool MatchCandles{get;set;}
  [NinjaScriptProperty,Display(Name="Settings JSON",GroupName="Sources",Order=3)]public string SettingsFile{get;set;}
  string Status="Loading NQ / ES / YM";
  protected override void OnStateChange()
  {
   if(State==State.SetDefaults)
   {
    Name="TV Port Suite";Description="Local port: RIZ 9.7, RIZ BLUE, 90min v25.3, DR/IDR 1.5. Verification pending.";
    IsOverlay=true;IsAutoScale=false;Calculate=Calculate.OnEachTick;DisplayInDataBox=false;PaintPriceMarkers=false;IsSuspendedWhileInactive=false;
    SourceContract="NQ 12-26";ESContract="ES 12-26";YMContract="YM 12-26";
    ShowRiz=ShowBlue=ShowCycles=ShowDR=MatchCandles=true;
    SettingsFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"NinjaTrader 8","bin","Custom","TVPortSettings.json");
   }
   else if(State==State.Configure)
   {
    AddDataSeries(SourceContract,BarsPeriodType.Minute,1);
    AddDataSeries(ESContract,BarsPeriodType.Minute,1);
    AddDataSeries(YMContract,BarsPeriodType.Minute,1);
    AddDataSeries(SourceContract,BarsPeriodType.Day,1);
    AddDataSeries(SourceContract,BarsPeriodType.Week,1);
    AddDataSeries(SourceContract,BarsPeriodType.Month,1);
   }
   else if(State==State.DataLoaded)
   {
    cfg=File.Exists(SettingsFile)?new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(SettingsFile)):new Dictionary<string,object>();
    upBrush=(Brush)new BrushConverter().ConvertFromString("#388E3C");upBrush.Freeze();downBrush=(Brush)new BrushConverter().ConvertFromString("#536FBD");downBrush.Freeze();
    ny=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");nowNY=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,ny);
    engines=new Dictionary<int,Aggregator>();
    foreach(int m in mainTF.Concat(blueTF).Concat(Enumerable.Range(47,13)).Distinct())engines[m]=new Aggregator(m,TF(m));
    day=new RizMachine();week=new RizMachine();month=new RizMachine();five=new Aggregator(5,"5");
    cycles=new Cycle90Engine{Both=S(C,"smtMode","AT LEAST ONE")=="BOTH",ShowGem=Flag(C,"gemShow",true),ShowTarget=Flag(C,"showTarget",true),ShowTargetLine=Flag(C,"showTargetLine",false),PivotLeft=Num(C,"gemPivotLeft",1),PivotRight=Num(C,"gemPivotRight",1)};
   }
   else if(State==State.Historical&&MatchCandles&&ChartControl!=null)
   {
    ChartControl.Dispatcher.InvokeAsync((Action)(()=>{try{
     ChartControl.Properties.ChartBackground=BrushFrom("#B8B8B8");ChartControl.Properties.ChartText=BrushFrom("#131722");
     ChartControl.Properties.GridLineHPen.Brush=BrushFrom("#302E2E2E");ChartControl.Properties.GridLineVPen.Brush=BrushFrom("#202E2E2E");
     var candle=ChartBars.Properties.ChartStyle as NinjaTrader.NinjaScript.ChartStyles.CandleStyle;
     if(candle!=null){candle.WickMatchesBody=false;candle.Stroke.Brush=BrushFrom("#131722");candle.Stroke2.Brush=BrushFrom("#131722");candle.UpBrush=upBrush;candle.DownBrush=downBrush;}
    }catch(Exception ex){Print("TV Port theme: "+ex.Message);}}));
   }
  }
  Brush BrushFrom(string x){var b=(Brush)new BrushConverter().ConvertFromString(x);b.Freeze();return b;}
  string S(string group,string name,string fallback){object x,v;if(cfg==null||!cfg.TryGetValue(group,out x))return fallback;var values=x as Dictionary<string,object>;return values==null||!values.TryGetValue(name,out v)?fallback:Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture);}
  bool Flag(string g,string n,bool d){bool v;return bool.TryParse(S(g,n,d.ToString()),out v)?v:d;}
  int Num(string g,string n,int d){int v;return int.TryParse(S(g,n,d.ToString()),out v)?v:d;}
  // TradingView stores input.color as little-endian RGBA (ABGR integer), not ARGB.
  uint Color(string g,string n,uint fallback)
  {
   string x=S(g,n,"");uint raw;
   if(uint.TryParse(x,out raw))return (raw&0xff000000)|((raw&255)<<16)|(raw&0xff00)|((raw>>16)&255);
   if(x.StartsWith("rgba("))
   {
    string[] p=x.Substring(5).TrimEnd(')').Split(',');double a;
    if(p.Length==4&&double.TryParse(p[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out a))
     return ((uint)(a*255)<<24)|((uint)int.Parse(p[0])<<16)|((uint)int.Parse(p[1])<<8)|(uint)int.Parse(p[2]);
   }
   return fallback;
  }
  DateTime ToNY(DateTime t){return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t,DateTimeKind.Unspecified),NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo,ny);}
  DateTime ToChart(DateTime t){return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t,DateTimeKind.Unspecified),ny,NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo);}
  Bar Read(int series,int ago)
  {return new Bar(ToNY(Times[series][ago]).AddMinutes(-1),Opens[series][ago],Highs[series][ago],Lows[series][ago],Closes[series][ago]);}
  protected override void OnBarUpdate()
  {
   if(engines==null)return;
   if(BarsInProgress==0)
   {if(MatchCandles){BarBrushes[0]=Close[0]>=Open[0]?upBrush:downBrush;CandleOutlineBrushes[0]=BarBrushes[0];}return;}
   int ago=0;
   if(State==State.Realtime)
   {
    if(BarsInProgress==1&&!IsFirstTickOfBar){lock(gate)BuildFrame(Read(1,0));return;}
    if(!IsFirstTickOfBar||CurrentBars[BarsInProgress]<1)return;ago=1;
   }
   int bip=BarsInProgress;
   if(CurrentBars[bip]<ago)return;
   if(bip>=4)
   {
    DateTime t=ToNY(Times[bip][ago]);
    var bar=new Bar(t,Opens[bip][ago],Highs[bip][ago],Lows[bip][ago],Closes[bip][ago]);
    lock(gate){(bip==4?day:bip==5?week:month).Push(bar,true);}return;
   }
   Bar x=Read(bip,ago);
   lock(gate)
   {
    if(bip==2){es[x.Time]=x;esWater=x.Time;}
    else if(bip==3){ym[x.Time]=x;ymWater=x.Time;}
    else
    {
     if(x.Time<=lastSource)return;lastSource=x.Time;sourceLast=x;
     foreach(var e in engines.Values)e.Push(x,nowNY,Num(R,"hist_d",14),Num(R,"hist_h",60));
     five.Push(x,nowNY,10000,10000);
     if(five.Machine.Last!=null&&five.Machine.Last.Time>lastFive){drBars.Add(five.Machine.Last.Copy());lastFive=five.Machine.Last.Time;}
     pending[x.Time]=x;
    }
    DateTime water=esWater<ymWater?esWater:ymWater;
    while(pending.Count>0&&pending.First().Key<=water)
    {
     var p=pending.First();Bar a,b;es.TryGetValue(p.Key,out a);ym.TryGetValue(p.Key,out b);
     cycles.Push(p.Value,a,b);pending.Remove(p.Key);
    }
    if(es.Count>5000){DateTime cut=x.Time.AddDays(-2);foreach(var k in es.Keys.Where(k=>k<cut).ToList())es.Remove(k);foreach(var k in ym.Keys.Where(k=>k<cut).ToList())ym.Remove(k);}
    if(sourceLast!=null&&(bip==1&&State==State.Realtime||bip==3&&State==State.Historical&&CurrentBars[3]>=BarsArray[3].Count-2||bip==1&&CurrentBars[1]>=BarsArray[1].Count-2))BuildFrame(sourceLast);
   }
  }
  int ChartMinutes{get{return BarsPeriod.BarsPeriodType==BarsPeriodType.Minute?BarsPeriod.Value:1;}}
  string TF(int m){return m==43200?"M":m==10080?"W":m==1440?"D":m==420?"7H":m==240?"4H":m==120?"2H":m==60?"1H":m.ToString();}
  int Rank(int m){int[] order={1,2,3,4,5,10,15,30,60,120,240,420,1440,10080,43200};return Array.IndexOf(order,m)+1;}
  void BuildFrame(Bar latest)
  {
   var result=new List<Mark>();
   if(ShowCycles)result.AddRange(cycles.Marks(latest.Time,ChartMinutes==1));
   if(ShowDR)BuildDR(result,latest);
   if(ShowRiz||ShowBlue)BuildRiz(result,latest);
   foreach(var mark in result)
   {
    if(mark.Color==0xff00ffff){mark.Color=Color(C,"colorCyan",mark.Color);mark.Opacity=100;}
    else if(mark.Color==0xffffff00){mark.Color=Color(C,"colorYellow",mark.Color);mark.Opacity=100;}
    else if(mark.Color==0xff0000ff){mark.Color=Color(C,mark.Opacity==60?"colorBridgeMarker":"colorBridge",mark.Color);mark.Opacity=100;}
    else if(mark.Color==0xff800080){mark.Color=Color(C,mark.Fill?"colorSMT":"smtLineColor",mark.Color);mark.Opacity=100;if(!mark.Fill)mark.Width=Num(C,"smtLineWidth",1);}
   }
   frame=result;Status=esWater>=latest.Time.AddMinutes(-1)&&ymWater>=latest.Time.AddMinutes(-1)?"":"Waiting for matching ES / YM minute bars";
  }
  sealed class Magnet{public double Price;public DateTime Birth;public string Label;public int Rank;public bool Candidate;}
  void BuildRiz(List<Mark> marks,Bar last)
  {
   var magnets=new List<Magnet>();var lower=new List<Magnet>();var fills=new List<Mark>();
   int native=ChartMinutes;double tick=TickSize;
   var contexts=new List<Tuple<int,RizMachine,Bar,bool>>();
   foreach(var p in engines.OrderByDescending(p=>p.Key))
   {
    Bar dev=State==State.Realtime&&last.Time>lastSource?p.Value.Preview(last):p.Value.Developing;
    // Fully closed buckets project the final closed state, not a synthetic next candle.
    contexts.Add(Tuple.Create(p.Key,p.Value.Machine,dev??p.Value.Machine.Last,dev!=null));
   }
   contexts.Insert(0,Tuple.Create(43200,month,month.Last,false));contexts.Insert(1,Tuple.Create(10080,week,week.Last,false));contexts.Insert(2,Tuple.Create(1440,day,day.Last,false));
   foreach(var ctx in contexts)
   {
    int tf=ctx.Item1;RizMachine machine=ctx.Item2;Bar dev=ctx.Item3;if(dev==null)continue;
    bool fixedTF=mainTF.Contains(tf)||tf>=1440||tf==native;
    bool nativeTF=tf==native;bool blueOnly=blueTF.Contains(tf)&&!nativeTF;bool enabledBlue=blueOnly?ShowBlue&&Flag(B,"show_2x",true):ShowRiz&&Flag(R,"show_2x",true);
    foreach(var z in machine.Zones)
    {
     ZoneView v=machine.View(z,dev,ctx.Item4,Flag(R,"prev_on",true));
     if(enabledBlue&&v.Blue)
     {fills.Add(new Mark{Left=z.Birth,Right=last.Time,Top=z.Top,Bottom=z.Bottom,Fill=true,Extend=true,Color=Color(blueOnly?B:R,"c2x_fill",0xff2962ff),BorderColor=Color(blueOnly?B:R,"c2x_brdc",0xff2962ff),BorderWidth=Num(blueOnly?B:R,"brd_w",1),Opacity=100-Num(blueOnly?B:R,"c2x_tr",75),Text=TF(tf)});continue;}
     if(!ShowRiz||!fixedTF||!Flag(R,"mtf_on",true)&&!nativeTF)continue;
     if(nativeTF&&v.Visible&&(z.Bull?Flag(R,"show_bull",true):Flag(R,"show_bear",true)))
     {
      string prefix=v.Breaker?"t3":z.Bull?"bull":"bear";
      if(v.Breaker&&!Flag(R,"show_t3",true))continue;
      fills.Add(new Mark{Left=z.Birth,Right=last.Time,Top=z.Top,Bottom=z.Bottom,Fill=true,Extend=true,Color=Color(R,prefix+"_fill",v.Breaker?0xff787b86:0xffffffff),BorderColor=Color(R,prefix+"_brdc",v.Breaker?0xff787b86:0xffffffff),BorderWidth=Num(R,"brd_w",1),Opacity=100-Num(R,prefix+"_tr",v.Breaker?70:65)});
     }
     else if(!nativeTF)
     {
      double p=v.Magnet;bool candidate=false;
      if(z.Tier==0&&Flag(R,"cand_on",true)&&native==1&&tf>native&&ctx.Item4&&z.North&&z.South)
      {
       if(dev.Bottom<z.Bottom&&dev.Top>z.Top)p=dev.Close>z.Top?z.Top:dev.Close<z.Bottom?z.Bottom:double.NaN;
       else if(dev.Open<z.Top&&dev.Close>z.Top&&dev.Low<=z.Bottom)p=z.Top;
       else if(dev.Open>z.Bottom&&dev.Close<z.Bottom&&dev.High>=z.Top)p=z.Bottom;
       candidate=!double.IsNaN(p);
      }
      if(!double.IsNaN(p))
      {var m=new Magnet{Price=p,Birth=z.Birth,Label=TF(tf),Rank=Rank(tf),Candidate=candidate};if(tf>native)magnets.Add(m);else lower.Add(m);}
     }
    }
   }
   // Original oldest-to-newest lists are preserved within each context. Group within 2 ticks;
   // 2/3/4-minute labels require at least three other labels. Blue coverage suppresses magnets.
   foreach(var f in fills)
   {
    if(f.Text=="")
    {
     double mag=last.Close>f.Top?f.Top:last.Close<f.Bottom?f.Bottom:double.NaN;
     var labels=lower.Where(x=>Math.Abs(x.Price-mag)<=tick*.5).Select(x=>x.Label).Distinct().ToList();
     int others=labels.Count(x=>x!="2"&&x!="3"&&x!="4");f.Text=string.Join("/",labels.Where(x=>!(x=="2"||x=="3"||x=="4")||others>=3));
    }
    marks.Add(f);
   }
   while(magnets.Count>0)
   {
    var a=magnets[0];var group=magnets.Where(x=>Math.Abs(x.Price-a.Price)<=tick*Num(R,"tol_tk",2)).ToList();
    foreach(var x in group)magnets.Remove(x);
    if(fills.Any(x=>x.Color==Color(R,"c2x_fill",0xff2962ff)&&(Math.Abs(x.Top-a.Price)<=tick*Num(R,"tol_tk",2)||Math.Abs(x.Bottom-a.Price)<=tick*Num(R,"tol_tk",2))))continue;
    var names=group.Select(x=>x.Label).Distinct().ToList();int others=names.Count(x=>x!="2"&&x!="3"&&x!="4");names=names.Where(x=>!(x=="2"||x=="3"||x=="4")||others>=3).ToList();if(names.Count==0)continue;
    int rank=group.Where(x=>names.Contains(x.Label)).Max(x=>x.Rank);int highest=group.Max(x=>x.Rank);
    DateTime left=group.Min(x=>x.Birth);if(native<60&&left<nowNY.AddDays(-Num(R,"hist_d",14)))left=nowNY.AddDays(-Num(R,"hist_d",14));
    marks.Add(new Mark{Left=left,Right=last.Time,Top=a.Price,Bottom=a.Price,Color=Color(R,"mtf_col",0xffd000ff),Extend=true,Dashed=group.Where(x=>x.Rank==highest).All(x=>x.Candidate),Width=Math.Max(1,(int)Math.Round(1+(Num(R,"mtf_wb",5)-1)*(rank-1)/14.0)),Text=string.Join("/",names)});
   }
  }
  sealed class RangeState
  {public DateTime Start,RangeEnd,ExtendEnd;public double High,Low,BodyHigh,BodyLow,Open,Close;public bool Regular;}
  void BuildDR(List<Mark> marks,Bar latest)
  {
   var bars=drBars.ToList();if(five.Developing!=null)bars.Add(five.Developing);
   var ranges=new List<RangeState>();RangeState current=null;
   foreach(var x in bars)
   {
    int m=x.Time.Hour*60+x.Time.Minute;string session=null,ext=null;bool regular=false;
    foreach(var key in new[]{"regular","after","overnight"})
    {
     if(!Flag(D,key=="regular"?"showRegular":key=="after"?"showAfter":"showOvernight",true))continue;
     string t=S(D,key+"Time",key=="regular"?"0930-1030":key=="after"?"1930-2030":"0300-0400");
     int from=HM(t.Substring(0,4)),to=HM(t.Substring(5,4));if(m>=from&&m<to){session=t;ext=S(D,key=="regular"?"regularExtend":key=="after"?"afterExtend":"overnightExtend",key=="regular"?"1030-1600":key=="after"?"2030-0200":"0400-0830");regular=key=="regular";break;}
    }
    if(session==null){current=null;continue;}
    DateTime start=x.Time.Date.AddMinutes(HM(session.Substring(0,4)));
    if(current==null||current.Start!=start)
    {
     DateTime rangeEnd=x.Time.Date.AddMinutes(HM(session.Substring(5,4)));DateTime end=x.Time.Date.AddMinutes(HM(ext.Substring(5,4)));if(end<rangeEnd)end=end.AddDays(1);
     current=new RangeState{Start=start,RangeEnd=rangeEnd,ExtendEnd=end,High=x.High,Low=x.Low,BodyHigh=x.Top,BodyLow=x.Bottom,Open=x.Open,Regular=regular};ranges.Add(current);
    }
    current.High=Math.Max(current.High,x.High);current.Low=Math.Min(current.Low,x.Low);current.BodyHigh=Math.Max(current.BodyHigh,x.Top);current.BodyLow=Math.Min(current.BodyLow,x.Bottom);current.Close=x.Close;
   }
   DateTime lastRdr=ranges.Where(x=>x.Regular).Select(x=>x.Start.Date.AddHours(16)).DefaultIfEmpty(latest.Time).Max();
   foreach(var r in ranges)
   {
    DateTime end=latest.Time<r.ExtendEnd?latest.Time:r.ExtendEnd;if(Flag(D,"extendToEndOfRDR",true)&&r.Start>=nowNY.AddDays(-2))end=lastRdr;
    if(end<r.Start)end=r.Start;
    if(Flag(D,"drlines",true)){DRLine(marks,r.Start,end,r.High,"dr");DRLine(marks,r.Start,end,r.Low,"dr");if(Flag(D,"middledrline",true))DRLine(marks,r.Start,end,(r.High+r.Low)/2,"middle");}
    if(Flag(D,"idrlines",true)){DRLine(marks,r.Start,end,r.BodyHigh,"idr");DRLine(marks,r.Start,end,r.BodyLow,"idr");if(Flag(D,"middleidrline",false))DRLine(marks,r.Start,end,(r.BodyHigh+r.BodyLow)/2,"middle");}
    if(Flag(D,"openline",true))DRLine(marks,r.Start,end,r.Open,"op");
    if(Flag(D,"showBackground",true))
    {
     bool body=S(D,"boxType","IDR")=="IDR",up=r.Close>r.Open;
     uint color=Flag(D,"useBoxColors",true)?Color(D,up?"upBoxColor":"downBoxColor",up?0x264caf50u:0x26ff5252u):Color(D,"backgroundColor",0x40787b86);
     marks.Add(new Mark{Left=r.Start,Right=r.RangeEnd<latest.Time?r.RangeEnd:latest.Time,Top=body?r.BodyHigh:r.High,Bottom=body?r.BodyLow:r.Low,Fill=true,Color=color,BorderWidth=Num(D,"boxBorderSize",1),BorderColor=Color(D,up?"upBorderColor":"downBorderColor",0x30ff5252)});
    }
   }
   var regs=ranges.Where(x=>x.Regular&&latest.Time>=x.RangeEnd).ToList();if(!Flag(D,"showStdLinesAll",false)&&regs.Count>1)regs=regs.Skip(regs.Count-1).ToList();
   if(Flag(D,"showStdLines",true))foreach(var r in regs)
   {
    double step=(r.BodyHigh-r.BodyLow)/2;if(step<=0)continue;
    DateTime end=r.Start.Date.AddDays(r.Start.DayOfWeek==DayOfWeek.Friday?3:1).AddHours(8).AddMinutes(30);
    for(int sign=-1;sign<=1;sign+=2)for(int i=1;i<=Num(D,sign==1?"stdLineStaticExtraPos":"stdLineStaticExtraNeg",6);i++)
    {
     double p=(sign==1?r.BodyHigh:r.BodyLow)+sign*i*step;
     marks.Add(new Mark{Left=r.RangeEnd,Right=end,Top=p,Bottom=p,Color=Color(D,"stdLineColor",0xffffffff),Width=Num(D,"linesWidth",2),Dashed=S(D,"stdLineStyle","Solid")!="Solid",Text=Flag(D,"showStdLineLabels",true)?(sign*i*.5).ToString(System.Globalization.CultureInfo.InvariantCulture):""});
    }
   }
  }
  int HM(string x){return int.Parse(x.Substring(0,2))*60+int.Parse(x.Substring(2,2));}
  void DRLine(List<Mark> result,DateTime left,DateTime right,double p,string type)
  {string style=S(D,type=="middle"?"middleLineStyle":type+"LineStyle","Solid");result.Add(new Mark{Left=left,Right=right,Top=p,Bottom=p,Color=Color(D,type=="middle"?"middleLineColor":type+"LineColor",0xffffffff),Width=Num(D,"linesWidth",2),Dashed=style=="Dashed",Dotted=style=="Dotted"});}
  protected override void OnRender(ChartControl control,ChartScale scale)
  {
   if(RenderTarget==null||ChartBars==null||ny==null)return;
   List<Mark> items;lock(gate)items=frame.ToList();
   float left=ChartPanel.X,right=ChartPanel.X+ChartPanel.W,top=ChartPanel.Y,bottom=ChartPanel.Y+ChartPanel.H;
   using(var font=new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory,"Arial",12))
   {
    foreach(var m in items)
    {
     if(double.IsNaN(m.Top)||double.IsNaN(m.Bottom))continue;
     float x1=control.GetXByTime(ToChart(m.Left)),x2=m.Extend?right-12:control.GetXByTime(ToChart(m.Right));
     float y1=scale.GetYByValue(m.Top),y2=scale.GetYByValue(m.Bottom);if(x2<left||x1>right||Math.Max(y1,y2)<top||Math.Min(y1,y2)>bottom)continue;
     x1=Math.Max(left,x1);x2=Math.Min(right,x2);
     uint col=m.Color;float alpha=((col>>24)&255)/255f*m.Opacity/100f;
     using(var brush=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(((col>>16)&255)/255f,((col>>8)&255)/255f,(col&255)/255f,alpha)))
     {
      if(m.Fill)
      {
       var rect=new SharpDX.RectangleF(x1,Math.Min(y1,y2),Math.Max(1,x2-x1),Math.Max(1,Math.Abs(y2-y1)));RenderTarget.FillRectangle(rect,brush);
       if(m.BorderWidth>0)using(var border=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(((m.BorderColor>>16)&255)/255f,((m.BorderColor>>8)&255)/255f,(m.BorderColor&255)/255f,((m.BorderColor>>24)&255)/255f)))RenderTarget.DrawRectangle(rect,border,m.BorderWidth);
      }
      else if(m.Left!=m.Right||m.Text=="")
      {
       using(var stroke=new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory,new SharpDX.Direct2D1.StrokeStyleProperties{DashStyle=m.Dotted?SharpDX.Direct2D1.DashStyle.Dot:m.Dashed?SharpDX.Direct2D1.DashStyle.Dash:SharpDX.Direct2D1.DashStyle.Solid}))
        RenderTarget.DrawLine(new SharpDX.Vector2(x1,y1),new SharpDX.Vector2(x2,y2),brush,m.Width,stroke);
      }
      if(m.Text!="")
      {
       float tx=m.Extend?right-95:m.Left==m.Right?x1-5:(x1+x2)/2;
       float ty=m.Fill?(y1+y2)/2-7:m.Left==m.Right?y1-8:(y1+y2)/2-14;
       RenderTarget.DrawText(m.Text,font,new SharpDX.RectangleF(tx,ty,200,28),brush);
      }
     }
    }
    if(Status!="")using(var b=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(.25f,.25f,.25f,.9f)))RenderTarget.DrawText(Status,font,new SharpDX.RectangleF(left+12,top+22,750,24),b);
   }
  }
 }
}
