using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using ERate470Lookup.Core;
using Path=System.IO.Path;

namespace ERate470Lookup.AndroidApp;

[Activity(Label="Saved BENs / Watchlists",Exported=false,ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize|ConfigChanges.KeyboardHidden,WindowSoftInputMode=SoftInput.AdjustResize)]
public sealed class PortfolioActivity : Activity
{
    const int ImportRequest=601,ExportRequest=602;
    readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(45)};
    readonly List<View> gated=[];
    PortfolioSession session=null!;
    CaliforniaDirectory directory=null!;
    FilingWindow windows=null!;
    Spinner lists=null!,entries=null!,forms=null!;
    EditText listName=null!,ben=null!,name=null!,tags=null!,years=null!,keyword=null!,service=null!;
    CheckBox changes=null!;
    TextView status=null!,counts=null!;
    LinearLayout results=null!;
    List<Watchlist> listItems=[];
    List<WatchlistEntry> entryItems=[];
    List<PortfolioRow> visible=[];
    CancellationTokenSource? operation;
    string? pendingExport;
    bool destroyed;
    int page;
    string? ListId=>lists.SelectedItemPosition>=0&&lists.SelectedItemPosition<listItems.Count?listItems[lists.SelectedItemPosition].Id:null;
    protected override void OnCreate(Bundle? saved)
    {
        base.OnCreate(saved);
        var data=Path.Combine(FilesDir!.AbsolutePath,"ERateLookup");
        try{session=new(new UsacClient(http,data),new PortfolioStore(data));directory=new(http,data);directory.LoadSeedIfEmpty();windows=new(http,data);}
        catch(Exception ex){SetContentView(new TextView(this){Text="Cannot open saved portfolio: "+ex.Message});return;}
        var scroll=new ScrollView(this);var root=Column();root.SetPadding(20,40,20,40);scroll.AddView(root);SetContentView(scroll);
        scroll.SetOnApplyWindowInsetsListener(new PortfolioInsets());scroll.RequestApplyInsets();
        root.AddView(Label("Saved BENs / Watchlists",24));
        root.AddView(Label("Save districts, choose years/forms, and refresh when needed. Open saved works offline."));
        lists=Picker(root,"Watchlist",[]);listName=Field(root,"List name");
        root.AddView(Button("New list",()=>Do(()=>{var l=session.Store.CreateList(listName.Text??"");ReloadLists(l.Id);}))); 
        root.AddView(Button("Rename list",()=>Do(()=>{session.Store.RenameList(RequireList(),listName.Text??"");ReloadLists(ListId);}))); 
        root.AddView(Button("Delete list",async()=>{if(await Confirm("Delete this watchlist? Filing history is retained.")){session.Store.DeleteList(RequireList());ReloadLists();}}));
        entries=Picker(root,"Saved BEN",[]);ben=Field(root,"BEN",true);name=Field(root,"Display name");tags=Field(root,"Tags");
        root.AddView(Button("Save BEN",()=>Do(()=>{session.Store.SaveEntry(RequireList(),new(ben.Text??"",name.Text??"",tags.Text??""));ReloadEntries();})));
        root.AddView(Button("Update BEN",()=>Do(()=>{session.Store.SaveEntry(RequireList(),new(ben.Text??"",name.Text??"",tags.Text??""),true);ReloadEntries();})));
        root.AddView(Button("Remove BEN",()=>Do(()=>{session.Store.RemoveEntry(RequireList(),ben.Text?.Trim()??"");ReloadEntries();})));
        root.AddView(Button("Import BEN CSV",()=>Do(()=>{RequireList();var intent=new Intent(Intent.ActionOpenDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType("*/*");StartActivityForResult(intent,ImportRequest);})));
        years=Field(root,"Funding years (comma separated)");years.Text=(DateTime.Now.Year+1).ToString();forms=Picker(root,"Forms",new[]{"Form 470","Form 471","Both forms"});
        root.AddView(Button("Refresh watchlist",()=>RefreshAsync(false)));root.AddView(Button("Refresh selected BEN",()=>RefreshAsync(true)));
        root.AddView(Button("Open saved / offline",()=>Do(()=>{session.LoadSaved(Queries(false));page=0;Render();status.Text="Saved results; no network request made.";})));
        root.AddView(Button("Cancel",()=>Do(()=>operation?.Cancel()),false));
        keyword=Field(root,"Keyword");service=Field(root,"Service contains");changes=new CheckBox(this){Text="Changes only"};root.AddView(changes);gated.Add(changes);
        root.AddView(Button("Apply filters",()=>Do(()=>{page=0;Render();})));
        root.AddView(Button("Select visible",()=>Do(()=>{foreach(var r in visible)session.Selected.Add(r.Id);Render();})));
        root.AddView(Button("Clear selection",()=>Do(()=>{session.Selected.Clear();Render();})));
        counts=Label("");root.AddView(counts);status=Label("");status.SetTextIsSelectable(true);root.AddView(status);results=Column();root.AddView(results);gated.Add(results);
        root.AddView(Button("Previous",()=>Do(()=>{page=Math.Max(0,page-1);Render();})));root.AddView(Button("Next",()=>Do(()=>{page++;Render();})));
        root.AddView(Button("Export selected CSV",()=>PrepareExportAsync("csv")));root.AddView(Button("Export selected Excel",()=>PrepareExportAsync("xlsx")));
        root.AddView(Button("Deadlines",async()=>{var text=await PortfolioInsights.DeadlinesAsync(session,windows,operation!.Token);if(!destroyed)new AlertDialog.Builder(this)!.SetTitle("Source-backed dates")!.SetMessage(text)!.SetPositiveButton("Close",(_,_)=>{})!.Show();}));
        root.AddView(Button("History / summary",()=>Do(()=>new AlertDialog.Builder(this)!.SetTitle("District history")!.SetMessage(PortfolioInsights.Summary(session,directory))!.SetPositiveButton("Close",(_,_)=>{})!.Show())));
        root.AddView(Button("Clear saved history",async()=>{if(await Confirm("Delete all saved filing snapshots? Watchlists are retained.")){session.Store.ClearHistory();session.LoadSaved(Queries(false));Render();}}));
        lists.ItemSelected+=(_,_)=>{listName.Text=listItems.FirstOrDefault(l=>l.Id==ListId)?.Name??"";ReloadEntries();};
        entries.ItemSelected+=(_,_)=>{if(entries.SelectedItemPosition>=0&&entries.SelectedItemPosition<entryItems.Count){var e=entryItems[entries.SelectedItemPosition];ben.Text=e.Ben;name.Text=e.Name;tags.Text=e.Tags;}};
        var pending=saved?.GetString("pendingPortfolioExport");if(pending is not null&&Path.GetDirectoryName(pending)==CacheDir!.AbsolutePath&&File.Exists(pending))pendingExport=pending;
        ReloadLists(saved?.GetString("portfolioList"));
        years.Text=saved?.GetString("portfolioYears")??years.Text;
        if(saved is not null)forms.SetSelection(saved.GetInt("portfolioForms",0));
    }
    string RequireList()=>ListId??throw new InvalidOperationException("Create or select a watchlist first.");
    static Task Do(Action action){action();return Task.CompletedTask;}
    LinearLayout Column()=>new(this){Orientation=Orientation.Vertical};
    TextView Label(string text,float size=16)=>new(this){Text=text,TextSize=size};
    EditText Field(LinearLayout root,string label,bool numeric=false)
    {root.AddView(Label(label));var field=new EditText(this){ContentDescription=label,InputType=numeric?InputTypes.ClassNumber:InputTypes.ClassText};field.SetSingleLine(true);root.AddView(field);gated.Add(field);return field;}
    Spinner Picker(LinearLayout root,string label,string[] values)
    {root.AddView(Label(label));var view=new Spinner(this){ContentDescription=label};SetItems(view,values);root.AddView(view);gated.Add(view);return view;}
    void SetItems(Spinner picker,IEnumerable<string> values)
    {var adapter=new ArrayAdapter<string>(this,global::Android.Resource.Layout.SimpleSpinnerItem,values.ToArray());adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);picker.Adapter=adapter;}
    Button Button(string title,Func<Task> action,bool gate=true)
    {var button=new Button(this){Text=title,ContentDescription=title};button.SetAllCaps(false);button.Click+=async(_,_)=>{if(gate)await Run(action);else await action();};if(gate)gated.Add(button);return button;}
    void ReloadLists(string? selected=null)
    {listItems=session.Store.Lists();SetItems(lists,listItems.Select(l=>l.Name));lists.SetSelection(Math.Max(0,listItems.FindIndex(l=>l.Id==selected)));ReloadEntries();}
    void ReloadEntries(){entryItems=ListId is null?[]:session.Store.Entries(ListId);SetItems(entries,entryItems.Select(e=>e.ToString()));}
    List<LookupKey> Queries(bool single)=>PortfolioSession.Queries(single?entries.SelectedItemPosition>=0&&entries.SelectedItemPosition<entryItems.Count?new[]{entryItems[entries.SelectedItemPosition]}:throw new InvalidOperationException("Select a saved BEN first."):session.Store.Entries(RequireList()),PortfolioSession.ParseYears(years.Text??""),forms.SelectedItemPosition);
    async Task Run(Func<Task> action,bool allowPending=false)
    {
        if(destroyed||operation is not null||(pendingExport is not null&&!allowPending))return;
        using var current=new CancellationTokenSource();operation=current;foreach(var c in gated)c.Enabled=false;
        try{await action();}catch(Exception ex){if(!destroyed)status.Text=ex is OperationCanceledException?"Canceled. A selected export destination may contain an incomplete file.":ex.Message;}
        finally{operation=null;if(!destroyed)foreach(var c in gated)c.Enabled=true;}
    }
    async Task RefreshAsync(bool single)
    {var queries=Queries(single);var progress=new Progress<string>(s=>{if(!destroyed)status.Text=s;});await Task.Run(()=>session.RefreshAsync(queries,progress,operation!.Token));if(!destroyed){page=0;Render();}}
    void Render()
    {
        visible=session.Filter(keyword.Text??"",service.Text??"",changes.Checked);page=Math.Clamp(page,0,Math.Max(0,(visible.Count-1)/50));results.RemoveAllViews();
        foreach(var r in visible.Skip(page*50).Take(50))
        {
            var check=new CheckBox(this){Text=r.Display,Checked=session.Selected.Contains(r.Id)};check.CheckedChange+=(_,e)=>{if(e.IsChecked)session.Selected.Add(r.Id);else session.Selected.Remove(r.Id);UpdateCounts();};results.AddView(check);
            var share=new Button(this){Text="Share filing summary"};share.Click+=async(_,_)=>await Run(()=>Do(()=>{var intent=new Intent(Intent.ActionSend);intent.SetType("text/plain");intent.PutExtra(Intent.ExtraText,PortfolioSession.ShareText(r));StartActivity(Intent.CreateChooser(intent,"Share filing"));}));results.AddView(share);
            if(System.Uri.TryCreate(r.Filing?.Pdf,UriKind.Absolute,out var uri)&&uri.Scheme=="https")
            {var open=new Button(this){Text="Open application PDF"};open.Click+=async(_,_)=>await Run(()=>Do(()=>StartActivity(new Intent(Intent.ActionView,global::Android.Net.Uri.Parse(uri.AbsoluteUri)))));results.AddView(open);}
        }
        UpdateCounts();
    }
    void UpdateCounts()=>counts.Text=$"{session.Selected.Count} selected · {visible.Count}/{session.Rows.Count} visible · page {page+1}/{Math.Max(1,(visible.Count+49)/50)}";
    Task<bool> Confirm(string message)
    {
        var completion=new TaskCompletionSource<bool>();var dialog=new AlertDialog.Builder(this)!.SetTitle("Confirm")!.SetMessage(message)!.SetPositiveButton("Continue",(_,_)=>completion.TrySetResult(true))!.SetNegativeButton("Cancel",(_,_)=>completion.TrySetResult(false))!.Create();
        dialog!.DismissEvent+=(_,_)=>completion.TrySetResult(false);dialog.Show();return completion.Task;
    }
    async Task PrepareExportAsync(string extension)
    {
        var rows=session.ExportRows();var path=Path.Combine(CacheDir!.AbsolutePath,"portfolio-"+Guid.NewGuid().ToString("N")+"."+extension);
        try
        {
            await Task.Run(()=>{if(extension=="csv")DataExporter.WriteCsv(path,rows,operation!.Token);else DataExporter.WriteExcel(path,rows,operation!.Token);});operation!.Token.ThrowIfCancellationRequested();
            pendingExport=path;var intent=new Intent(Intent.ActionCreateDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType(extension=="csv"?"text/csv":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");intent.PutExtra(Intent.ExtraTitle,"E-rate-portfolio."+extension);StartActivityForResult(intent,ExportRequest);
        }
        catch{pendingExport=null;throw;}
        finally{if(pendingExport!=path)File.Delete(path);}
    }
    protected override async void OnActivityResult(int requestCode,Result resultCode,Intent? data)
    {
        base.OnActivityResult(requestCode,resultCode,data);
        if(requestCode==ImportRequest&&resultCode==Result.Ok&&data?.Data is { } source)
            await Run(async()=>
            {
                var id=RequireList();using var input=ContentResolver!.OpenInputStream(source)??throw new IOException("Cannot read CSV.");using var output=new MemoryStream();
                var buffer=new byte[8192];int count;while((count=await input.ReadAsync(buffer,operation!.Token))>0){if(output.Length+count>1024*1024)throw new ArgumentException("CSV exceeds 1 MiB.");output.Write(buffer,0,count);}
                var preview=PortfolioSession.ParseCsv(System.Text.Encoding.UTF8.GetString(output.ToArray()));
                if(!await Confirm($"Import {preview.Entries.Count} valid BENs?\n{string.Join("\n",preview.Errors.Take(10))}\nExisting BENs skipped; extra columns ignored."))return;
                var existing=session.Store.Entries(id).Select(e=>e.Ben).ToHashSet();var added=0;foreach(var e in preview.Entries.Where(e=>!existing.Contains(e.Ben))){session.Store.SaveEntry(id,e);added++;}ReloadEntries();status.Text=$"Imported {added} BENs.";
            });
        if(requestCode==ExportRequest&&pendingExport is string path)
            await Run(async()=>
            {
                try
                {
                    if(resultCode==Result.Ok&&data?.Data is { } destination){using var input=File.OpenRead(path);using var output=ContentResolver!.OpenOutputStream(destination,"wt")??throw new IOException("Cannot write destination.");await AndroidBehavior.CopyExportAsync(input,output,operation!.Token);if(!destroyed)status.Text="Selected results exported.";}
                    else if(!destroyed)status.Text="Export canceled.";
                }
                finally{File.Delete(path);pendingExport=null;}
            },true);
    }
    protected override void OnSaveInstanceState(Bundle state)
    {state.PutString("pendingPortfolioExport",pendingExport);state.PutString("portfolioList",ListId);state.PutString("portfolioYears",years.Text);state.PutInt("portfolioForms",forms.SelectedItemPosition);base.OnSaveInstanceState(state);}
    protected override void OnDestroy(){destroyed=true;operation?.Cancel();http.Dispose();base.OnDestroy();}
    sealed class PortfolioInsets:Java.Lang.Object,View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view,WindowInsets? insets)
        {
            if(view is not null&&insets is not null)
            {
                if(Build.VERSION.SdkInt>=BuildVersionCodes.R){var bars=insets.GetInsets(WindowInsets.Type.SystemBars()|WindowInsets.Type.Ime());view.SetPadding(bars.Left,bars.Top,bars.Right,bars.Bottom);}
                else view.SetPadding(insets.SystemWindowInsetLeft,insets.SystemWindowInsetTop,insets.SystemWindowInsetRight,insets.SystemWindowInsetBottom);
            }
            return insets!;
        }
    }
}
