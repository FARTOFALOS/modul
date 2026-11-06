using System;
using MNQProtection;
class RiskTests
{
 static int n;static void C(bool x,string t){if(!x)throw new Exception(t);n++;}
 static void Main()
 {
  C(RiskMath.Price(30000,1,1,2,.25,250,2)==29876,"1 MNQ includes $2 round trip");
  C(RiskMath.Price(30000,2,1,2,.25,250,2)==29938.5,"2 MNQ fixed total $250 including $4 fees");
  C(RiskMath.Price(30000,4,1,2,.25,250,2)==29969.75,"4 MNQ fixed total $250 including $8 fees");
  C(RiskMath.Price(30000,1,-1,2,.25,250,2)==30124,"short symmetric");
  C(RiskMath.Breakeven(30000,1,2,.25,2)==30001,"BE long includes commission");
  C(RiskMath.Breakeven(30000,-1,2,.25,2)==29999,"BE short includes commission");
  C(RiskMath.Target(30000,1,1,2,.25,250,2)==30126,"long target is net $250 after fee");
  C(RiskMath.Target(30000,2,-1,2,.25,250,2)==29936.5,"short target is net $250 after fees");
  C(RiskMath.NeverLoosen(30001,29876,1)==30001,"auto cannot undo long BE");
  C(RiskMath.NeverLoosen(29999,30124,-1)==29999,"auto cannot undo short BE");
  C(RiskMath.Price(30000,1,1,2,.25,300,2)==29851,"$300 total: one MNQ,149 points including fee");
  C(RiskMath.Price(30000,2,1,2,.25,300,2)==29926,"$300 total: two MNQ,74 points including fees");
  C(RiskMath.Price(30000,4,1,2,.25,300,2)==29963.5,"$300 total: four MNQ,36.5 points including fees");
  foreach(int direction in new[]{-1,1})
  {
   double previous=RiskMath.Price(30000,1,direction,2,.25,300,2);
   foreach(int quantity in new[]{2,4,3,1,5,2,1})
   {previous=RiskMath.NeverLoosen(previous,RiskMath.Price(30000,quantity,direction,2,.25,300,2),direction);C(RiskMath.PlannedLoss(30000,previous,quantity,direction,2,2)<=300.000001,"scale in/out total risk stays capped");}
   previous=RiskMath.Breakeven(30000,direction,2,.25,2);
   foreach(int quantity in new[]{4,2,1})
   {previous=RiskMath.NeverLoosen(previous,RiskMath.Price(30000,quantity,direction,2,.25,300,2),direction);C(RiskMath.PlannedLoss(30000,previous,quantity,direction,2,2)<=0.000001,"partial exits cannot undo BE including fees on remaining contracts");}
  }
  C(RiskMath.EvaluationFee("MNQ",1)==1,"one MNQ filled side costs $1");
  C(RiskMath.NetSessionCash(50897.5,84)==50813.5,"gross Rithmic cash minus session fees");
  C(RiskMath.EvaluationFee("MNQ",3)+RiskMath.EvaluationFee("MNQ",1)+RiskMath.EvaluationFee("MNQ",2)==6,"partial exits charge each filled contract exactly once");
  C(RiskMath.EvaluationFee("NQ",2)==5,"mini commissions differ from micros");
  C(RiskMath.SessionStartUtc(new DateTime(2026,11,2,21,59,0,DateTimeKind.Utc))==new DateTime(2026,11,1,22,0,0,DateTimeKind.Utc),"before 17 NY retains current trading day after DST");
  C(RiskMath.SessionStartUtc(new DateTime(2026,11,2,22,0,0,DateTimeKind.Utc))==new DateTime(2026,11,2,22,0,0,DateTimeKind.Utc),"fees reset at user-selected 17 New York");
  C(RiskMath.SessionStartUtc(new DateTime(2026,10,30,21,1,0,DateTimeKind.Utc))==new DateTime(2026,10,30,21,0,0,DateTimeKind.Utc),"summer offset uses DST");
  C(RiskMath.NetDayResult(353.5,463,true)==-109.5,"Nov 4 result matches OneUp after all commissions");
  C(RiskMath.NetDayResult(-48.5,2,true)==-50.5,"Nov 5 closed loss includes both filled sides");
  C(RiskMath.NetDayResult(353.5,0,false)==0,"new session without fills cannot carry stale previous-day provider result");
  var gate=new ExitLifecycleGate();var filledStop=new object();var take=new object();var externalExit=new object();
  var ocoIds=new System.Collections.Generic.HashSet<string>();for(int i=0;i<1000;i++)C(ocoIds.Add(ExitLifecycleGate.NewOcoGroup()),"every new protective submission gets a fresh OCO id");
  gate.Position(true);C(!gate.CanCancel(filledStop,true,false),"flat notification before execution must not cancel pending fill");
  gate.Execution(filledStop,true);C(!gate.CanCancel(filledStop,true,false),"filled stop cannot be cancelled even if order snapshot is stale Accepted");
  C(!gate.CanCancel(take,true,true),"native OCO must not receive duplicate cancellation");
  C(gate.CanCancel(take,true,false),"unlinked manual take must be cleaned after confirmed exit");gate.SentCancel(take);
  C(!gate.CanCancel(take,true,false),"repeat notifications must not resend cancel");
  gate.Reset();gate.Execution(filledStop,true);gate.Position(true);C(!gate.CanCancel(filledStop,true,false),"execution before flat still fences the filled leg");
  gate.Execution(externalExit,true);C(gate.CanCancel(take,true,false),"manual close allows one cleanup after execution");
  gate.Reset();gate.Execution(filledStop,false,false);gate.Execution(externalExit,true);
  C(gate.CanCancel(filledStop,true,false),"partial fill must not fence the unfilled remainder after external close");
  gate.Reset();gate.Position(false);gate.Execution(externalExit,false,true);gate.Position(true);
  C(gate.CanCancel(take,true,false),"closing execution before account flat must permit orphan-stop cleanup");
  C(!gate.CanCancel(externalExit,true,false),"late flat must not cancel completed closing order");
  gate.Reset();gate.Position(true);gate.Execution(externalExit,true,true);
  C(gate.CanCancel(take,true,false),"account flat before closing execution must also clean protection");
  gate.Reset();gate.Execution(externalExit,true,true);gate.Reset();gate.Position(false);gate.Position(true);
  C(!gate.CanCancel(take,true,false),"new position cycle cannot inherit prior close confirmation");
  gate.Reset();gate.Execution(externalExit,false,false);gate.Position(true);
  C(!gate.CanCancel(take,true,false),"partial closing fill plus flat alone cannot cancel an unreported completed leg");
  for(int q=1;q<=100;q++)foreach(int d in new[]{-1,1})foreach(double avg in new[]{30000,30000.125,29999.9166666667})
  {double p=RiskMath.Price(avg,q,d,2,.25,250,2),t=RiskMath.Target(avg,q,d,2,.25,250,2);C(RiskMath.PlannedLoss(avg,p,q,d,2,2)<=250.000001,"rounding never exceeds budget");C(Math.Abs(p/.25-Math.Round(p/.25))<1e-7,"valid stop tick");C(RiskMath.PlannedNetProfit(avg,t,q,d,2,2)>=249.999999,"target reaches requested net profit");C(Math.Abs(t/.25-Math.Round(t/.25))<1e-7,"valid target tick");}
  bool rejected=false;try{RiskMath.Price(30000,125,1,2,.25,250,2);}catch(ArgumentException){rejected=true;}C(rejected,"fees consume budget: no silent invalid stop");
  rejected=false;try{RiskMath.Price(30000,0,1,2,.25,250,2);}catch(ArgumentException){rejected=true;}C(rejected,"flat: no protective entry");
  for(int q=1;q<=100;q++)foreach(int d in new[]{-1,1})foreach(double avg in new[]{30000,30000.125,29999.9166666667})
  {
   double stop300=RiskMath.Price(avg,q,d,2,.25,300,2),target300=RiskMath.Target(avg,q,d,2,.25,300,2),be300=RiskMath.Breakeven(avg,d,2,.25,2);
   C(RiskMath.PlannedLoss(avg,stop300,q,d,2,2)<=300.000001,"current 300 budget includes fees across quantity and average");
   C(RiskMath.PlannedNetProfit(avg,target300,q,d,2,2)>=299.999999,"current 300 net target includes fees");
   C(RiskMath.PlannedNetProfit(avg,be300,q,d,2,2)>=-0.000001,"BE covers remaining quantity commission");
  }
  Console.WriteLine("PASS "+n+" risk math checks");
 }
}
