// Local port of the user's Pine snapshots. No order/account API is used.
using System;
using System.Collections.Generic;
using System.Linq;
namespace TvPort
{
 public sealed class Bar
 {
  public DateTime Time; public double Open,High,Low,Close;
  public Bar(DateTime t,double o,double h,double l,double c){Time=t;Open=o;High=h;Low=l;Close=c;}
  public Bar Copy(){return new Bar(Time,Open,High,Low,Close);}
  public double Bottom {get{return Math.Min(Open,Close);}}
  public double Top {get{return Math.Max(Open,Close);}}
 }
 public sealed class Zone
 {
  public double Top,Bottom; public DateTime Birth; public bool Bull,North=true,South=true,Active,SouthBreak;
  public int Tier,LastSpan=-1,Spans;
  public Zone Copy(){return (Zone)MemberwiseClone();}
 }
 public sealed class ZoneView
 {
  public Zone Zone; public bool Visible,Blue,Candidate,Breaker; public double Magnet=double.NaN;
 }
 public sealed class RizMachine
 {
  public readonly List<Zone> Zones=new List<Zone>();
  public int Index=-1,Cap=40; public Bar Previous,BeforePrevious,Last;
  public void Push(Bar b,bool warm)
  {
   Index++;
   if(warm)
   {
    for(int i=Zones.Count-1;i>=0;i--)
    {
     Zone z=Zones[i];
     if(z.Tier==3){if(z.SouthBreak?b.High>=z.Bottom:b.Low<=z.Top)Zones.RemoveAt(i);continue;}
     bool span=b.Bottom<z.Bottom&&b.Top>z.Top;
     if(span&&(!z.Active||Index>=z.LastSpan+2))
     {
      if(!z.Active){z.Active=true;z.Tier=z.North&&z.South?1:2;z.Spans=1;}else z.Spans++;
      z.LastSpan=Index;
     }
     else
     {
      bool sb=!span&&z.South&&!z.North&&b.Open>z.Bottom&&b.Close<z.Bottom;
      bool nb=!span&&z.North&&!z.South&&b.Open<z.Top&&b.Close>z.Top;
      bool sb2=!span&&z.North&&z.South&&b.Open>z.Bottom&&b.Close<z.Bottom&&b.High>=z.Top&&b.Close<=z.Top;
      bool nb2=!span&&z.North&&z.South&&b.Open<z.Top&&b.Close>z.Top&&b.Low<=z.Bottom&&b.Close>=z.Bottom;
      if(sb||nb||sb2||nb2){z.North=z.South=false;z.Tier=3;z.SouthBreak=sb||sb2;z.Active=true;z.LastSpan=Index;}
      else
      {
       if(b.Low<=z.Top&&b.High>=z.Top)z.North=false;
       if(b.Low<=z.Bottom&&b.High>=z.Bottom)z.South=false;
       if(!z.North&&!z.South)Zones.RemoveAt(i);
      }
     }
    }
    Bar a=BeforePrevious,c=Previous;
    if(a!=null&&c!=null)
    {
     if(b.Low>a.High&&c.Top>a.High&&c.Bottom<b.Low)
      Add(c.Bottom>a.Top?a.Top:a.High,b.Bottom>c.Top?b.Bottom:b.Low,a.Time,true);
     if(b.High<a.Low&&c.Bottom<a.Low&&c.Top>b.High)
      Add(b.Top<c.Bottom?b.Top:b.High,c.Top<a.Bottom?a.Bottom:a.Low,a.Time,false);
    }
    while(Zones.Count>Cap)Zones.RemoveAt(0);
   }
   BeforePrevious=Previous;Previous=b.Copy();Last=b.Copy();
  }
  void Add(double bottom,double top,DateTime birth,bool bull)
  {if(top>bottom)Zones.Add(new Zone{Bottom=bottom,Top=top,Birth=birth,Bull=bull});}
  public ZoneView View(Zone z,Bar b,bool developing,bool preview)
  {
   bool span=b.Bottom<z.Bottom&&b.Top>z.Top;
   bool spanOK=span&&Index+(developing?1:0)>=z.LastSpan+2;
   bool n=z.North&&!(developing&&!spanOK&&b.Low<=z.Top&&b.High>=z.Top);
   bool s=z.South&&!(developing&&!spanOK&&b.Low<=z.Bottom&&b.High>=z.Bottom);
   bool sb=developing&&z.Tier!=3&&!span&&z.South&&!z.North&&b.Open>z.Bottom&&b.Close<z.Bottom;
   bool nb=developing&&z.Tier!=3&&!span&&z.North&&!z.South&&b.Open<z.Top&&b.Close>z.Top;
   bool sb2=developing&&z.Tier!=3&&!span&&z.North&&z.South&&b.Open>z.Bottom&&b.Close<z.Bottom&&b.High>=z.Top&&b.Close<=z.Top;
   bool nb2=developing&&z.Tier!=3&&!span&&z.North&&z.South&&b.Open<z.Top&&b.Close>z.Top&&b.Low<=z.Bottom&&b.Close>=z.Bottom;
   bool br=preview&&(sb||nb||sb2||nb2);
   bool dead=developing&&(z.SouthBreak?b.High>=z.Bottom:b.Low<=z.Top);
   var v=new ZoneView{Zone=z,Breaker=z.Tier==3||br};
   if(z.Tier>=1)
    v.Visible=z.Tier==3?!dead&&(z.SouthBreak?b.Close<z.Bottom:b.Close>z.Top):br||(n&&s)||(n&&b.Close>z.Top)||(s&&b.Close<z.Bottom);
   else if(preview&&developing)
   {v.Visible=z.North&&z.South&&span||br;v.Candidate=v.Visible;}
   bool strict=z.North&&z.South&&n&&s;
   v.Blue=(z.Tier==1||z.Tier==2)&&strict&&(z.Spans>=2||preview&&developing&&spanOK)&&!br;
   if(v.Visible)
   {
    if(z.Tier==3)v.Magnet=z.SouthBreak?z.Bottom:z.Top;
    else if(br)v.Magnet=sb||sb2?z.Bottom:z.Top;
    else if(n&&s)v.Magnet=b.Close>z.Top?z.Top:b.Close<z.Bottom?z.Bottom:double.NaN;
    else v.Magnet=n?z.Top:z.Bottom;
   }
   return v;
  }
 }
 // Pine futures intraday buckets start at the electronic session open, 18:00 New York.
 // Missing bars are not synthesized. The caller supplies NY wall-clock bar OPEN times.
 public sealed class Aggregator
 {
  public readonly int Minutes; public readonly string Label; public readonly RizMachine Machine=new RizMachine();
  public Bar Developing; public DateTime Key=DateTime.MinValue; public DateTime LastInput=DateTime.MinValue; public bool Complete;
  public Aggregator(int minutes,string label){Minutes=minutes;Label=label;}
  public static DateTime Bucket(DateTime t,int minutes)
  {
   DateTime session=t.Date.AddHours(18);if(t<session)session=session.AddDays(-1);
   if(minutes==1440)return session;
   if(minutes==10080){int days=(int)session.DayOfWeek;return session.Date.AddDays(-days).AddHours(18);}
   if(minutes==43200){DateTime trade=session.Date.AddDays(1);return new DateTime(trade.Year,trade.Month,1).AddDays(-1).AddHours(18);}
   return session.AddMinutes(Math.Floor((t-session).TotalMinutes/minutes)*minutes);
  }
  public void Push(Bar b,DateTime now,int minuteDays,int hourDays)
  {
   if(b.Time<=LastInput)return;LastInput=b.Time;
   DateTime key=Bucket(b.Time,Minutes);
   if(Developing!=null&&key!=Key&&Complete)Machine.Push(Developing,Minutes>=1440||Developing.Time>=now.AddDays(-(Minutes>=60?hourDays:minuteDays)));
   if(Developing==null||key!=Key){Key=key;Developing=new Bar(key,b.Open,b.High,b.Low,b.Close);Complete=b.Time==key;}
   else{Developing.High=Math.Max(Developing.High,b.High);Developing.Low=Math.Min(Developing.Low,b.Low);Developing.Close=b.Close;}
   if(Minutes<1440&&(b.Time.AddMinutes(1)>=key.AddMinutes(Minutes)||b.Time.Hour==16&&b.Time.Minute==59))
   {if(Complete)Machine.Push(Developing,Developing.Time>=now.AddDays(-(Minutes>=60?hourDays:minuteDays)));Developing=null;}
  }
  public Bar Preview(Bar b)
  {
   DateTime key=Bucket(b.Time,Minutes);
   if(Developing==null||key!=Key)return new Bar(key,b.Open,b.High,b.Low,b.Close);
   var v=Developing.Copy();v.High=Math.Max(v.High,b.High);v.Low=Math.Min(v.Low,b.Low);v.Close=b.Close;return v;
  }
 }
 public sealed class Mark
 {
  public DateTime Left,Right;public double Top,Bottom;public string Text="";
  public uint Color=0xffd000ff,BorderColor;public int Opacity=100,Width=1,BorderWidth;public bool Fill,Extend,Dashed,Dotted;
 }
 public sealed class CycleRange
 {
  public DateTime Start,End,HighTime,LowTime;public Bar HighBar,LowBar;
  public double AHigh=double.NaN,ALow=double.NaN,BHigh=double.NaN,BLow=double.NaN,AHighLow,ALowHigh,BHighLow,BLowHigh;
  public void Add(Bar m,Bar a,Bar b)
  {
   if(HighBar==null){Start=m.Time;HighBar=m;LowBar=m;HighTime=LowTime=m.Time;}
   End=m.Time;
   if(m.High>HighBar.High||m.High==HighBar.High&&m.High-m.Low>HighBar.High-HighBar.Low){HighBar=m;HighTime=m.Time;}
   if(m.Low<LowBar.Low||m.Low==LowBar.Low&&m.High-m.Low>LowBar.High-LowBar.Low){LowBar=m;LowTime=m.Time;}
   if(a!=null){if(double.IsNaN(AHigh)||a.High>AHigh){AHigh=a.High;AHighLow=a.Low;}if(double.IsNaN(ALow)||a.Low<ALow){ALow=a.Low;ALowHigh=a.High;}}
   if(b!=null){if(double.IsNaN(BHigh)||b.High>BHigh){BHigh=b.High;BHighLow=b.Low;}if(double.IsNaN(BLow)||b.Low<BLow){BLow=b.Low;BLowHigh=b.High;}}
  }
 }
 public sealed class CycleBox
 {
  public bool High,M,A,B,Bridge,Active,Reversal,Continuation;public int State;
  public double Top,Bottom,ATop,ABottom,BTop,BBottom,RightPrice=double.NaN;
  public DateTime Left,Right,Peak,RightTime;
  public double Level{get{return High?Top:Bottom;}}
  public bool All{get{return M&&A&&B;}}
 }
 public sealed class CycleTarget
 {public int Direction;public double[] Levels;public bool[] Hit=new bool[3];public DateTime Start;}
 public sealed class Cycle90Engine
 {
  public readonly List<CycleBox> Boxes=new List<CycleBox>(); public readonly List<Mark> Symbols=new List<Mark>();
  public readonly List<CycleTarget> Targets=new List<CycleTarget>();
  public bool Both,ShowGem=true,ShowTarget=true,ShowTargetLine;public int PivotLeft=1,PivotRight=1;
  CycleRange current;int prev=-1;DateTime date=DateTime.MinValue;List<Bar> history=new List<Bar>();
  CycleBox search,active;Bar extreme;int digit,actDigit,phase;double actPx,target,rx;DateTime actTime;int actDir;
  public bool Smt(CycleBox z){return Both?(z.M&&!z.A&&!z.B)||(!z.M&&z.A&&z.B):(z.M||z.A||z.B)&&!z.All;}
  public static int Digit(DateTime t){int n=t.Hour/10+t.Hour%10+t.Minute/10+t.Minute%10;return n==0?0:1+(n-1)%9;}
  void Reset(DateTime t)
  {
   if(t.DayOfWeek==DayOfWeek.Monday){Boxes.Clear();Symbols.Clear();}
   else{Boxes.RemoveAll(z=>z.State==3);foreach(var z in Boxes){z.Active=false;z.RightPrice=double.NaN;}Symbols.Clear();}
   Targets.Clear();search=active=null;phase=0;current=null;prev=-1;date=t.Date;
  }
  void AddBox(CycleRange r,bool high)
  {
   var z=new CycleBox{High=high,Left=r.Start,Right=r.End,Peak=high?r.HighTime:r.LowTime,
    Top=high?r.HighBar.High:r.LowBar.High,Bottom=high?r.HighBar.Low:r.LowBar.Low,
    ATop=high?r.AHigh:r.ALowHigh,ABottom=high?r.AHighLow:r.ALow,BTop=high?r.BHigh:r.BLowHigh,BBottom=high?r.BHighLow:r.BLow};
   var same=Boxes.LastOrDefault(x=>x.High==high);var opp=Boxes.LastOrDefault(x=>x.High!=high);
   foreach(var p in new[]{same,opp})if(p!=null&&p.Peak==z.Peak){p.Bridge=z.Bridge=true;p.State=Math.Max(p.State,2);z.State=2;}
   Boxes.Add(z);
  }
  CycleBox Opposite(CycleBox z){return Boxes.FirstOrDefault(x=>x.Left==z.Left&&x.High!=z.High);}
  void Symbol(DateTime t,double p,string text,uint color){Symbols.Add(new Mark{Left=t,Right=t,Top=p,Bottom=p,Text=text,Color=color});}
  public void Push(Bar m,Bar a,Bar b)
  {
   if(date!=m.Time.Date)Reset(m.Time);
   history.Add(m);if(history.Count>600)history.RemoveAt(0);
   int idx=(int)Math.Floor((m.Time.Hour*60+m.Time.Minute-60)/90.0);
   bool weekday=m.Time.DayOfWeek!=DayOfWeek.Saturday&&m.Time.DayOfWeek!=DayOfWeek.Sunday;
   if(weekday&&idx>=0)
   {
    if(current==null){current=new CycleRange();current.Add(m,a,b);prev=idx;}
    else if(idx!=prev)
    {
     // The exact boundary M1 belongs to both adjacent cycle snapshots, as in v25.3.
     if(m.Time.Hour*60+m.Time.Minute==(prev+1)*90+60)current.Add(m,a,b);
     if((current.End-current.Start).TotalMinutes<=95){AddBox(current,true);AddBox(current,false);}
     current=new CycleRange();current.Add(m,a,b);prev=idx;
    }
    else current.Add(m,a,b);
   }
   foreach(var z in Boxes)
   {
    if(m.Time<z.Right)continue;
    z.M|=z.High?m.High>z.Top:m.Low<z.Bottom;
    if(a!=null)z.A|=z.High?a.High>z.ATop:a.Low<z.ABottom;
    if(b!=null)z.B|=z.High?b.High>z.BTop:b.Low<z.BBottom;
    if(z.M)z.State=3;else if(z.Bridge)z.State=2;else if(Smt(z)&&z.State<1)z.State=1;
    bool today=z.Right.Date==m.Time.Date;
    if(Smt(z)&&today)
    {
     double p=z.High?m.High:m.Low;
     if(!z.Active||double.IsNaN(z.RightPrice)||(z.High?p>z.RightPrice:p<z.RightPrice)){z.RightPrice=p;z.RightTime=m.Time;}z.Active=true;
    }
    else{z.Active=false;z.RightPrice=double.NaN;}
   }
   // Ordering is deliberately low-boxes then high-boxes, matching the Pine source.
   foreach(var z in Boxes.OrderBy(x=>x.High?1:0))
   {
    if(z.Right.Date!=m.Time.Date||m.Time.AddMinutes(1)<z.Right)continue;
    bool allAbove=a!=null&&b!=null&&m.Close>z.Top&&a.Close>z.ATop&&b.Close>z.BTop;
    bool allBelow=a!=null&&b!=null&&m.Close<z.Bottom&&a.Close<z.ABottom&&b.Close<z.BBottom;
    // Source v25.3 is asymmetric: high-box reversal uses MAIN close only.
    bool rev=!z.Reversal&&Smt(z)&&!z.All&&(z.High?m.Close<z.Bottom:allAbove);
    bool cont=!z.Continuation&&z.All&&(z.High?allAbove:m.Close<z.Bottom);
    if(rev)
    {
     z.Reversal=true;Symbol(m.Time,z.High?m.High:m.Low,"●",z.High?0xfff23645:0xff00cc00);
     var o=Opposite(z);if(ShowTarget&&o!=null)Targets.Add(new CycleTarget{Direction=z.High?-1:1,Start=m.Time,Levels=new[]{o.Level,o.High?o.ATop:o.ABottom,o.High?o.BTop:o.BBottom}});
    }
    if(cont){z.Continuation=true;Symbol(m.Time,z.High?m.Low:m.High,"○",z.High?0xff00cc00:0xfff23645);}
   }
   foreach(var t in Targets)for(int k=0;k<3;k++)
   {Bar x=k==0?m:k==1?a:b;if(x!=null&&!t.Hit[k]&&!double.IsNaN(t.Levels[k])&&(t.Direction==1?x.High>=t.Levels[k]:x.Low<=t.Levels[k]))t.Hit[k]=true;}
   if(ShowGem)Gem(m);
   if(Symbols.Count>450)Symbols.RemoveRange(0,Symbols.Count-450);
  }
  void Gem(Bar m)
  {
   if(search!=null)
   {
    if(search.All||search.Right.Date!=m.Time.Date){search=null;extreme=null;}
    else if(search.Reversal)
    {
     if(extreme!=null&&(digit==3||digit==6||digit==9))
     {
      if(active!=null)Symbol(extreme.Time.AddMinutes(-1),extreme.Close,"●",0xff888888);
      active=search;actDir=search.High?-1:1;phase=1;actDigit=digit;actPx=actDir==1?Math.Min(extreme.Close,m.Low):Math.Max(extreme.Close,m.High);actTime=extreme.Time;
      var opp=Opposite(search);target=opp==null?double.NaN:opp.Level;Symbol(extreme.Time,extreme.Close,digit.ToString(),0xff00cc00);
     }
     search=null;extreme=null;
    }
    else
    {
     double p=search.High?m.High:m.Low;
     if(extreme==null||(search.High?p>extreme.Close:p<extreme.Close)){extreme=new Bar(m.Time,p,p,p,p);digit=Digit(m.Time);}
    }
   }
   if(search==null)
   {
    CycleBox candidate=null;
    foreach(var z in Boxes.Where(z=>z.High))if(z.Right.Date==m.Time.Date&&z.M&&Smt(z)&&!z.All&&!z.Reversal&&(candidate==null||z.Right>=candidate.Right))candidate=z;
    foreach(var z in Boxes.Where(z=>!z.High))if(z.Right.Date==m.Time.Date&&z.M&&Smt(z)&&!z.All&&!z.Reversal&&(candidate==null||z.Right>candidate.Right))candidate=z;
    if(candidate!=null)
    {
     search=candidate;extreme=null;
     for(int n=history.Count-1;n>=Math.Max(0,history.Count-301);n--)
     {Bar x=history[n];if(x.Time<search.Right)break;double p=search.High?x.High:x.Low;if(extreme==null||(search.High?p>extreme.Close:p<extreme.Close))extreme=new Bar(x.Time,p,p,p,p);}
     digit=extreme==null?0:Digit(extreme.Time);
    }
   }
   if(active!=null&&(actDir==1?m.Low<actPx:m.High>actPx)){Symbol(actTime.AddMinutes(-1),actPx,"●",0xff888888);active=null;phase=0;}
   if(active!=null&&phase==1&&!double.IsNaN(target)&&(actDir==1?m.High>=target:m.Low<=target)){phase=2;rx=double.NaN;}
   if(active!=null&&phase==2)
   {
    double p=actDir==1?m.High:m.Low;rx=double.IsNaN(rx)?p:actDir==1?Math.Max(rx,p):Math.Min(rx,p);
    int center=history.Count-1-PivotRight;
    if(center>=PivotLeft)
    {
     Bar x=history[center];double cp=actDir==1?x.High:x.Low;bool pivot=true;
     for(int k=center-PivotLeft;k<=center+PivotRight;k++)if(k!=center&&(actDir==1?history[k].High>cp:history[k].Low<cp))pivot=false;
     if(pivot&&(actDir==1?cp>=rx:cp<=rx)&&Digit(x.Time)==actDigit){Symbol(x.Time,cp,actDigit.ToString(),0xfff23645);active=null;phase=0;}
    }
   }
  }
  public List<Mark> Marks(DateTime end,bool oneMinute)
  {
   var result=new List<Mark>();
   foreach(var z in Boxes)
   {
    uint color=z.State==3?0xffffff00:z.State==2?0xff0000ff:z.State==1?0xff800080:0xff00ffff;
    result.Add(new Mark{Left=z.Left,Right=z.Right,Top=z.Top,Bottom=z.Bottom,Color=color,BorderColor=(color&0xffffff)|(z.State==1?0xb3000000u:0x80000000u),BorderWidth=1,Fill=true,Opacity=z.State>=1&&z.State<=2?35:18});
    if(z.Bridge)result.Add(new Mark{Left=z.Peak,Right=z.Peak.AddMinutes(1),Top=z.Top,Bottom=z.Bottom,Color=0xff0000ff,Fill=true,Opacity=60});
    if(z.Active&&!double.IsNaN(z.RightPrice))
    {
     string label=z.M?(!z.A&&!z.B?"ES+YM":!z.A?"ES":!z.B?"YM":""):(z.A&&z.B?"ES+YM":z.A?"ES":z.B?"YM":"");
     result.Add(new Mark{Left=z.Peak,Right=z.RightTime,Top=z.Level,Bottom=z.RightPrice,Color=0xff800080,Width=3,Dashed=true,Text=label});
    }
   }
   if(oneMinute)result.AddRange(Symbols);
   if(oneMinute&&search!=null&&extreme!=null&&(digit==3||digit==6||digit==9))result.Add(new Mark{Left=extreme.Time,Right=extreme.Time,Top=extreme.Close,Bottom=extreme.Close,Color=0xff00cc00,Text=digit.ToString()});
   if(ShowTargetLine)foreach(var t in Targets)result.Add(new Mark{Left=t.Start,Right=end,Top=t.Levels[0],Bottom=t.Levels[0],Color=0xffff9800,Dashed=true});
   return result;
  }
 }
}
