using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    private void OwnModal(Window window)
    {
        var owner=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w!=window && w.IsActive && w.IsVisible);
        if(owner is not null) window.Owner=owner;
    }

    private async Task ShowProfile(string? user)
    {
        var panel=Stack(); var title=Text("Профіль",24,"Text",true); panel.Children.Add(title);
        var filter=new ComboBox {ItemsSource=new[]{"Усі складності","Легка","Середня","Складна","Експерт"},SelectedIndex=0};panel.Children.Add(filter);
        var body=Stack(); panel.Children.Add(new ScrollViewer { Content=body, MaxHeight=490, VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        var window=Modal("Профіль",panel,690,750); OwnModal(window);
        int page=0; bool loading=false;
        var nav=new UniformGrid { Columns=3 }; panel.Children.Add(nav);
        var prev = Action("Попередня");
        var next = Action("Наступна");
        prev.Click += async (_, _) => { page--; await Load(); };
        next.Click += async (_, _) => { page++; await Load(); };
        nav.Children.Add(prev); nav.Children.Add(next); nav.Children.Add(Action("Закрити",()=>window.Close()));
        async Task Load()
        {
            if(loading)return; loading=true; prev.IsEnabled=next.IsEnabled=false;
            try
            {
                var p=await Request<PublicProfile>("profile?page="+page+(user is null?"":"&user="+Uri.EscapeDataString(user))+(filter.SelectedIndex>0?"&difficulty="+(Difficulty)(filter.SelectedIndex-1):""));
                if(!window.IsVisible)return;
                title.Text=p.User; body.Children.Clear();
                body.Children.Add(Text($"Ігор: {p.Games} • перемог: {p.Wins} • ідеальних: {p.PerfectWins} • балів: {p.Score:N0}",15));
                body.Children.Add(Text($"Серія: {p.CurrentStreak}/{p.BestStreak} • щоденних перемог: {p.DailyWins}",14,"Muted")); Gap(body);
                foreach(var s in p.ByDifficulty??[])body.Children.Add(Text($"{Level(s.Difficulty)}: {s.Wins}/{s.Games} • ідеальні {s.PerfectWins} • серія {s.CurrentStreak}/{s.BestStreak} • {s.Score:N0} балів",14));
                if(account is not null && user is not null){var links=await Request<List<FriendEntry>>("friends");if(!window.IsVisible)return;var link=links.FirstOrDefault(x=>x.User==p.User);AddFriendActions(body,p.User,link?.Status,async()=>await Load());}
                body.Children.Add(Text("Місячні кубки",18,"Text",true));
                var cups=new WrapPanel(); body.Children.Add(cups);
                foreach(var month in p.Cups) { var cup=Card(Text("🏆 "+month,18,"Accent",true),8); cup.Margin=new Thickness(3); cups.Children.Add(cup); }
                if(p.Cups.Count==0)body.Children.Add(Text("Кубків ще немає.",13,"Muted"));
                Gap(body); body.Children.Add(Text("Історія ігор",18,"Text",true));
                foreach(var r in p.History)
                {
                    string outcome=r.Outcome switch {"completed"=>"Перемога","defeated"=>"Поразка",_=>"Перервано"};
                    body.Children.Add(Text($"{r.Ended.ToLocalTime():g} • {(r.ChallengeDay is null?r.Source:"Щоденна "+r.ChallengeDay)} • {Level(r.Difficulty)} • {outcome} • {FormatTime(r.Seconds)} • {r.Score} балів",13));
                }
                if(p.History.Count==0)body.Children.Add(Text("Ігор ще немає.",13,"Muted"));
                prev.IsEnabled=page>0; next.IsEnabled=p.HasMore;
            }
            catch(Exception ex){body.Children.Clear();body.Children.Add(Text(ex.Message,14,"Wrong"));prev.IsEnabled=page>0;}
            finally{loading=false;}
        }
        filter.SelectionChanged+=async (_,_)=>{page=0;await Load();};
        window.Loaded+=async (_,_)=>await Load();
        window.ShowDialog(); await Task.CompletedTask;
    }

    private void AddFriendActions(StackPanel panel,string user,string? status,Func<Task> reload)
    {
        var actions=new WrapPanel();panel.Children.Add(actions);
        var choices=status switch {"incoming"=>new[]{("Прийняти","accept"),("Відхилити","decline")},
            "outgoing"=>new[]{("Скасувати запит","cancel")},"friend"=>new[]{("Видалити друга","remove")},_=>new[]{("Додати в друзі","send")}};
        foreach(var (label,action) in choices)
        {
            var b=Action(label,async()=>{try{await Request<SyncAck>("friends",new FriendRequest(user,action));await reload();}catch(Exception ex){Dialog("Друзі",ex.Message,false);}});
            b.Margin=new Thickness(3);actions.Children.Add(b);
        }
    }
    private async Task ShowFriends()
    {
        var panel=Stack();panel.Children.Add(Text("Друзі та пошук гравців",24,"Text",true));
        var search=new TextBox {MinWidth=250,Padding=new Thickness(6),Margin=new Thickness(0,6,0,6)};panel.Children.Add(search);
        var body=Stack();panel.Children.Add(new ScrollViewer{Content=body,MaxHeight=440,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var window=Modal("Друзі",panel,570);OwnModal(window);
        async Task Load()
        {
            body.Children.Clear();
            try
            {
                if(account is not null)
                {
                    var friends=await Request<List<FriendEntry>>("friends");
                    foreach(var status in new[]{"incoming","outgoing","friend"})
                    {
                        body.Children.Add(Text(status=="incoming"?"Вхідні запити":status=="outgoing"?"Надіслані запити":"Друзі",17,"Text",true));
                        foreach(var f in friends.Where(x=>x.Status==status)){body.Children.Add(Action(f.User,()=>_=ShowProfile(f.User)));AddFriendActions(body,f.User,f.Status,Load);}
                    }
                }
                if(search.Text.Trim().Length>=2)
                {
                    body.Children.Add(Text("Результати пошуку",17,"Text",true));
                    foreach(var name in await Request<List<string>>("players?q="+Uri.EscapeDataString(search.Text.Trim()),token:""))body.Children.Add(Action(name,()=>_=ShowProfile(name)));
                }
            }
            catch(Exception ex){body.Children.Add(Text(ex.Message,14,"Wrong"));}
        }
        panel.Children.Insert(2,Action("Знайти / оновити",()=>_=Load()));
        panel.Children.Add(Action("Закрити",()=>window.Close()));
        window.Loaded+=async (_,_)=>await Load();window.ShowDialog();await Task.CompletedTask;
    }

    private async Task ShowChallenges()
    {
        if(account is null){ShowAccount();return;}
        string? month=null;
        while(account is not null && !closing)
        {
            string? selectedDay=null; bool claim=false;
            var panel=Stack(); var title=Text("Щоденні ігри",24,"Text",true); panel.Children.Add(title);
            var nav=new UniformGrid { Columns=3 }; panel.Children.Add(nav);
            var previous = Action("‹"); var progress=Text("",16,"Accent",true); progress.TextAlignment=TextAlignment.Center;
            var next = Action("›"); nav.Children.Add(previous); nav.Children.Add(progress); nav.Children.Add(next);
            var grid=new UniformGrid { Columns=7 }; panel.Children.Add(grid);
            var cups=new WrapPanel(); panel.Children.Add(Text("Місячні кубки",17,"Text",true));panel.Children.Add(cups);
            var notice=Text("",13,"Muted");panel.Children.Add(notice);
            var window=Modal("Щоденні ігри",panel,560,760);
            var reward=Action("Отримати нагороду",()=> { claim=true; window.Close(); },primary:true);reward.Visibility=Visibility.Collapsed;panel.Children.Add(reward);
            var close=Action("Закрити",()=>window.Close());close.IsCancel=true;panel.Children.Add(close);
            bool loading=false;
            previous.Click += (_, _) => MoveMonth(-1);
            next.Click += (_, _) => MoveMonth(1);
            void MoveMonth(int delta)
            {
                if(loading||month is null)return;
                var d=DateOnly.ParseExact(month+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture).AddMonths(delta);
                month=d.ToString("yyyy-MM",CultureInfo.InvariantCulture);_=Load();
            }
            async Task Load()
            {
                if(loading)return;loading=true;previous.IsEnabled=next.IsEnabled=false;
                try
                {
                    var c=await Request<ChallengeCalendar>("challenge/calendar"+(month is null?"":"?month="+month));
                    if(!window.IsVisible)return;
                    month=c.Month; var first=DateOnly.ParseExact(month+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
                    title.Text=first.ToString("MMMM yyyy",CultureInfo.GetCultureInfo("uk-UA"));
                    progress.Text=$"{c.Days.Count(d=>d.Status=="won")}/{c.Days.Count}";
                    grid.Children.Clear();
                    foreach(var d in new[]{"Пн","Вт","Ср","Чт","Пт","Сб","Нд"})grid.Children.Add(Text(d,13,"Muted",true));
                    for(int i=0;i<((int)first.DayOfWeek+6)%7;i++)grid.Children.Add(new Border());
                    foreach(var d in c.Days)
                    {
                        var b=Action(int.Parse(d.Day[^2..]).ToString()+(d.Status=="won"?" ✓":""),()=>{selectedDay=d.Day;window.Close();});
                        b.Margin=new Thickness(2);b.MinHeight=38;b.IsEnabled=d.Status is "available" or "started" or "retry";
                        b.ToolTip=d.Status switch {"won"=>"Перемога","future"=>"Майбутній день","missed"=>"Пропущено","started"=>"Продовжити","retry"=>"Повторити",_=>"Почати"};
                        if(d.Status=="won"){b.Background=B("Same");b.Opacity=1;}else if(d.Status=="started")b.BorderBrush=B("Accent");
                        grid.Children.Add(b);
                    }
                    cups.Children.Clear();foreach(var cup in c.Cups){var tile=Card(Text("🏆 "+cup,16,"Accent",true),6);tile.Margin=new Thickness(3);cups.Children.Add(tile);}
                    if(c.Cups.Count==0)cups.Children.Add(Text("Кубків ще немає.",13,"Muted"));
                    previous.IsEnabled=first.Year>2000||first.Month>1;next.IsEnabled=c.Month!=c.Today[..7];
                    daily=await Request<DailyStatus>("daily");if(window.IsVisible)reward.Visibility=daily.Packs.Any(p=>p.Milestone=="challenge")?Visibility.Visible:Visibility.Collapsed;
                    notice.Text="";
                }
                catch(Exception ex){notice.Text=ex.Message;previous.IsEnabled=true;}
                finally{loading=false;}
            }
            window.Loaded+=async (_,_)=>await Load();window.ShowDialog();
            if(claim){challengeRewards=true;weeklyRewards=false;await ShowDaily();challengeRewards=false;continue;}
            if(selectedDay is null)break;
            await ShowChallengeGame(selectedDay);
        }
        Render();
    }

    private async Task ShowChallengeGame(string day)
    {
        var panel=Stack();panel.Children.Add(Text("Щоденна гра • "+day,22,"Text",true));
        var summary=Text("",16,"Text",true);panel.Children.Add(summary);
        var notice=Text("",13,"Muted");panel.Children.Add(notice);
        var grid=new UniformGrid { Columns=9, Rows=9, Width=414, Height=414, HorizontalAlignment=HorizontalAlignment.Center };
        var boardCells=new Button[81];panel.Children.Add(grid);
        var pad=new UniformGrid { Columns=9 };panel.Children.Add(pad);var padCells=new Button[9];
        var tools=new UniformGrid { Columns=4 };panel.Children.Add(tools);
        var window=Modal("Щоденна гра",new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto },560,Math.Min(850,SystemParameters.WorkArea.Height-30));
        OnlineGameView? game=null;bool working=false, note=false, allowClose=false, connected=true;
        int selected=0;var anchor=Stopwatch.StartNew();double reviveDeadline=0;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(200) };
        var heartbeat=new DispatcherTimer { Interval=TimeSpan.FromSeconds(6) };
        bool Active()=>connected&&!working&&game is { Paused:false,Completed:false,OutOfLives:false,Reward:null };
        void Select(int i){selected=i;Draw();}
        for(int i=0;i<81;i++){var b=Action("");b.FontSize=19;b.Padding=new Thickness(0);b.Margin=new Thickness(1);boardCells[i]=b;grid.Children.Add(b);}
        for(int d=1;d<=9;d++){var b=Action(d.ToString());b.Padding=new Thickness(1);padCells[d-1]=b;pad.Children.Add(b);}
        var erase = Action("Стерти"); var undo = Action("↶"); var redo = Action("↷");
        var notes = Action("Нотатки");
        var hint = Action("Підказка"); var candidates = Action("Кандидати");
        var pause = Action("Пауза");
        var auto = Action("Автозавершення");
        foreach(var b in new[]{erase,undo,redo,notes,hint,candidates,pause,auto}){b.Margin=new Thickness(2);tools.Children.Add(b);}
        var mainAction = Action("Почати", primary: true);panel.Children.Add(mainAction);
        var exit = Action("До календаря", () => window.Close()); panel.Children.Add(exit);
        for (int i = 0; i < boardCells.Length; i++)
        {
            int index = i;
            boardCells[i].Click += (_, _) => Select(index);
        }
        for (int i = 0; i < padCells.Length; i++)
        {
            int digit = i + 1;
            padCells[i].Click += async (_, _) => await Move("enter", digit);
        }
        erase.Click += async (_, _) => await Move("enter", 0);
        undo.Click += async (_, _) => await Move("undo");
        redo.Click += async (_, _) => await Move("redo");
        notes.Click += (_, _) => { note = !note; Draw(); };
        hint.Click += async (_, _) => await Aid("hint");
        candidates.Click += async (_, _) => await Aid("candidates");
        pause.Click += async (_, _) => await Move(game?.Paused == true ? "resume" : "pause");
        auto.Click += async (_, _) => await Move("auto-finish");
        mainAction.Click += async (_, _) => await Primary();
        void Clock()
        {
            if(game is null)return;
            int seconds=game.Seconds+(connected&&!game.Paused&&!game.Completed&&!game.OutOfLives&&game.Reward is null?(int)Math.Min(12,anchor.Elapsed.TotalSeconds):0);
            summary.Text=$"{FormatTime(seconds)} • {new string('♥',Math.Clamp(game.Lives,0,3))} • {game.CurrentScore} балів • помилки {game.Mistakes}";
            if(game.OutOfLives&&!game.FinalDefeat&&game.Reward is null)
            {
                int left=Math.Max(0,(int)Math.Ceiling(reviveDeadline-anchor.Elapsed.TotalMilliseconds/1000));
                notice.Text=$"Поразка • останній шанс: {left} с";mainAction.IsEnabled=connected&&!working&&left>0;
                if(left==0&&!working&&connected)_=Load();
            }
        }
        void Accept(OnlineGameView g)
        {
            game=g.Empty?null:g;connected=true;anchor.Restart();reviveDeadline=(game?.ReviveRemainingMs??0)/1000.0;
            if(game is not null)selected=Math.Clamp(game.Selected,0,80);
        }
        void Draw()
        {
            bool active=Active();bool visible=game is {Paused:false,Completed:false,OutOfLives:false}&&connected;
            grid.Visibility=game?.Completed==true||game?.FinalDefeat==true?Visibility.Collapsed:Visibility.Visible;
            for(int i=0;i<81;i++)
            {
                var b=boardCells[i];int value=visible?game!.Values[i]:0;
                b.Content=value!=0?value.ToString():visible&&game!.Notes[i]!=0?string.Join(" ",Enumerable.Range(1,9).Where(d=>(game.Notes[i]&(1<<d))!=0)):"";
                b.FontSize=value!=0?19:10;b.Background=B(i==selected&&visible?"Selected":visible&&game!.Wrong[i]?"WrongBg":"Panel");
                b.Foreground=B(visible&&game!.Wrong[i]?"Wrong":visible&&game!.Givens[i]!=0?"Given":"Accent");b.IsEnabled=active;
                b.BorderThickness=new Thickness(1,1,i%9%3==2?2:1,i/9%3==2?2:1);
            }
            for(int i=0;i<9;i++)
            {
                var g=new Grid {MinHeight=30};var number=Text((i+1).ToString(),19,"Text",true);number.HorizontalAlignment=HorizontalAlignment.Center;number.VerticalAlignment=VerticalAlignment.Center;
                var count=Text((game?.Remaining[i]??9).ToString(),10,"Muted");count.HorizontalAlignment=HorizontalAlignment.Right;count.VerticalAlignment=VerticalAlignment.Bottom;
                g.Children.Add(number);g.Children.Add(count);padCells[i].HorizontalContentAlignment=HorizontalAlignment.Stretch;padCells[i].Content=g;padCells[i].IsEnabled=active;
            }
            erase.IsEnabled=notes.IsEnabled=hint.IsEnabled=candidates.IsEnabled=active;undo.IsEnabled=active&&game!.CanUndo;redo.IsEnabled=active&&game!.CanRedo;
            hint.Content=$"Підказка ({game?.HintCredits??0})";candidates.Content=$"Кандидати ({game?.CandidateCredits??0})";notes.Content=note?"Нотатки ✓":"Нотатки";
            auto.Content=game?.AutoFinish==true?"Авто ✓":"Авто";auto.IsEnabled=connected&&!working&&game is {Completed:false,OutOfLives:false};
            pause.Content=game?.Paused==true?"Продовжити":"Пауза";pause.IsEnabled=connected&&!working&&game is {Completed:false,OutOfLives:false,Reward:null};
            mainAction.Content=!connected?"Відновити з’єднання":game is null?"Почати":game.Completed?"До календаря":game.FinalDefeat?"Повторити":game.OutOfLives?"Останній шанс":game.Paused?"Продовжити":"";
            mainAction.Visibility=active?Visibility.Collapsed:Visibility.Visible;mainAction.IsEnabled=!working;
            if(connected)notice.Text=game is null?"":game.Completed?"Головоломку розв’язано!":game.FinalDefeat?"Поразка":game.Paused?"Гра на паузі":"";
            Clock();
        }
        async Task Load(bool pauseSession=false)
        {
            if(working)return;working=true;int oldSelected=selected;bool existing=game is not null;
            try{Accept(await Request<OnlineGameView>("web/session?challengeDay="+day+(pauseSession?"&pause=true":"")));if(existing)selected=oldSelected;}
            catch(Exception ex){connected=false;notice.Text=ex.Message;}
            finally{working=false;if(window.IsVisible)Draw();}
        }
        async Task Start()
        {
            if(working||!connected)return;working=true;Draw();
            try{Accept(await Request<OnlineGameView>("web/new",new{difficulty=Difficulty.Medium,id=game?.Id,revision=game?.Revision??0,autoFinish=state.AutoFinish,challengeDay=day}));}
            catch(Exception ex){notice.Text=ex.Message;connected=false;}
            finally{working=false;if(window.IsVisible)Draw();}
        }
        async Task<bool> Move(string action,int digit=0,string? reward=null)
        {
            if(working||!connected||game is null)return false;
            working=true;Draw();int cell=selected;
            try
            {
                var g=await Request<OnlineGameView>("web/action",new{id=game.Id,revision=game.Revision,action,cell,digit,note=action=="auto-finish"?!game.AutoFinish:note,reward,challengeDay=day});
                Accept(g);if(action!="hint")selected=cell;return true;
            }
            catch(Exception ex){connected=false;notice.Text=ex.Message;return false;}
            finally{working=false;if(window.IsVisible)Draw();}
        }
        async Task Aid(string kind)
        {
            if(!Active())return;
            int count=kind=="hint"?game!.HintCredits:game!.CandidateCredits;
            if(count>0){await Move(kind);return;}
            try{daily=await Request<DailyStatus>("daily");}
            catch{notice.Text="Сервіс недоступний.";return;}
            if(WalletCount(kind)>0){await Move("wallet",reward:kind);return;}
            await Ad(kind);
        }
        async Task Ad(string kind)
        {
            if(!await Move("reward-start",reward:kind))return;
            if(game?.Reward is null)return;
            var body=Stack();body.Children.Add(Text("Демонстраційна реклама",22,"Text",true));
            var remaining=Text("",24,"Accent",true);body.Children.Add(remaining);
            var ad=Modal("Винагорода",body,360);ad.Owner=window;
            var clock=Stopwatch.StartNew();int length=game.Reward.Remaining;
            var claim=Action("Отримати",()=>ad.DialogResult=true,primary:true);claim.IsEnabled=false;body.Children.Add(claim);
            var cancel=Action("Скасувати",()=>ad.DialogResult=false);cancel.IsCancel=true;body.Children.Add(cancel);
            var tick=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
            tick.Tick+=(_,_)=>{int left=Math.Max(0,(int)Math.Ceiling(length-clock.Elapsed.TotalSeconds));remaining.Text=left+" с";claim.IsEnabled=left==0;};
            tick.Start();bool accepted=ad.ShowDialog()==true;tick.Stop();
            await Move(accepted?"reward-claim":"reward-cancel",reward:kind);
        }
        async Task Primary()
        {
            if(!connected){await Load(true);return;}if(game is null||game.FinalDefeat){await Start();return;}
            if(game.Completed){window.Close();return;}if(game.OutOfLives){await Ad("life");return;}if(game.Paused)await Move("resume");
        }
        timer.Tick+=(_,_)=>Clock();
        heartbeat.Tick+=async (_,_)=>{if(game is {Paused:false,Completed:false,OutOfLives:false,Reward:null}&&!working&&connected)await Load();};
        window.Loaded+=async (_,_)=>{await Load(true);if(game?.Reward is not null)await Move("reward-cancel",reward:game.Reward.Kind);if(game is null&&connected)await Start();timer.Start();heartbeat.Start();};
        window.Closing+=async (_,e)=>{if(allowClose)return;if(working){e.Cancel=true;return;}if(game is {Paused:false,Completed:false,OutOfLives:false}&&connected){e.Cancel=true;await Move("pause");allowClose=true;window.Close();}};
        window.Closed+=(_,_)=>{timer.Stop();heartbeat.Stop();};
        window.PreviewKeyDown+=(_,e)=>
        {
            if(!Active())return;
            int digit=e.Key>=Key.D1&&e.Key<=Key.D9?(int)e.Key-(int)Key.D0:e.Key>=Key.NumPad1&&e.Key<=Key.NumPad9?(int)e.Key-(int)Key.NumPad0:0;
            if(digit>0)_=Move("enter",digit);
            else if(e.Key is Key.Delete or Key.Back)_=Move("enter",0);
            else if(e.Key==Key.Left)Select(Math.Max(0,selected-1));
            else if(e.Key==Key.Right)Select(Math.Min(80,selected+1));
            else if(e.Key==Key.Up)Select(Math.Max(0,selected-9));
            else if(e.Key==Key.Down)Select(Math.Min(80,selected+9));
            else if(e.Key==Key.N){note=!note;Draw();}
            else if(e.Key==Key.P)_=Move("pause");
            else return;
            e.Handled=true;
        };
        Draw();window.ShowDialog();await Task.CompletedTask;
    }
}
