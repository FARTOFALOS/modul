using System;
namespace MNQProtection
{
 public sealed class ExitLifecycleGate
 {
  public static string NewOcoGroup(){return "MNQRP_"+Guid.NewGuid().ToString("N");}
  bool flat,confirmed,closingFillObserved;
  readonly System.Collections.Generic.HashSet<object> completed=new System.Collections.Generic.HashSet<object>();
  readonly System.Collections.Generic.HashSet<object> cancelled=new System.Collections.Generic.HashSet<object>();
  public void Position(bool isFlat){flat=isFlat;if(!flat)confirmed=false;else if(closingFillObserved)confirmed=true;}
  public void Execution(object order,bool isFlat){Execution(order,isFlat,true);}
  public void Execution(object order,bool isFlat,bool fullyFilled){if(order!=null&&fullyFilled){completed.Add(order);closingFillObserved=true;}flat=isFlat;if(flat&&closingFillObserved)confirmed=true;}
  public bool CanCancel(object order,bool live,bool nativeOcoHandlesIt)
  {return flat&&confirmed&&order!=null&&live&&!nativeOcoHandlesIt&&!completed.Contains(order)&&!cancelled.Contains(order);}
  public void SentCancel(object order){cancelled.Add(order);}
  public void Reset(){flat=confirmed=closingFillObserved=false;completed.Clear();cancelled.Clear();}
 }
 public static class RiskMath
 {
  // Rithmic cash excludes commissions. Subtract only fees for the same current session;
  // prior settled sessions cannot be inferred from an execution snapshot.
  public static double NetSessionCash(double cash,double sessionFees){return cash-sessionFees;}
  public static DateTime SessionStartUtc(DateTime utcNow)
  {
   var eastern=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
   var now=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow,DateTimeKind.Utc),eastern);
   var start=now.Date.AddHours(17);if(now<start)start=start.AddDays(-1);
   return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start,DateTimeKind.Unspecified),eastern);
  }
  public static double NetDayResult(double grossRealized,double sessionFees,bool hasSessionExecutions)
  {return hasSessionExecutions?grossRealized-sessionFees:0;}
  public static double EvaluationFee(string instrument,int quantity)
  {if(quantity<0)throw new ArgumentException("Invalid execution quantity");return quantity*(Array.IndexOf(new[]{"MNQ","MES","MYM","M2K","MCL","MGC","M6A","M6B","M6E","MCD","MSF","MJY","MNG","MHG","MBT","MET"},instrument.ToUpperInvariant())>=0?1.0:2.5);}
  // All prices are rounded toward the entry/profit, so tick rounding cannot increase planned loss.
  public static double Price(double entry,int quantity,int direction,double pointValue,double tick,double budget,double roundTripFee)
  {
   if(quantity<=0||pointValue<=0||tick<=0||budget<=0||roundTripFee<0||direction*direction!=1||double.IsNaN(entry)||double.IsInfinity(entry))throw new ArgumentException("Invalid risk inputs");
   double remaining=budget-quantity*roundTripFee;
   if(remaining<=0)throw new ArgumentException("Fees exhaust the risk budget");
   double raw=entry-direction*remaining/(quantity*pointValue);
   return direction==1?Math.Ceiling(raw/tick-1e-9)*tick:Math.Floor(raw/tick+1e-9)*tick;
  }
  public static double Breakeven(double entry,int direction,double pointValue,double tick,double roundTripFee)
  {
   if(pointValue<=0||tick<=0||roundTripFee<0||direction*direction!=1)throw new ArgumentException("Invalid BE inputs");
   double raw=entry+direction*roundTripFee/pointValue;
   return direction==1?Math.Ceiling(raw/tick-1e-9)*tick:Math.Floor(raw/tick+1e-9)*tick;
  }
  // Target is rounded away from the entry so planned net profit is never below the requested target.
  public static double Target(double entry,int quantity,int direction,double pointValue,double tick,double netTarget,double roundTripFee)
  {
   if(quantity<=0||pointValue<=0||tick<=0||netTarget<=0||roundTripFee<0||direction*direction!=1||double.IsNaN(entry)||double.IsInfinity(entry))throw new ArgumentException("Invalid target inputs");
   double raw=entry+direction*(netTarget+quantity*roundTripFee)/(quantity*pointValue);
   return direction==1?Math.Ceiling(raw/tick-1e-9)*tick:Math.Floor(raw/tick+1e-9)*tick;
  }
  public static double PlannedLoss(double entry,double stop,int quantity,int direction,double pointValue,double roundTripFee)
  {return direction*(entry-stop)*quantity*pointValue+quantity*roundTripFee;}
  public static double PlannedNetProfit(double entry,double target,int quantity,int direction,double pointValue,double roundTripFee)
  {return direction*(target-entry)*quantity*pointValue-quantity*roundTripFee;}
  public static double NeverLoosen(double oldPrice,double desired,int direction)
  {return oldPrice<=0?desired:direction==1?Math.Max(oldPrice,desired):Math.Min(oldPrice,desired);}
 }
}
