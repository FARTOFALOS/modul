// OneUp Evaluation profile. No entry orders. Startup arms protection only after observing a flat MNQ account.
// Local OCO is not an offline/server guarantee. Live writes require a separate user opt-in.
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using MNQProtection;

namespace NinjaTrader.NinjaScript.Indicators
{
 public class MNQProtectionPanel : Indicator
 {
  readonly object sync=new object();
  static readonly HashSet<string> owners=new HashSet<string>();
  static readonly object apiPanelsLock=new object();
  static readonly List<WeakReference> apiPanels=new List<WeakReference>();
  DateTime apiQuoteTime=DateTime.MinValue;
  public static Dictionary<string,object> SimApiPanel(string instrument,bool arm)
  {
   WeakReference[] refs;lock(apiPanelsLock)refs=apiPanels.ToArray();
   foreach(var wr in refs){var p=wr.Target as MNQProtectionPanel;if(p==null)continue;lock(p.sync)
   {
    if(p.stopped||p.selected==null||p.selected.Name!="Sim101"||p.Instrument==null||p.Instrument.FullName!=instrument)continue;
    if(arm){if(!p.Connected()||!p.Acquire())throw new InvalidOperationException("SIM protection unavailable");p.automatic=true;p.startupPending=false;p.reconcilePending=true;p.reconcileAfter=DateTime.UtcNow;}
    return new Dictionary<string,object>{{"found",true},{"armed",p.automatic},{"connected",p.Connected()},{"riskBudget",p.RiskBudget},{"roundTripCommission",p.RoundTripCommission},{"bid",p.bid},{"ask",p.ask},{"last",p.last},{"quoteAgeMs",(DateTime.UtcNow-p.apiQuoteTime).TotalMilliseconds}};
   }}
   return new Dictionary<string,object>{{"found",false},{"armed",false}};
  }
  Account selected;AccountSelector selector;Border panel;TextBlock summary,stateText,levelsText;
  Button autoButton,manualButton,beButton,pickStopButton,pickTargetButton;
  DispatcherTimer timer;ChartScale priceScale;
  Order stop,target;string group="",lease="",message="Выберите счёт в Chart Trader";
  bool automatic,manualTracking,stopped,writing,closing,startupPending,beRequested;int pick,knownDirection,managedDirection;double plannedStop,plannedTarget;bool hasStop,hasTarget;
  readonly HashSet<AccountItem> reported=new HashSet<AccountItem>();
  int qty;double average,bid,ask,last;DateTime lastChange=DateTime.MinValue;
  sealed class ExecutionMark {public DateTime Time;public double Price;public int Side;public bool Entry,Exit;}
  ExecutionMark[] marks=new ExecutionMark[0];
  readonly ExitLifecycleGate exitGate=new ExitLifecycleGate();
  readonly List<Order> companionTakes=new List<Order>();
  readonly HashSet<Order> executedExits=new HashSet<Order>();
  readonly Dictionary<Order,int> executionFills=new Dictionary<Order,int>();
  readonly HashSet<string> seenFills=new HashSet<string>();
  DateTime reconcileAfter=DateTime.MinValue;bool reconcilePending;
  TextBlock cursorText;
  [NinjaScriptProperty,Range(1,100000),Display(Name="Общий бюджет убытка, $",GroupName="Риск",Order=0)]public double RiskBudget{get;set;}
  [NinjaScriptProperty,Range(0,1000),Display(Name="Комиссия за вход + выход / контракт, $",GroupName="Риск",Order=1)]public double RoundTripCommission{get;set;}
  [NinjaScriptProperty,Display(Name="Разрешить работу с проп/реальным счётом",GroupName="Разрешение",Order=0)]public bool AllowLiveOrders{get;set;}
  [NinjaScriptProperty,Display(Name="Автостоп после запуска (для новых позиций)",GroupName="Риск",Order=2)]public bool AutoProtectOnLoad{get;set;}
  [NinjaScriptProperty,Display(Name="Автоматический тейк",GroupName="Риск",Order=3)]public bool AutoProfitTarget{get;set;}
  protected override void OnStateChange()
  {
   if(State==State.SetDefaults)
   {
    Name="MNQ Protection Panel";Description="Fixed whole-position risk including OneUp Evaluation commissions. Manual OCO / automatic stop / BE / account display. Validate on Sim101 first.";
    IsOverlay=true;Calculate=Calculate.OnEachTick;IsAutoScale=false;DisplayInDataBox=false;PaintPriceMarkers=false;IsSuspendedWhileInactive=false;
    RiskBudget=300;RoundTripCommission=2;AllowLiveOrders=false;AutoProtectOnLoad=false;AutoProfitTarget=false;
   }
   else if(State==State.Historical&&ChartControl!=null)
    ChartControl.Dispatcher.InvokeAsync((Action)Install);
   else if(State==State.Terminated)
   {
    stopped=true;automatic=false;manualTracking=false;
    lock(apiPanelsLock)apiPanels.RemoveAll(w=>!w.IsAlive||ReferenceEquals(w.Target,this));
    if(selected!=null){selected.PositionUpdate-=PositionChanged;selected.OrderUpdate-=OrderChanged;selected.AccountItemUpdate-=AccountChanged;selected.ExecutionUpdate-=ExecutionChanged;}
    Release();
    if(ChartControl!=null)ChartControl.Dispatcher.InvokeAsync((Action)(()=>{if(timer!=null)timer.Stop();ChartControl.MouseLeftButtonDown-=PickPrice;ChartControl.MouseMove-=PreviewPrice;if(panel!=null)UserControlCollection.Remove(panel);}));
    // Deliberately do NOT cancel protective orders when the indicator is removed.
   }
  }
  Button Button(string text,RoutedEventHandler handler)
  {var b=new Button{Content=text,Margin=new Thickness(1),Padding=new Thickness(5,3,5,3),FontSize=11,Foreground=Brushes.White,Background=Brushes.DimGray};b.Click+=handler;return b;}
  void Install()
  {
   if(stopped||panel!=null)return;
   lock(apiPanelsLock){apiPanels.RemoveAll(w=>!w.IsAlive);apiPanels.Add(new WeakReference(this));}
   if(ChartBars!=null)ChartBars.Properties.PlotExecutions=ChartExecutionStyle.DoNotPlot;
   var stack=new StackPanel();summary=new TextBlock{Foreground=Brushes.White,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3)};
   stack.Children.Add(summary);
   var row=new WrapPanel();
   autoButton=Button("АВТО СТОП $300: ВЫКЛ",ToggleAuto);manualButton=Button("РУЧНОЙ",Manual);
   beButton=Button("Б/У + ком.",Breakeven);beButton.Background=Brushes.DarkGreen;
   row.Children.Add(autoButton);row.Children.Add(manualButton);row.Children.Add(beButton);stack.Children.Add(row);
   var prices=new WrapPanel();pickStopButton=Button("СТОП",(s,e)=>{pick=1;message="Кликните цену стопа.";});
   pickTargetButton=Button("ТЕЙК",(s,e)=>{pick=2;message="Кликните цену тейка.";});
   prices.Children.Add(pickStopButton);prices.Children.Add(pickTargetButton);prices.Children.Add(Button("ПОСТАВИТЬ OCO",PlaceProtection));stack.Children.Add(prices);
   levelsText=new TextBlock{Foreground=Brushes.LightSkyBlue,FontSize=10,Margin=new Thickness(3)};stack.Children.Add(levelsText);
   cursorText=new TextBlock{Foreground=Brushes.LightSkyBlue,FontSize=10,Margin=new Thickness(3)};stack.Children.Add(cursorText);
   stateText=new TextBlock{Foreground=Brushes.Gold,FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3)};stack.Children.Add(stateText);
   panel=new Border{Child=stack,Background=new SolidColorBrush(Color.FromArgb(235,30,32,38)),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Width=350,Margin=new Thickness(55,30,0,0)};
   UserControlCollection.Add(panel);ChartControl.MouseLeftButtonDown+=PickPrice;ChartControl.MouseMove+=PreviewPrice;
   timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=(s,e)=>Tick();timer.Start();Tick();
  }
  DependencyObject Find(DependencyObject root,string id)
  {
   if(root==null)return null;
   if(AutomationProperties.GetAutomationId(root)==id)return root;
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var x=Find(VisualTreeHelper.GetChild(root,i),id);if(x!=null)return x;}return null;
  }
  void Bind(Account a)
  {
   if(a==selected)return;
   if(selected!=null){selected.PositionUpdate-=PositionChanged;selected.OrderUpdate-=OrderChanged;selected.AccountItemUpdate-=AccountChanged;selected.ExecutionUpdate-=ExecutionChanged;}
   automatic=manualTracking=closing=beRequested=false;Release();selected=a;stop=target=null;group="";knownDirection=managedDirection=0;hasStop=hasTarget=false;reported.Clear();marks=new ExecutionMark[0];
   exitGate.Reset();companionTakes.Clear();executedExits.Clear();executionFills.Clear();seenFills.Clear();reconcilePending=false;startupPending=AutoProtectOnLoad;
   if(selected!=null){selected.PositionUpdate+=PositionChanged;selected.OrderUpdate+=OrderChanged;selected.AccountItemUpdate+=AccountChanged;selected.ExecutionUpdate+=ExecutionChanged;}
   message="Авто выключено. Выбор счёта не меняет существующие заявки.";
  }
  void Tick()
  {
   if(stopped||panel==null)return;
   lock(sync)
   {
   try
   {
    if(selector==null)selector=Find(Window.GetWindow(ChartControl),"ChartTraderControlAccountSelector") as AccountSelector;
    Bind(selector==null?null:selector.SelectedAccount);ReadPosition();
    if(startupPending&&selected!=null&&Connected()&&Instrument.MasterInstrument.Name=="MNQ"&&(selected.Name.IndexOf("OUP",StringComparison.OrdinalIgnoreCase)>=0||selected.Name=="Sim101")&&(AllowLiveOrders||selected.Name=="Sim101"))
    {
     if(qty==0&&Acquire()){automatic=true;startupPending=false;message="Автостоп вооружён для новых позиций: общий риск $"+RiskBudget+" с комиссией.";}
     else if(qty>0)message="Автостарт ожидает закрытия текущей позиции. Для её защиты нажмите АВТО вручную.";
    }
    if(!reconcilePending&&(automatic||manualTracking))Reconcile();
    if(reconcilePending&&DateTime.UtcNow>=reconcileAfter){reconcilePending=false;Reconcile();}
    if(beRequested&&qty>0&&Ready(stop)){beRequested=false;Breakeven(null,null);}
    if(selected==null){summary.Text="СЧЁТ НЕ ВЫБРАН — включите Chart Trader";autoButton.Content="АВТО СТОП $"+RiskBudget.ToString("0.##")+": ВЫКЛ";autoButton.Background=Brushes.DimGray;levelsText.Text="";stateText.Text="Счёт не выбран. Автоматизация выключена.";return;}
    double cash=selected.Get(AccountItem.CashValue,Currency.UsDollar);
    double grossRealized=selected.Get(AccountItem.GrossRealizedProfitLoss,Currency.UsDollar);
    UpdateExecutionMarks();
    string connection=Connected()?"подключён":"НЕТ СОЕДИНЕНИЯ";
    bool evaluation=selected.Name.IndexOf("OUP",StringComparison.OrdinalIgnoreCase)>=0;
    double fees=evaluation?SessionFees():0;
    double netDay=RiskMath.NetDayResult(grossRealized,fees,fees>0);
    // This Evaluation feed's CashValue/GrossRealizedProfitLoss exclude current-day
    // commissions. Prior settled days are already in CashValue. Never subtract them again.
    // No unrealized PnL or projected exit fee is included: only actually filled sides.
    summary.Text=selected.Name+" · "+connection+" · v11.06-S300-F1\n"+
     (evaluation?"Баланс с комиссией: "+Money(RiskMath.NetSessionCash(cash,fees),AccountItem.CashValue)+
      "\nСегодня чистыми: "+(double.IsNaN(netDay)||double.IsInfinity(netDay)?"нет данных":netDay.ToString("+$0.00;-$0.00;$0.00")):
      "Баланс / день с комиссией: только OneUp Evaluation");
    autoButton.Content="АВТО СТОП $"+RiskBudget.ToString("0.##")+": "+(automatic?"ВКЛ":"ВЫКЛ");autoButton.Background=automatic?Brushes.DarkRed:Brushes.DimGray;
    if(qty>0)
    {
     double calculated=RiskMath.Price(average,qty,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RiskBudget,RoundTripCommission);
     double calculatedTarget=RiskMath.Target(average,qty,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RiskBudget,RoundTripCommission);
     levelsText.Text="SL "+(Live(stop)?stop.StopPrice.ToString("0.#####"):calculated.ToString("0.#####"))+(AutoProfitTarget||Live(target)?" · TP "+(Live(target)?target.LimitPrice.ToString("0.#####"):calculatedTarget.ToString("0.#####")):"");
    }
    else levelsText.Text="Авто — для следующей позиции. Комиссия вход+выход: $"+RoundTripCommission+" / контракт.";
    stateText.Text=message+"\nSL "+(stop==null?"—":stop.OrderState+" "+stop.StopPrice.ToString("0.#####"))+" · TP "+(target==null?"—":target.OrderState+" "+target.LimitPrice.ToString("0.#####"))+(AllowLiveOrders?"":" · только Sim101");
   }
   catch(Exception ex){message="Ошибка панели: "+ex.Message;if(stateText!=null)stateText.Text=message;}
   }
  }
  string Money(double x,AccountItem item){return double.IsNaN(x)||double.IsInfinity(x)||x==0&&!reported.Contains(item)?"нет данных":x.ToString("$0.00;-$0.00");}
  double SessionFees()
  {
   // Rithmic replays current-session fills on reconnect. Recompute from the snapshot,
   // deduplicated by execution+order, so reconnects and indicator reloads never add twice.
   Execution[] fills;lock(selected.Executions)fills=selected.Executions.ToArray();
   var sessionStart=NinjaTrader.Core.Globals.ToApplicationTime(RiskMath.SessionStartUtc(DateTime.UtcNow),TimeZoneInfo.Utc);
   return fills.Where(x=>x.Instrument!=null&&x.Quantity>0&&x.Time>=sessionStart)
    .GroupBy(x=>x.ExecutionId+"|"+x.OrderId+"|"+x.Instrument.FullName)
    .Sum(g=>RiskMath.EvaluationFee(g.First().Instrument.MasterInstrument.Name,g.First().Quantity));
  }
  void UpdateExecutionMarks()
  {
   if(selected==null||Instrument==null){marks=new ExecutionMark[0];return;}
   Execution[] executions;lock(selected.Executions)executions=selected.Executions.Where(x=>x.Instrument.FullName==Instrument.FullName).ToArray();
   var result=new List<ExecutionMark>();int after=qty*knownDirection;
   // Reconstruct backwards from the actual current position: no assumption that the oldest available execution started flat.
   foreach(var x in executions.Select((e,i)=>new{E=e,I=i}).OrderByDescending(x=>x.E.Time).ThenByDescending(x=>x.I))
   {
    var e=x.E;int side=e.MarketPosition==MarketPosition.Long?1:e.MarketPosition==MarketPosition.Short?-1:0;if(side==0)continue;
    int before=after-side*e.Quantity;bool exits=before!=0&&Math.Sign(before)!=side;
    result.Add(new ExecutionMark{Time=e.Time,Price=e.Price,Side=side,Exit=exits,Entry=!exits||e.Quantity>Math.Abs(before)});after=before;
   }
   marks=result.ToArray();
  }
  bool Connected(){return selected!=null&&selected.Connection!=null&&selected.Connection.Status==ConnectionStatus.Connected;}
  Position ReadPosition()
  {
   qty=0;average=0;knownDirection=0;if(selected==null||Instrument==null)return null;
   Position p;lock(selected.Positions)p=selected.Positions.FirstOrDefault(x=>x.Instrument.FullName==Instrument.FullName&&x.MarketPosition!=MarketPosition.Flat&&x.Quantity>0);
   if(p!=null){qty=p.Quantity;average=p.AveragePrice;knownDirection=p.MarketPosition==MarketPosition.Long?1:-1;}return p;
  }
  bool Writable(bool requirePosition)
  {
   if(!Connected()){message="Нет соединения. Заявки не отправлены.";return false;}
   if(Instrument.MasterInstrument.Name!="MNQ"){message="Управление защитой разрешено только на графике MNQ.";return false;}
   if(!AllowLiveOrders&&selected.Name!="Sim101"){message="Сначала проверка на Sim101. Проп/реальный счёт не изменён.";return false;}
   ReadPosition();if(requirePosition&&qty==0){message="Нет открытой позиции MNQ. Заявки не отправлены.";return false;}return true;
  }
  bool Acquire()
  {
   string key=selected.Name+"|"+Instrument.FullName;
   lock(owners){if(lease==key)return true;if(owners.Contains(key)){message="Этот счёт и MNQ уже обслуживает другая панель.";return false;}owners.Add(key);lease=key;}return true;
  }
  void Release(){lock(owners){if(lease!="")owners.Remove(lease);lease="";}}
  bool Live(Order x){return x!=null&&x.OrderState!=OrderState.Filled&&x.OrderState!=OrderState.Cancelled&&x.OrderState!=OrderState.Rejected;}
  bool Ready(Order x){return x!=null&&(x.OrderState==OrderState.Working||x.OrderState==OrderState.Accepted||x.OrderState==OrderState.PartFilled);}
  bool Own(Order x){return x!=null&&(ReferenceEquals(x,stop)||ReferenceEquals(x,target));}
  bool ClosingSide(Order x){return knownDirection==1?x.OrderAction==OrderAction.Sell:x.OrderAction==OrderAction.Buy||x.OrderAction==OrderAction.BuyToCover;}
  bool ForeignExits()
  {
   lock(selected.Orders)return selected.Orders.Any(x=>!Own(x)&&x.Instrument.FullName==Instrument.FullName&&Live(x)&&(x.OrderType==OrderType.StopMarket||x.OrderType==OrderType.StopLimit||x.OrderType==OrderType.Limit));
  }
  void ToggleAuto(object sender,RoutedEventArgs e)
  {
   lock(sync)
   {
    startupPending=false;
    if(automatic){automatic=false;message="Авто выключено. Существующий стоп остаётся действующим.";return;}
    if(!Writable(false)||!Acquire())return;

    automatic=true;manualTracking=false;message="Автостоп включён: общий риск $"+RiskBudget+" с комиссией.";
    Reconcile();
   }
  }
  void Manual(object sender,RoutedEventArgs e)
  {lock(sync){startupPending=false;automatic=false;pick=0;message="Ручной режим: укажите две цены и нажмите «Поставить защиту». Существующие заявки не отменены.";}}
  void PickPrice(object sender,MouseButtonEventArgs e)
  {
   if(pick==0||priceScale==null)return;
   var point=e.GetPosition(ChartControl);if(point.Y<ChartPanel.Y||point.Y>ChartPanel.Y+ChartPanel.H)return;
   double price=Instrument.MasterInstrument.RoundToTickSize(priceScale.GetValueByY((float)point.Y));
   if(pick==1){plannedStop=price;hasStop=true;}else{plannedTarget=price;hasTarget=true;}
   pick=0;e.Handled=true;message="Уровень выбран. Заявки ещё не отправлены.";Tick();ChartControl.InvalidateVisual();
  }
  void PreviewPrice(object sender,MouseEventArgs e)
  {
   if(cursorText==null||priceScale==null||qty<=0){if(cursorText!=null)cursorText.Text="";return;}
   var xy=e.GetPosition(ChartControl);if(xy.Y<ChartPanel.Y||xy.Y>ChartPanel.Y+ChartPanel.H)return;
   double price=Instrument.MasterInstrument.RoundToTickSize(priceScale.GetValueByY((float)xy.Y));
   double net=knownDirection*(price-average)*qty*Instrument.MasterInstrument.PointValue-qty*RoundTripCommission;
   cursorText.Text="Курсор "+price.ToString("0.#####")+" · вся позиция "+net.ToString("+$0.00;-$0.00;$0.00")+" с комиссией (расчёт)";
  }
  void PlaceProtection(object sender,RoutedEventArgs e)
  {
   lock(sync)
   {
    if(!Writable(true)||!Acquire())return;
    if(ForeignExits()){message="Обнаружены другие заявки MNQ. Они не изменены; новая пара не отправлена.";return;}
    if(!hasTarget||(!automatic&&!hasStop)){message="Сначала укажите стоп и тейк.";return;}
    double sp=automatic?RiskMath.Price(average,qty,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RiskBudget,RoundTripCommission):plannedStop;
    if(!ValidStop(sp)||!ValidTarget(plannedTarget))return;
    if(!Live(stop)||!Live(target))group=ExitLifecycleGate.NewOcoGroup();
    if(group==""){message="Нет собственной OCO-группы. Заявки не отправлены.";return;}
    var list=new List<Order>();OrderAction action=knownDirection==1?OrderAction.Sell:OrderAction.BuyToCover;
    if(!Live(stop)){stop=selected.CreateOrder(Instrument,action,OrderType.StopMarket,OrderEntry.Automated,TimeInForce.Gtc,qty,0,sp,group,"MNQPanel.Stop",NinjaTrader.Core.Globals.MaxDate,null);list.Add(stop);}
    if(!Live(target)){target=selected.CreateOrder(Instrument,action,OrderType.Limit,OrderEntry.Automated,TimeInForce.Gtc,qty,plannedTarget,0,group,"MNQPanel.Target",NinjaTrader.Core.Globals.MaxDate,null);list.Add(target);}
    managedDirection=knownDirection;plannedStop=stop!=null?stop.StopPrice:sp;hasStop=true;manualTracking=!automatic;if(list.Count>0)selected.Submit(list);
    message="Защитные заявки отправлены; ожидается подтверждение. OCO обслуживается локальным NinjaTrader.";
   }
  }
  bool ValidStop(double p)
  {
   double market=knownDirection==1?bid:ask;
   if(market<=0){message="Нет текущего Bid/Ask. Защита не отправлена.";return false;}
   if(knownDirection==1?p>=market:p<=market){message="Стоп уже за текущей ценой. Бюджет нельзя восстановить переносом стопа; автоматическое закрытие не выполняется.";return false;}return true;
  }
  bool ValidTarget(double p)
  {if(knownDirection==1?p<=bid:p>=ask){message="Тейк должен быть на прибыльной стороне от текущей цены. Заявки не отправлены.";return false;}return true;}
  void Breakeven(object sender,RoutedEventArgs e)
  {
   lock(sync)
   {
    if(!Writable(true))return;
    if(Live(stop)&&!Ready(stop)){beRequested=true;message="Б/У запрошен: ожидается подтверждение текущего стопа, затем он будет перенесён.";return;}
    // Existing manual profit targets must not block a protective BE stop.
    Order[] other;lock(selected.Orders)other=selected.Orders.Where(x=>!Own(x)&&x.Instrument.FullName==Instrument.FullName&&Live(x)).ToArray();
    var existingStops=other.Where(x=>ClosingSide(x)&&(x.OrderType==OrderType.StopMarket||x.OrderType==OrderType.StopLimit)).ToArray();
    if(existingStops.Length>0)
    {
     if(!Live(stop)&&existingStops.Length==1&&existingStops[0].OrderType==OrderType.StopMarket&&Ready(existingStops[0])&&existingStops[0].Quantity-existingStops[0].Filled==qty&&(knownDirection==1?existingStops[0].OrderAction==OrderAction.Sell:existingStops[0].OrderAction==OrderAction.Buy||existingStops[0].OrderAction==OrderAction.BuyToCover))
     {if(!Acquire())return;stop=existingStops[0];executionFills[stop]=stop.Filled;group=stop.Oco;managedDirection=knownDirection;manualTracking=!automatic;}
     else{message="Несколько стопов или несовпадающее количество: Б/У не изменён, дубликат не создан.";return;}
    }
    companionTakes.Clear();
    companionTakes.AddRange(other.Where(x=>x.OrderType==OrderType.Limit&&(knownDirection==1?x.OrderAction==OrderAction.Sell:x.OrderAction==OrderAction.Buy||x.OrderAction==OrderAction.BuyToCover)));
    double be=RiskMath.Breakeven(average,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RoundTripCommission);
    if(Ready(stop))be=RiskMath.NeverLoosen(stop.StopPrice,be,knownDirection);
    if(!ValidStop(be))return;
    if(Ready(stop))
    {stop.StopPriceChanged=be;stop.QuantityChanged=qty+stop.Filled;lastChange=DateTime.UtcNow;selected.Change(new[]{stop});message="Стоп переносится в Б/У с комиссией.";}
    else
    {
     if(!Acquire())return;
     // An existing OCO id cannot be extended/reused by a later submission.
     // Manual takes stay untouched and are linked locally by confirmed-exit cleanup.
     group=ExitLifecycleGate.NewOcoGroup();
     stop=selected.CreateOrder(Instrument,knownDirection==1?OrderAction.Sell:OrderAction.BuyToCover,OrderType.StopMarket,OrderEntry.Automated,TimeInForce.Gtc,qty,0,be,group,"MNQPanel.Stop",NinjaTrader.Core.Globals.MaxDate,null);
     managedDirection=knownDirection;manualTracking=!automatic;selected.Submit(new[]{stop});message="Реальный стоп Б/У с комиссией отправлен.";
    }
    plannedStop=be;hasStop=true;
   }
  }
  void PositionChanged(object sender,PositionEventArgs e)
  {lock(sync){if(sender!=selected||e.Position.Instrument.FullName!=Instrument.FullName)return;ReadPosition();exitGate.Position(qty==0);if(qty==0){if(stop!=null&&stop.OrderState==OrderState.Filled)exitGate.Execution(stop,true);if(target!=null&&target.OrderState==OrderState.Filled)exitGate.Execution(target,true);}reconcilePending=true;reconcileAfter=DateTime.UtcNow.AddMilliseconds(300);}}
  void ExecutionChanged(object sender,ExecutionEventArgs e)
  {
   lock(sync){if(sender!=selected||e.Execution.Instrument.FullName!=Instrument.FullName)return;
    int executionSide=e.Execution.MarketPosition==MarketPosition.Long?1:-1;
    bool isClosing=e.Execution.Order!=null&&(Own(e.Execution.Order)||companionTakes.Contains(e.Execution.Order)||managedDirection!=0&&executionSide!=managedDirection);
    var fillOrder=e.Execution.Order;
    if(fillOrder!=null&&seenFills.Add(e.Execution.ExecutionId+"|"+e.Execution.OrderId))
    {int previous;executionFills.TryGetValue(fillOrder,out previous);executionFills[fillOrder]=previous+e.Execution.Quantity;}
    bool fullyFilled=fillOrder!=null&&(fillOrder.OrderState==OrderState.Filled||fillOrder.Filled>=fillOrder.Quantity||executionFills.ContainsKey(fillOrder)&&executionFills[fillOrder]>=fillOrder.Quantity);
    if(isClosing&&fullyFilled)executedExits.Add(e.Execution.Order);
    // Execution.Position is the account quantity at this execution, independent of stale OrderState callbacks.
    if(isClosing)exitGate.Execution(fillOrder,e.Execution.Position==0,fullyFilled);ReadPosition();reconcilePending=true;reconcileAfter=DateTime.UtcNow.AddMilliseconds(300);
   }
  }
  void AccountChanged(object sender,AccountItemEventArgs e){lock(sync){if(sender==selected)reported.Add(e.AccountItem);}}
  void OrderChanged(object sender,OrderEventArgs e)
  {
   lock(sync)
   {
    if(sender!=selected||(!Own(e.Order)&&!companionTakes.Contains(e.Order)))return;
    if(e.Order.OrderState==OrderState.Rejected){automatic=false;manualTracking=false;beRequested=false;startupPending=AutoProtectOnLoad;message="ЗАЩИТНАЯ ЗАЯВКА ОТКЛОНЕНА. Проверьте позицию и заявки: "+e.Order.Name;return;}
    ReadPosition();bool siblingFilled=stop!=null&&stop.OrderState==OrderState.Filled||target!=null&&target.OrderState==OrderState.Filled||!string.IsNullOrEmpty(e.Order.Oco)&&executedExits.Any(x=>x.Oco==e.Order.Oco);
    if(e.Order.OrderState==OrderState.Cancelled&&qty>0&&!siblingFilled&&!closing){automatic=false;manualTracking=false;startupPending=AutoProtectOnLoad;message="Стоп отменён. Проверьте текущую позицию; после её полного закрытия автостоп снова включится для следующего входа.";return;}
    if(closing&&!Live(stop)&&!Live(target)&&!companionTakes.Any(Live)){stop=target=null;group="";closing=false;managedDirection=0;companionTakes.Clear();}
    if(Ready(e.Order)&&!writing)Reconcile();
   }
  }
  void Reconcile()
  {
   if(stopped||writing||!Writable(false))return;
   if(reconcilePending&&DateTime.UtcNow<reconcileAfter)return;
   if(qty>0&&(DateTime.UtcNow-lastChange).TotalMilliseconds<750)return;
   if(closing){message="Ожидание отмены предыдущей защиты. Новая пара пока не создаётся.";return;}
   if(qty>0&&managedDirection==0){exitGate.Reset();executedExits.Clear();}
   if(qty>0&&managedDirection!=0&&knownDirection!=managedDirection)
   {automatic=manualTracking=false;closing=true;var old=new[]{stop,target}.Where(Live).ToArray();if(old.Length>0)selected.Cancel(old);message="Направление позиции сменилось: прежняя защита отменяется, авто отключено. Нужна повторная активация.";return;}
   if(qty==0)
   {
    beRequested=false;
    var candidates=new[]{stop,target}.Concat(companionTakes).Where(x=>x!=null).Distinct().ToArray();
    var filledLegs=candidates.Where(f=>f.OrderState==OrderState.Filled).Concat(executedExits).Distinct().ToArray();
    var orders=candidates.Where(x=>exitGate.CanCancel(x,Ready(x),!string.IsNullOrEmpty(x.Oco)&&filledLegs.Any(f=>!ReferenceEquals(f,x)&&f.Oco==x.Oco))).ToArray();
    foreach(var x in orders)exitGate.SentCancel(x);
    closing=orders.Length>0;if(orders.Length>0)selected.Cancel(orders);
    if(!candidates.Any(Live)){stop=target=null;group="";managedDirection=0;companionTakes.Clear();hasStop=hasTarget=false;}
    manualTracking=false;message="Позиция закрыта. Ожидается завершение исполнений/OCO; повторные отмены не отправляются.";return;
   }
   // After a confirmed scale-out the filled leg may be smaller than the remaining account position.
   // Never reuse it or its OCO id. The 300 ms execution/position settling fence above runs first.
   if(stop!=null&&stop.OrderState==OrderState.Filled)stop=null;
   if(target!=null&&target.OrderState==OrderState.Filled)target=null;
   if(!automatic&&!manualTracking)return;
   try
   {
    writing=true;
    if(automatic)
    {
     Order[] other;lock(selected.Orders)other=selected.Orders.Where(x=>!Own(x)&&x.Instrument.FullName==Instrument.FullName&&Live(x)&&ClosingSide(x)).ToArray();
     var others=other.Where(x=>x.OrderType==OrderType.StopMarket||x.OrderType==OrderType.StopLimit).ToArray();
     if(others.Length>0)
     {
      if(!Live(stop)&&others.Length==1&&others[0].OrderType==OrderType.StopMarket&&Ready(others[0]))
      {stop=others[0];executionFills[stop]=stop.Filled;managedDirection=knownDirection;}
      else{message="Проверьте другие стопы MNQ: новая защита не дублируется.";return;}
     }
     companionTakes.Clear();companionTakes.AddRange(other.Where(x=>x.OrderType==OrderType.Limit));
    }
    if(automatic&&(!Live(stop)||(AutoProfitTarget&&!Live(target))))
    {

     double p=RiskMath.Price(average,qty,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RiskBudget,RoundTripCommission);
     double t=RiskMath.Target(average,qty,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RiskBudget,RoundTripCommission);
     if(!ValidStop(p)||(AutoProfitTarget&&!ValidTarget(t)))return;
     group=ExitLifecycleGate.NewOcoGroup();
     var create=new List<Order>();OrderAction action=knownDirection==1?OrderAction.Sell:OrderAction.BuyToCover;
     if(!Live(stop)){stop=selected.CreateOrder(Instrument,action,OrderType.StopMarket,OrderEntry.Automated,TimeInForce.Gtc,qty,0,p,group,"MNQPanel.Stop",NinjaTrader.Core.Globals.MaxDate,null);create.Add(stop);}
     if(AutoProfitTarget&&!Live(target)){target=selected.CreateOrder(Instrument,action,OrderType.Limit,OrderEntry.Automated,TimeInForce.Gtc,qty,t,0,group,"MNQPanel.Target",NinjaTrader.Core.Globals.MaxDate,null);create.Add(target);}
     managedDirection=knownDirection;plannedStop=p;plannedTarget=t;hasStop=true;hasTarget=AutoProfitTarget;lastChange=DateTime.UtcNow;selected.Submit(create);message="Автостоп отправлен; ожидается подтверждение брокера.";return;
    }
    var change=new List<Order>();
    if(Ready(stop))
    {
     double p=automatic?RiskMath.NeverLoosen(stop.StopPrice,RiskMath.Price(average,qty,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RiskBudget,RoundTripCommission),knownDirection):stop.StopPrice;
     int total=qty+stop.Filled;
     if((Math.Abs(stop.StopPrice-p)>TickSize*.1||stop.Quantity!=total)&&ValidStop(p))
     {stop.StopPriceChanged=p;stop.QuantityChanged=total;change.Add(stop);plannedStop=p;}
    }
    if(Ready(target))
    {
     double t=automatic&&AutoProfitTarget?RiskMath.Target(average,qty,knownDirection,Instrument.MasterInstrument.PointValue,TickSize,RiskBudget,RoundTripCommission):target.LimitPrice;
     if(target.Quantity!=qty+target.Filled||Math.Abs(target.LimitPrice-t)>TickSize*.1){target.QuantityChanged=qty+target.Filled;target.LimitPriceChanged=t;change.Add(target);plannedTarget=t;}
    }
    if(change.Count>0){lastChange=DateTime.UtcNow;selected.Change(change);message="Пересчёт защиты по текущему размеру позиции; ожидается подтверждение.";}
   }
   catch(Exception ex){automatic=false;message="Ошибка защиты: "+ex.Message;}
   finally{writing=false;}
  }
  protected override void OnBarUpdate()
  {
   if(State!=State.Realtime)return;
   bid=GetCurrentBid();ask=GetCurrentAsk();last=Close[0];apiQuoteTime=DateTime.UtcNow;
  }
  protected override void OnRender(ChartControl chart,ChartScale scale)
  {
   priceScale=scale;
   if(RenderTarget==null||ChartBars==null)return;
   try
   {
    foreach(var m in marks)
    {
     int bar=Bars.GetBar(m.Time);if(bar<ChartBars.FromIndex||bar>ChartBars.ToIndex)continue;
     float x=chart.GetXByBarIndex(ChartBars,bar),y=scale.GetYByValue(m.Price);
     using(var ink=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,m.Side==1?new SharpDX.Color4(.1f,.4f,1,1):new SharpDX.Color4(.9f,.1f,.7f,1)))
     {
      if(m.Exit)RenderTarget.DrawLine(new SharpDX.Vector2(x-5,y),new SharpDX.Vector2(x+5,y),ink,2);
      if(m.Entry)
      {
       float tip=y+(m.Side==1?5:-5),baseY=tip+(m.Side==1?6:-6);
       using(var geometry=new SharpDX.Direct2D1.PathGeometry(NinjaTrader.Core.Globals.D2DFactory))
       {using(var sink=geometry.Open()){sink.BeginFigure(new SharpDX.Vector2(x,tip),SharpDX.Direct2D1.FigureBegin.Filled);sink.AddLine(new SharpDX.Vector2(x-4,baseY));sink.AddLine(new SharpDX.Vector2(x+4,baseY));sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);sink.Close();}RenderTarget.FillGeometry(geometry,ink);}
      }
     }
    }
   }catch{}
  }
 }
}
