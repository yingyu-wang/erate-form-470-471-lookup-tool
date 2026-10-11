using System.Net;
using System.Text;
using ERate470Lookup.Core;
using Microsoft.Data.Sqlite;

static class PortfolioChecks
{
    public static async Task RunAsync()
    {
        static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
        var folder=Path.Combine(Path.GetTempPath(),"ERatePortfolio-"+Guid.NewGuid());
        try
        {
            var store=new PortfolioStore(folder);var list=store.CreateList("Territory");store.SaveEntry(list.Id,new("00123","District, One","West"));
            var reopened=new PortfolioStore(folder);
            Check(reopened.Entries(list.Id).Single().Ben=="00123","Watchlist preserves text BEN and survives restart");
            bool duplicate=false;try{store.SaveEntry(list.Id,new("00123","Duplicate",""));}catch(InvalidOperationException){duplicate=true;}
            Check(duplicate,"Duplicate watchlist BEN rejected");
            store.SaveEntry(list.Id,new("00123","Updated","East"),true);store.RenameList(list.Id,"Renamed");
            Check(store.Entries(list.Id).Single().Name=="Updated"&&store.Lists().Any(l=>l.Name=="Renamed"),"Watchlist rename and entry update");
            var key=new LookupKey("00123",2027,FilingForm.Form470);var now=DateTimeOffset.UtcNow;
            Filing Filing(string number,string service="Wireless")=>new(number,key.Ben,"District","Nickname","2026-10-01",service,"https://example.org/form.pdf","Certified","2026-10-29T00:00:00.000");
            var seq1=store.BeginObservation();var seq2=store.BeginObservation();
            Check(store.SaveLive(key,seq2,new(new(){Filing("2")},now,false)),"Complete live observation saved");
            Check(!store.SaveLive(key,seq1,new(new(){Filing("1")},now.AddSeconds(-1),false))&&store.Read(key)!.Filings.Single().Number=="2","Older overlapping observation cannot regress baseline");
            Check(store.Read(key)!.Changes.Count==0,"First observation creates no false new alerts");
            Check(!store.SaveLive(key,store.BeginObservation(),new(new(){Filing("3")},now,true)),"Cached result cannot advance baseline");
            store.SaveLive(key,store.BeginObservation(),new(new(){Filing("2","Switches"),Filing("3")},now.AddSeconds(1),false));
            Check(store.Read(key)!.Changes.Select(c=>c.Kind).Order().SequenceEqual(new[]{"Changed","New"}),"New and changed filings compared by application identity");
            Check(PortfolioStore.Compare(new[]{Filing("1","Wireless; Switches")},new[]{Filing("1","Switches; Wireless; Wireless")}).Count==0,"Service order and duplicates do not create changes");
            store.SaveLive(key,store.BeginObservation(),new(new(),now.AddSeconds(2),false));
            Check(store.Read(key)!.Changes.All(c=>c.Kind=="No longer returned by this query"),"Missing filings not inferred withdrawn");
            var unread=store.UnreadChanges(key);var lastSequence=unread.Max(e=>e.Sequence);
            store.SaveLive(key,store.BeginObservation(),new(new(),now.AddSeconds(3),false));
            Check(store.UnreadChanges(key).Count==unread.Count,"Repeated refresh retains unread changes without duplicate events");
            store.Acknowledge(key,lastSequence);Check(store.UnreadChanges(key).Count==0,"Acknowledgement is independent of the saved baseline");
            store.RemoveEntry(list.Id,key.Ben);store.DeleteList(list.Id);
            Check(store.Read(key) is not null,"Removing list and membership preserves saved history");
            var parsed=PortfolioSession.ParseCsv("\uFEFFBEN,Name,Tags\r\n00123,\"District, One\",West\r\n00123,Duplicate,\r\nbad,Invalid,\r\n456,\"A \"\"quoted\"\" name\",East\r\n");
            Check(parsed.Entries.Count==2&&parsed.Errors.Count==2&&parsed.Entries[1].Name=="A \"quoted\" name","CSV quoting, duplicate and invalid row preview");
            var imported=store.CreateList("Imports");
            Check(store.ImportEntries(imported.Id,parsed.Entries)==2&&store.ImportEntries(imported.Id,parsed.Entries)==0,"Atomic import skips existing BENs");
            bool invalidImport=false;try{store.ImportEntries(imported.Id,new[]{new WatchlistEntry("700","Valid",""),new WatchlistEntry("bad","Invalid","")});}catch(ArgumentException){invalidImport=true;}
            Check(invalidImport&&!store.Entries(imported.Id).Any(e=>e.Ben=="700"),"Invalid import rolls back all new entries");
            bool badHeader=false;try{PortfolioSession.ParseCsv("BEN,BEN\n1,2");}catch(ArgumentException){badHeader=true;}
            Check(badHeader,"Ambiguous CSV headers rejected");
            bool oversized=false;try{PortfolioSession.ParseCsv(new string('x',1024*1024+1));}catch(ArgumentException){oversized=true;}
            Check(oversized,"Oversized imports rejected");
            bool maxQueries=false;try{PortfolioSession.Queries(Enumerable.Range(1,500).Select(n=>new WatchlistEntry(n.ToString(),"","")),new[]{2025,2026},2);}catch(ArgumentException){maxQueries=true;}
            Check(maxQueries,"Batch expanded-query limit enforced");
            using var handler=new PortfolioHandler();using var http=new HttpClient(handler);var client=new UsacClient(http,Path.Combine(folder,"cache"));var session=new PortfolioSession(client,store);
            var keys=new[]{new LookupKey("1",2027,FilingForm.Form470),new LookupKey("2",2027,FilingForm.Form470),new LookupKey("3",2027,FilingForm.Form470)};
            await session.RefreshAsync(keys,null,default);
            Check(session.Rows.Count==3&&session.Rows.Any(r=>r.Outcome=="Failed")&&session.Rows.Any(r=>r.Outcome=="SuccessEmpty")&&session.Rows.Any(r=>r.Outcome=="Success"),"Mixed batch failure, empty and success remain distinct");
            session.LoadSaved(new[]{keys[0]});Check(session.Rows.Single().Note.StartsWith("Saved / offline"),"Complete saved snapshots open offline");
            var row=session.Rows.Single();session.Selected.Add(row.Id);
            Check(session.Filter("district","wireless").Count==1&&session.Filter("district","switch").Count==0&&session.Selected.Count==1,"Filters combine and preserve hidden selection");
            Check(session.ExportRows().Count==2&&session.ExportRows()[1][4]=="1","Selected export contains the intended query identity");
            var count=handler.Count;using var canceled=new CancellationTokenSource();canceled.Cancel();await session.RefreshAsync(keys,null,canceled.Token);
            Check(handler.Count==count&&session.Rows.All(r=>r.Outcome=="Not started"),"Canceled batch does not start pending requests");
            Check(PortfolioInsights.ContractDate(Filing("1"))=="2026-10-29"&&PortfolioInsights.ContractDate(Filing("1") with{AllowableContractDate="n/a"})=="Unknown","Source contract dates preserve date precision and missing values");
            Check(!PortfolioSession.ShareText(row with{Filing=Filing("1") with{Pdf="file:///secret"}}).Contains("file:///"),"Share text excludes non-HTTPS links");
            var inFlight=store.BeginObservation();store.ClearHistory();Check(store.Read(key) is null&&store.Lists().Count>0,"Clear history preserves watchlists");
            Check(!store.SaveLive(key,inFlight,new(new(){Filing("9")},now,false)),"An in-flight lookup cannot undo Clear saved history");
            using(var retryHandler=new RetryHandler())using(var retryHttp=new HttpClient(retryHandler))
            {
                var retryClient=new UsacClient(retryHttp,Path.Combine(folder,"retry"));await retryClient.GetFilingsAsync("1",2027,true,default);
                Check(retryHandler.Count==3,"Rate-limited GET honors bounded retry attempts");
            }
            var future=Path.Combine(folder,"future");Directory.CreateDirectory(future);
            using(var db=new SqliteConnection("Data Source="+Path.Combine(future,"portfolio.sqlite3"))){db.Open();using var c=db.CreateCommand();c.CommandText="PRAGMA user_version=99";c.ExecuteNonQuery();}
            bool futureRejected=false;try{_ = new PortfolioStore(future);}catch(InvalidOperationException){futureRejected=true;}
            Check(futureRejected,"Future-schema portfolio is refused without overwrite");
        }
        finally{SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
    }
    sealed class PortfolioHandler:HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Count++;var query=Uri.UnescapeDataString(request.RequestUri!.Query);
            if(query.Contains("billed_entity_number='2'"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            var body=query.Contains("billed_entity_number='3'")?"[]":"""[{"application_number":"10","billed_entity_number":"1","billed_entity_name":"District","fcc_form_470_status":"Certified","function":"Wireless"}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
        }
    }
    sealed class RetryHandler:HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var response=new HttpResponseMessage(++Count<3?HttpStatusCode.TooManyRequests:HttpStatusCode.OK){Content=new StringContent("[]")};
            response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);return Task.FromResult(response);
        }
    }
}
