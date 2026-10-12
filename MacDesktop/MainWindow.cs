using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ERate470Lookup.Core;
namespace ERate470Lookup.MacDesktop;

public sealed class MainWindow:Window
{
    private readonly TextBox customer=new(){PlaceholderText="BEN or California customer name",MinWidth=280};
    private readonly ComboBox year=new(){Width=95};
    private readonly ComboBox form=new(){Width=115};
    private readonly ComboBox theme=new(){Width=135};
    private readonly Button search=Button("Search"),refresh=Button("Refresh live"),choose=Button("Check selected customer");
    private readonly Button open=Button("Open selected PDF"),export=Button("Export selected CSV / Excel");
    private readonly Button saveBen=Button("Add BEN to watchlist");
    private readonly Button saveMatches=Button("Add selected BENs to watchlist");
    private readonly TextBlock matchSelection=Text("");
    private readonly Button all=Button("Select all"),none=Button("Unselect all"),sync=Button("Refresh CA directory / budgets"),windowInfo=Button("Form 471 window info");
    private readonly TextBlock status=Text("Enter a BEN or customer name. Funding year defaults to 2027.");
    private readonly TextBlock budget=Text("Loading California data…"),windowNotice=Text(""),selection=Text("0 selected"),detail=Text("");
    private readonly ListBox matches=new(){Height=120,SelectionMode=SelectionMode.Multiple};
    private readonly StackPanel matchPanel=new(){Spacing=8,IsVisible=false};
    private readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Extended,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,MinHeight=180};
    private readonly Border budgetCard=new(){CornerRadius=new CornerRadius(14),Padding=new Thickness(16)};
    private readonly Grid root=new(){RowDefinitions=new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto,Auto"),Margin=new Thickness(24)};
    private readonly List<Button> buttons=[];
    private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(30)};
    private readonly CancellationTokenSource lifetime=new();
    private readonly DispatcherTimer typing=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly UsacClient client;
    private readonly CaliforniaDirectory directory;
    private readonly FilingWindow windows;
    private readonly string portfolioDirectory;
    private string selectedName="";
    private readonly string themePath;
    private Bitmap? backdrop;
    private List<Filing> results=[];
    private string selectedBen="";
    private int selectedYear=2027;
    private FilingForm selectedForm=FilingForm.Form470;
    private bool busy,syncing,complete,force;
    private bool? hasCycle470;
    private DateTimeOffset fetched;
    private bool ready;
    private readonly DataGridTextColumn numberColumn=new(){Header="Form 470",Binding=new Binding("Number"),Width=new DataGridLength(120)};
    private readonly DataGridTextColumn servicesColumn=new(){Header="Requested services",Binding=new Binding("Services"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)};

    public MainWindow(bool refreshOnOpen = true)
    {
        Title="E-rate · Form 470 / 471 Lookup";Width=1200;Height=850;MinWidth=900;MinHeight=650;
        FontFamily=new FontFamily(OperatingSystem.IsLinux()?"DejaVu Sans":"Arial");FontSize=14;
        var cache=OperatingSystem.IsMacOS()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","ERateLookup","cache"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ERateLookup","cache");
        portfolioDirectory=Path.GetDirectoryName(cache)!;
        client=new(http,cache);directory=new(http,cache);directory.LoadSeedIfEmpty();windows=new(http,cache);
        themePath=Path.Combine(Path.GetDirectoryName(cache)!,"theme.txt");
        using(var asset=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("mint-glass-background.png"))if(asset is not null)backdrop=new Bitmap(asset);
        year.ItemsSource=Enumerable.Range(2016,12).Reverse().ToList();year.SelectedItem=2027;
        form.ItemsSource=new[]{"Form 470","Form 471"};form.SelectedIndex=0;
        theme.ItemsSource=new[]{"Mint Glass","Midnight","Classic"};
        var saved="Mint Glass";try{if(File.Exists(themePath))saved=File.ReadAllText(themePath).Trim();}catch(IOException){}catch(UnauthorizedAccessException){}
        theme.SelectedItem=new[]{"Mint Glass","Midnight","Classic"}.Contains(saved)?saved:"Mint Glass";
        Add(new TextBlock{Text="Has your customer filed?",FontSize=32,FontWeight=FontWeight.Bold,Margin=new Thickness(0,0,0,18)},0);
        var input=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),Margin=new Thickness(0,0,0,10)};
        var field=new StackPanel{Spacing=5};field.Children.Add(Text("BEN or customer name · California"));field.Children.Add(customer);input.Children.Add(field);
        AddInput(input,year,"Funding year",1);AddInput(input,form,"Form",2);
        Grid.SetColumn(search,3);search.VerticalAlignment=VerticalAlignment.Bottom;input.Children.Add(search);
        Grid.SetColumn(refresh,4);refresh.VerticalAlignment=VerticalAlignment.Bottom;input.Children.Add(refresh);Add(input,1);
        status.FontWeight=FontWeight.Bold;status.Margin=new Thickness(0,0,0,10);Add(status,2);
        budgetCard.Child=budget;budgetCard.Margin=new Thickness(0,0,0,10);Add(budgetCard,3);
        matchPanel.Children.Add(Text($"Select districts to save · {(OperatingSystem.IsMacOS()?"Command (⌘)":"Ctrl")}/Shift for multiple"));matchPanel.Children.Add(matches);
        var matchActions=new WrapPanel();matchActions.Children.Add(choose);matchActions.Children.Add(saveMatches);matchActions.Children.Add(matchSelection);matchPanel.Children.Add(matchActions);Add(matchPanel,4);
        grid.Columns.Add(numberColumn);
        grid.Columns.Add(new DataGridTextColumn{Header="Certified date",Binding=new Binding("Certified"),Width=new DataGridLength(125)});
        grid.Columns.Add(new DataGridTextColumn{Header="Status",Binding=new Binding("Status"),Width=new DataGridLength(145)});
        grid.Columns.Add(new DataGridTextColumn{Header="Customer",Binding=new Binding("Name"),Width=new DataGridLength(270)});
        grid.Columns.Add(servicesColumn);Add(grid,5);Add(detail,6);
        var actions=new WrapPanel{Margin=new Thickness(0,10,0,8)};
        var portfolio=Button("Saved BENs / Watchlists");actions.Children.Add(portfolio);
        portfolio.Click+=async(_,_)=>{try{await new PortfolioWindow(client,Path.GetDirectoryName(cache)!,directory,windows).ShowDialog(this);}catch(Exception ex){await MessageAsync(ex.Message);}};
        actions.Children.Add(saveBen);
        saveBen.Click+=async(_,_)=>await AddBenToWatchlistAsync();
        foreach(var b in new[]{open,all,none,export,sync,windowInfo}){buttons.Add(b);actions.Children.Add(b);}
        selection.Margin=new Thickness(8,12,8,0);actions.Children.Add(selection);actions.Children.Add(Text("Theme"));actions.Children.Add(theme);Add(actions,7);
        Add(Text($"Filing cache: 2 hours. Refresh live bypasses it. Names search the local California directory.\nHold {(OperatingSystem.IsMacOS()?"Command (⌘)":"Ctrl")} to select individual rows; Shift selects a range. USAC data may lag EPC submissions."),8);Add(windowNotice,9);
        Content=root;buttons.AddRange(new[]{search,refresh,choose});ApplyTheme();ready=true;
        search.Click+=async(_,_)=>await SearchAsync(false);refresh.Click+=async(_,_)=>await SearchAsync(true);choose.Click+=async(_,_)=>await ChooseAsync();
        matches.DoubleTapped+=async(_,_)=>await ChooseAsync();
        matches.SelectionChanged+=(_,_)=>UpdateMatchSelection();saveMatches.Click+=async(_,_)=>await AddMatchesToWatchlistAsync();
        customer.TextChanged+=(_,_)=>{typing.Stop();if(!busy){Reset("Customer changed. Search again.");typing.Start();}};typing.Tick+=(_,_)=>{typing.Stop();Suggest();};
        customer.KeyDown+=async(_,e)=>{if(e.Key==Avalonia.Input.Key.Enter){e.Handled=true;await SearchAsync(false);}};
        year.SelectionChanged+=async(_,_)=>{if(!ready||busy)return;Reset("Funding year changed. Search again.");await WindowNoticeAsync(true);};
        form.SelectionChanged+=async(_,_)=>{if(!ready||busy)return;selectedForm=form.SelectedIndex==1?FilingForm.Form471:FilingForm.Form470;numberColumn.Header=$"Form {(int)selectedForm}";servicesColumn.Header=selectedForm==FilingForm.Form471?"Service category":"Requested services";Reset("Form type changed. Search again.");await WindowNoticeAsync(true);};
        grid.SelectionChanged+=(_,_)=>UpdateSelection();all.Click+=(_,_)=>{grid.SelectedItems.Clear();foreach(var row in results)grid.SelectedItems.Add(row);};none.Click+=(_,_)=>grid.SelectedItems.Clear();
        open.Click+=async(_,_)=>await OpenAsync();grid.DoubleTapped+=async(_,_)=>await OpenAsync();export.Click+=async(_,_)=>await ExportAsync();sync.Click+=async(_,_)=>await SyncAsync(true);windowInfo.Click+=async(_,_)=>await WindowNoticeAsync(true);
        theme.SelectionChanged+=(_,_)=>{if(!ready)return;ApplyTheme();try{File.WriteAllText(themePath,theme.SelectedItem as string??"Mint Glass");}catch(IOException){}catch(UnauthorizedAccessException){}};
        Opened+=async(_,_)=>{budget.Text="California directory / C2 snapshot: "+directory.LastUpdated;if(refreshOnOpen&&directory.NeedsRefresh)await SyncAsync(false);};
        Closed+=(_,_)=>{typing.Stop();lifetime.Cancel();http.Dispose();backdrop?.Dispose();};UpdateSelection();
    }
    private static Button Button(string text)=>new(){Content=text,Padding=new Thickness(12,8),Margin=new Thickness(0,0,8,6)};
    private static TextBlock Text(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap};
    private void Add(Control control,int row){Grid.SetRow(control,row);root.Children.Add(control);}
    private static void AddInput(Grid grid,Control control,string label,int column)
    {var p=new StackPanel{Spacing=5,Margin=new Thickness(10,0,0,0)};p.Children.Add(Text(label));p.Children.Add(control);Grid.SetColumn(p,column);grid.Children.Add(p);}
    private void ApplyTheme()
    {
        var name=theme.SelectedItem as string??"Mint Glass";var dark=name=="Midnight";
        RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;
        Foreground=Brush.Parse(dark?"#EDF5FA":"#173E35");
        Background=name=="Mint Glass"&&backdrop is not null?new ImageBrush(backdrop){Stretch=Stretch.UniformToFill}:Brush.Parse(dark?"#172630":"#F5F6F7");
        budgetCard.Background=Brush.Parse(dark?"#DC253C48":name=="Classic"?"#FFFFFF":"#DDF4FCF8");
        budgetCard.BorderBrush=Brush.Parse(dark?"#587482":"#C0DCD0");budgetCard.BorderThickness=new Thickness(1);
        budget.FontWeight=FontWeight.Bold;budget.FontSize=15;
    }
    private void Reset(string message)
    {complete=false;results=[];grid.ItemsSource=null;selectedBen="";selectedName="";hasCycle470=null;matches.ItemsSource=null;matchPanel.IsVisible=false;status.Text=message;budget.Text="Choose a customer to view its C2 budget.";UpdateSelection();UpdateMatchSelection();}
    private void UpdateSelection()
    {
        selection.Text=$"{grid.SelectedItems.Count} of {results.Count} selected";
        detail.Text=(grid.SelectedItem as Filing)?.Nickname??"";
        all.IsEnabled=none.IsEnabled=!busy&&complete&&results.Count>0;
        export.IsEnabled=!busy&&complete;open.IsEnabled=!busy&&grid.SelectedItems.Count>0;
        saveBen.IsEnabled=!busy&&selectedBen.Length>0;
    }
    private void SetBusy(bool value)
    {busy=value;customer.IsEnabled=year.IsEnabled=form.IsEnabled=search.IsEnabled=refresh.IsEnabled=matches.IsEnabled=!value;grid.IsEnabled=!value;UpdateSelection();UpdateMatchSelection();}
    private void UpdateMatchSelection()
    {var count=matches.SelectedItems?.Count??0;matchSelection.Text=$"{count} selected";saveMatches.IsEnabled=!busy&&matchPanel.IsVisible&&count>0;choose.IsEnabled=!busy&&matchPanel.IsVisible&&count==1;}
    private async Task ExecuteAsync(Func<Task> action)
    {
        if(busy)return;SetBusy(true);
        try{await action();}
        catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text="Lookup timed out. No filing determination was made.";}
        catch(Exception ex){if(!lifetime.IsCancellationRequested)status.Text="Lookup failed — no filing determination was made. "+ex.Message;}
        finally{if(!lifetime.IsCancellationRequested)SetBusy(false);}
    }
    private void Suggest()
    {
        if(busy)return;var text=customer.Text?.Trim()??"";force=false;
        if(text.Length<2||text.All(char.IsAsciiDigit)){matchPanel.IsVisible=false;return;}
        try{var found=directory.Search(text);matches.ItemsSource=found;matchPanel.IsVisible=found.Count>0;UpdateMatchSelection();status.Text=found.Count==0?"No local match. Enter the filing BEN or try another spelling.":$"{found.Count} local match(es). Select districts to save, or one customer to look up."+(found.Count==15?" Type more letters to narrow the list.":"");}
        catch(Exception ex){status.Text="Local search failed: "+ex.Message;}
    }
    private async Task SearchAsync(bool refresh)
    {
        if(busy)return;var text=customer.Text?.Trim()??"";
        if(text.Length==0||(!text.All(char.IsAsciiDigit)&&text.Length<2)){await MessageAsync("Enter a BEN or at least two characters of a customer name.");return;}
        force=refresh;selectedYear=(int)year.SelectedItem!;typing.Stop();Reset("Searching…");
        await ExecuteAsync(async()=>
        {
            if(text.All(char.IsAsciiDigit)){await LookupAsync(text);return;}
            var found=directory.Search(text);matches.ItemsSource=found;matchPanel.IsVisible=found.Count>0;status.Text=found.Count==0?"No local California match. Try a shorter name or enter a BEN.":"Select districts to save, or one customer to look up filings.";
        });
    }
    private async Task ChooseAsync()
    {if(busy||matches.SelectedItems?.Count!=1||matches.SelectedItem is not Entity entity)return;selectedYear=(int)year.SelectedItem!;matchPanel.IsVisible=false;await ExecuteAsync(()=>LookupAsync(entity.Ben,entity.Name));}
    private async Task LookupAsync(string ben,string name="")
    {
        complete=false;results=[];grid.ItemsSource=null;selectedBen=ben;selectedName=name;hasCycle470=null;ShowBudget();status.Text=$"Checking Form {(int)selectedForm}, BEN {ben}, FY{selectedYear}…";
        var result=await client.GetFilingsAsync(ben,selectedYear,selectedForm,force,lifetime.Token);
        results=result.Data;fetched=result.FetchedAt;complete=true;grid.ItemsSource=results;foreach(var row in results)grid.SelectedItems.Add(row);
        var note="";
        if(selectedYear>=2026)
        {
            try{hasCycle470=(await client.HasNewCycleForm470Async(ben,force,lifetime.Token)).Data;}
            catch(Exception) when(!lifetime.IsCancellationRequested){note=" New-cycle check unavailable; budget status comes from USAC.";}
        }
        ShowBudget();UpdateSelection();status.Text=(results.Count>0?$"FILED — {results.Count} certified Form {(int)selectedForm} application(s)":$"No certified Form {(int)selectedForm} found in currently published data")+$" for BEN {ben}, FY{selectedYear}. {(result.FromCache?"Cached":"Live query")}; fetched {fetched.LocalDateTime:g}."+note;
    }
    private void ShowBudget()
    {
        var b=directory.GetBudget(selectedBen,selectedYear);
        if(b is null){budget.Text=$"C2 budget · BEN {selectedBen}\nNot reported in the downloaded budget data. This is not a zero budget.\nDirectory updated: {directory.LastUpdated}";return;}
        var forecast=BudgetRow.ForecastView(b.Version,hasCycle470);
        var note=hasCycle470==false?"No certified Form 470 found for 2026 or 2027. ":"";
        if(decimal.TryParse(b.Available,NumberStyles.Number,CultureInfo.InvariantCulture,out var value)&&value<0)note+="Negative remaining balance: review with USAC. ";
        budget.Text=$"{(forecast?"Forecast C2 budget":"C2 budget")} · {b.Cycle} · USAC status: {b.Version} · {b.Name}\nTotal: {BudgetRow.Money(b.Total)}     Committed: {BudgetRow.Money(b.Committed)}     Pending: {BudgetRow.Money(b.Pending)}\n{(forecast?"Forecast remaining (not confirmed)":"Reported available")}: {BudgetRow.Money(b.Available)}\n{note}Pre-discount figures; committed is not disbursed. Form 470 does not confirm a budget. Updated: {directory.LastUpdated}";
    }
    private async Task SyncAsync(bool manual)
    {
        if(syncing)return;syncing=true;sync.IsEnabled=false;
        try{await directory.RefreshAsync(lifetime.Token);if(selectedBen.Length>0)ShowBudget();else budget.Text="California directory / C2 data refreshed: "+directory.LastUpdated;if(manual&&!busy)status.Text="California data refreshed successfully.";}
        catch(Exception ex){if(!lifetime.IsCancellationRequested)budget.Text="Directory refresh failed; existing data retained. Updated: "+directory.LastUpdated+"\n"+ex.Message;}
        finally{syncing=false;sync.IsEnabled=true;}
    }
    private async Task WindowNoticeAsync(bool popup)
    {
        if(selectedForm!=FilingForm.Form471){windowNotice.Text="";return;}
        var requested=(int)year.SelectedItem!;
        try{var schedule=await windows.GetAsync(requested,lifetime.Token);if(selectedForm!=FilingForm.Form471||(int)year.SelectedItem! !=requested||lifetime.IsCancellationRequested)return;windowNotice.Text=schedule.Message(DateTimeOffset.UtcNow);if(popup&&await MessageAsync(windowNotice.Text,"Open USAC page"))OpenUrl(schedule.Source);}
        catch(Exception){if(!lifetime.IsCancellationRequested)windowNotice.Text="Could not verify filing-window dates. Check USAC's official announcements; lookups remain available.";}
    }
    private async Task OpenAsync()
    {
        if(grid.SelectedItem is not Filing filing)return;
        if(!Uri.TryCreate(filing.Pdf,UriKind.Absolute,out var uri)||uri.Scheme!="https"){await MessageAsync("USAC did not provide a usable PDF link.");return;}
        try{OpenUrl(uri.AbsoluteUri);}catch(Exception ex){await MessageAsync("Could not open the PDF: "+ex.Message);}
    }
    private static void OpenUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    private async Task AddBenToWatchlistAsync()
    {
        if(selectedBen.Length==0){await MessageAsync("Identify a BEN first — search by name or enter a BEN, then run a lookup.");return;}
        try
        {
            var suggested=selectedName.Length>0?selectedName:directory.GetBudget(selectedBen,selectedYear)?.Name??"";
            await SaveToWatchlistAsync([new(selectedBen,suggested,"")],false);
        }
        catch(Exception ex){await MessageAsync(ex.Message);}
    }
    private async Task AddMatchesToWatchlistAsync()
    {
        if(busy||!matchPanel.IsVisible)return;
        var entries=matches.SelectedItems?.OfType<Entity>().DistinctBy(e=>e.Ben).Select(e=>new WatchlistEntry(e.Ben,e.Name,"")).ToList()??[];
        if(entries.Count==0)return;
        typing.Stop();await SaveToWatchlistAsync(entries,true);
    }
    private async Task SaveToWatchlistAsync(IReadOnlyList<WatchlistEntry> entries,bool skipExisting)
    {
        try
        {
            var portfolioStore=new PortfolioStore(portfolioDirectory);
            var watchlists=portfolioStore.Lists();
            if(watchlists.Count==0){await MessageAsync("Create a watchlist in Saved BENs / Watchlists first.");return;}
            var dialog=new Window{Title=entries.Count==1?$"Add BEN {entries[0].Ben} to watchlist":$"Add {entries.Count} BENs to watchlist",Width=520,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var panel=new StackPanel{Margin=new Thickness(20),Spacing=10};
            var picker=new ComboBox{ItemsSource=watchlists,SelectedIndex=watchlists.Count>0?0:-1};
            var nameBox=new TextBox{Text=entries[0].Name,PlaceholderText="Display name (optional)"};
            var tagsBox=new TextBox{PlaceholderText="Tags (optional)"};
            var error=Text("");error.Foreground=Brushes.DarkRed;
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
            var save=Button("Save");var cancel=Button("Cancel");var added=0;
            save.Click+=(_,_)=>{
                try
                {
                    if(picker.SelectedItem is not Watchlist list)throw new InvalidOperationException("No watchlist available.");
                    var pending=entries.Select(e=>e with{Name=entries.Count==1?nameBox.Text??"":e.Name,Tags=tagsBox.Text??""}).ToList();
                    if(skipExisting)added=portfolioStore.ImportEntries(list.Id,pending);
                    else{portfolioStore.SaveEntry(list.Id,pending[0]);added=1;}
                    dialog.Close(true);
                }
                catch(Exception ex){error.Text=ex.Message;}
            };
            cancel.Click+=(_,_)=>dialog.Close(false);
            buttons.Children.Add(save);buttons.Children.Add(cancel);
            panel.Children.Add(Text("Watchlist"));panel.Children.Add(picker);
            if(entries.Count==1){panel.Children.Add(Text("Display name"));panel.Children.Add(nameBox);}
            else{panel.Children.Add(Text("Selected districts · existing BENs will be kept"));panel.Children.Add(new ListBox{ItemsSource=entries,Height=Math.Min(180,entries.Count*32)});}
            panel.Children.Add(Text("Tags"));panel.Children.Add(tagsBox);
            panel.Children.Add(error);panel.Children.Add(buttons);
            dialog.Content=panel;
            if(await dialog.ShowDialog<bool>(this))
                status.Text=$"{added} BEN(s) added; {entries.Count-added} already saved. Open Saved BENs / Watchlists to refresh them.";
        }
        catch(Exception ex){await MessageAsync(ex.Message);}
    }
    private async Task<bool> MessageAsync(string message,string? action=null)
    {
        var dialog=new Window{Title="E-rate Lookup",Width=620,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(20),Spacing=14};panel.Children.Add(Text(message));var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var close=Button("OK");close.Click+=(_,_)=>dialog.Close(false);buttons.Children.Add(close);
        if(action is not null){var go=Button(action);go.Click+=(_,_)=>dialog.Close(true);buttons.Children.Add(go);}panel.Children.Add(buttons);dialog.Content=panel;return await dialog.ShowDialog<bool>(this);
    }
    private async Task ExportAsync()
    {
        if(!complete)return;
        var selected=grid.SelectedItems.Cast<Filing>().Select(r=>r.Number).ToHashSet();var items=results.Where(r=>selected.Contains(r.Number)).ToList();
        if(results.Count>0&&items.Count==0){await MessageAsync($"Select at least one filing. Use {(OperatingSystem.IsMacOS()?"Command":"Ctrl")}-click or Select all.");return;}
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Export selected filings",SuggestedFileName=$"Form{(int)selectedForm}_{selectedBen}_FY{selectedYear}.xlsx",DefaultExtension="xlsx",ShowOverwritePrompt=true,FileTypeChoices=new[]{new FilePickerFileType("Excel workbook"){Patterns=new[]{"*.xlsx"}},new FilePickerFileType("CSV file"){Patterns=new[]{"*.csv"}}}});
        if(file is null)return;
        try
        {
            if(!file.Path.IsFile)throw new IOException("Choose a local file destination.");
            var path=file.Path.LocalPath;var b=directory.GetBudget(selectedBen,selectedYear);var exported=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
            var rows=new List<string[]>{new[]{"ExportedUTC","LookupFetchedUTC","DirectoryUpdatedUTC","FundingYear","BEN","Customer","LookupResult","FormType","ApplicationNumber","FilingStatus","CertifiedDate","Nickname","Services","PDF","BudgetCycle","USACBudgetStatus","BudgetDisplayStatus","TotalC2BudgetPreDiscount","CommittedC2BudgetPreDiscount","PendingC2BudgetPreDiscount","ReportedRemainingC2BudgetPreDiscount"}};
            var state=b is null?"Not reported":BudgetRow.ForecastView(b.Version,hasCycle470)?"Forecast":b.Version;
            var output=results.Count>0?items:new List<Filing>{new("",selectedBen,b?.Name??customer.Text??"","","","","")};
            foreach(var r in output)rows.Add(new[]{exported,fetched.ToString("O",CultureInfo.InvariantCulture),directory.LastUpdated,selectedYear.ToString(CultureInfo.InvariantCulture),selectedBen,r.Name,results.Count>0?"Filed":$"No certified Form {(int)selectedForm} found in published data",((int)selectedForm).ToString(CultureInfo.InvariantCulture),r.Number,results.Count>0?r.Status:"",r.Certified,r.Nickname,r.Services,r.Pdf,b?.Cycle??"Not reported",b?.Version??"Not reported",state,b?.Total??"Not reported",b?.Committed??"Not reported",b?.Pending??"Not reported",b?.Available??"Not reported"});
            if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))DataExporter.WriteCsv(path,rows);else DataExporter.WriteExcel(path,rows);
            status.Text=$"Exported {items.Count} selected filing(s) to {path}";
        }
        catch(Exception ex){await MessageAsync("Export failed: "+ex.Message);}
        finally{file.Dispose();}
    }
}
