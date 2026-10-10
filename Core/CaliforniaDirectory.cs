using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
namespace ERate470Lookup.Core;
public sealed class DirectoryRow
{
    [JsonPropertyName("entity_number")] public string Number{get;set;}="";
    [JsonPropertyName("entity_name")] public string Name{get;set;}="";
    [JsonPropertyName("entity_type")] public string Type{get;set;}="";
    [JsonPropertyName("physical_city")] public string City{get;set;}="";
    [JsonPropertyName("physical_state")] public string State{get;set;}="";
}
public sealed class BudgetRow
{
    [JsonPropertyName("ben")] public string Ben{get;set;}="";
    [JsonPropertyName("billed_entity_name")] public string Name{get;set;}="";
    [JsonPropertyName("city")] public string City{get;set;}="";
    [JsonPropertyName("state")] public string State{get;set;}="";
    [JsonPropertyName("applicant_type")] public string Type{get;set;}="";
    [JsonPropertyName("c2_budget_cycle")] public string Cycle{get;set;}="";
    [JsonPropertyName("c2_budget_version")] public string Version{get;set;}="";
    [JsonPropertyName("c2_budget")] public string? Total{get;set;}
    [JsonPropertyName("funded_c2_budget_amount")] public string? Committed{get;set;}
    [JsonPropertyName("pending_c2_budget_amount")] public string? Pending{get;set;}
    [JsonPropertyName("available_c2_budget_amount")] public string? Available{get;set;}
    public static bool ForecastView(string version,bool? hasCycle470)=>version.Equals("Forecast",StringComparison.OrdinalIgnoreCase)||(hasCycle470==false&&!version.Equals("Confirmed",StringComparison.OrdinalIgnoreCase));
    public static string Money(string? raw)=>decimal.TryParse(raw,NumberStyles.Number,CultureInfo.InvariantCulture,out var value)?value.ToString("C2",CultureInfo.GetCultureInfo("en-US")):"Not reported";
}
public sealed class CaliforniaDirectory
{
    private readonly string connectionString;
    private readonly HttpClient http;
    public CaliforniaDirectory(HttpClient http,string cacheDirectory)
    {
        this.http=http;Directory.CreateDirectory(cacheDirectory);
        connectionString=new SqliteConnectionStringBuilder{DataSource=Path.Combine(cacheDirectory,"cache.sqlite3"),Pooling=false}.ToString();
        using var db=Open();using var c=db.CreateCommand();
        c.CommandText="CREATE TABLE IF NOT EXISTS entities(number TEXT PRIMARY KEY,name TEXT NOT NULL,name_upper TEXT NOT NULL,city TEXT,state TEXT,type TEXT);CREATE TABLE IF NOT EXISTS c2_budgets(ben TEXT,cycle TEXT,payload TEXT,PRIMARY KEY(ben,cycle));CREATE TABLE IF NOT EXISTS directory_meta(key TEXT PRIMARY KEY,value TEXT NOT NULL);";c.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var db=new SqliteConnection(connectionString);db.Open();return db;}
    public string LastUpdated
    {
        get{using var db=Open();using var c=db.CreateCommand();c.CommandText="SELECT value FROM directory_meta WHERE key='updated'";return c.ExecuteScalar() as string??"Not yet loaded";}
    }
    public bool NeedsRefresh=>!DateTimeOffset.TryParse(LastUpdated,out var date)||DateTimeOffset.UtcNow-date>=TimeSpan.FromDays(1)||date>DateTimeOffset.UtcNow;
    public void LoadSeedIfEmpty()
    {
        if(LastUpdated!="Not yet loaded")return;
        using var entities=Assembly.GetExecutingAssembly().GetManifestResourceStream("CaliforniaEntities.json")??throw new InvalidOperationException("Entity seed missing.");
        using var budgets=Assembly.GetExecutingAssembly().GetManifestResourceStream("CaliforniaC2.json")??throw new InvalidOperationException("Budget seed missing.");
        using var timestamp=Assembly.GetExecutingAssembly().GetManifestResourceStream("CaliforniaSnapshot.txt")??throw new InvalidOperationException("Seed timestamp missing.");
        using var reader=new StreamReader(timestamp);
        Replace(JsonSerializer.Deserialize<List<DirectoryRow>>(entities)??[],JsonSerializer.Deserialize<List<BudgetRow>>(budgets)??[],DateTimeOffset.Parse(reader.ReadToEnd().Trim(),CultureInfo.InvariantCulture));
    }
    public List<Entity> Search(string text)
    {
        using var db=Open();using var c=db.CreateCommand();
        c.CommandText="SELECT number,name,city,state,type FROM entities WHERE name_upper LIKE $name ESCAPE '\\' ORDER BY CASE WHEN type='School District' THEN 0 WHEN type='Library System' THEN 1 ELSE 2 END,name LIMIT 15";
        var escaped=text.ToUpperInvariant().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");c.Parameters.AddWithValue("$name","%"+escaped+"%");
        using var r=c.ExecuteReader();var result=new List<Entity>();while(r.Read())result.Add(new(r.GetString(0),r.GetString(1)+" ["+r.GetString(4)+"]",r.GetString(2),r.GetString(3)));return result;
    }
    public BudgetRow? GetBudget(string ben,int year)
    {
        var cycle=year>=2026&&year<=2030?"FY2026-2030":year>=2021&&year<=2025?"FY2021-2025":null;if(cycle is null)return null;
        using var db=Open();using var c=db.CreateCommand();c.CommandText="SELECT payload FROM c2_budgets WHERE ben=$ben AND cycle=$cycle";c.Parameters.AddWithValue("$ben",ben);c.Parameters.AddWithValue("$cycle",cycle);var payload=c.ExecuteScalar() as string;return payload is null?null:JsonSerializer.Deserialize<BudgetRow>(payload);
    }
    public async Task RefreshAsync(CancellationToken ct)
    {
        var entities=await DownloadAsync<DirectoryRow>("7i5i-83qf",new(){["$select"]="entity_number,entity_name,entity_type,physical_city,physical_state",["$where"]="physical_state='CA' AND status='Active'",["$order"]="entity_number"},ct);
        var budgets=await DownloadAsync<BudgetRow>("6brt-5pbv",new(){["$where"]="state='CA' AND c2_budget_cycle in ('FY2021-2025','FY2026-2030')",["$order"]="ben,c2_budget_cycle"},ct);
        if(entities.Count==0||budgets.Count==0)throw new InvalidOperationException("USAC returned an empty directory; existing local records were preserved.");
        await Task.Run(()=>Replace(entities,budgets,DateTimeOffset.UtcNow),ct);
    }
    private async Task<List<T>> DownloadAsync<T>(string dataset,Dictionary<string,string> parameters,CancellationToken ct)
    {
        var all=new List<T>();for(var offset=0;offset<100000;offset+=5000)
        {
            parameters["$limit"]="5000";parameters["$offset"]=offset.ToString(CultureInfo.InvariantCulture);
            using var response=await http.GetAsync("https://opendata.usac.org/resource/"+dataset+".json?"+UsacClient.EncodeQuery(parameters),ct);response.EnsureSuccessStatusCode();using var stream=await response.Content.ReadAsStreamAsync(ct);
            var rows=await JsonSerializer.DeserializeAsync<List<T>>(stream,cancellationToken:ct)??throw new JsonException("Unexpected directory response.");all.AddRange(rows);if(rows.Count<5000)return all;
        }
        throw new InvalidOperationException("Directory download exceeded the result limit; local records were preserved.");
    }
    public void Replace(List<DirectoryRow> entities,List<BudgetRow> budgets,DateTimeOffset fetched)
    {
        using var db=Open();using var transaction=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=transaction;c.CommandText="DELETE FROM entities; DELETE FROM c2_budgets;";c.ExecuteNonQuery();
        foreach(var r in entities.Where(r=>r.State=="CA"))InsertEntity(db,transaction,r.Number,r.Name,r.City,r.State,r.Type);
        foreach(var r in budgets.Where(r=>r.State=="CA"))
        {
            InsertEntity(db,transaction,r.Ben,r.Name,r.City,r.State,r.Type);
            using var command=db.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT OR REPLACE INTO c2_budgets VALUES($ben,$cycle,$payload)";command.Parameters.AddWithValue("$ben",r.Ben);command.Parameters.AddWithValue("$cycle",r.Cycle);command.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(r));command.ExecuteNonQuery();
        }
        c.CommandText="INSERT OR REPLACE INTO directory_meta VALUES('updated',$date)";c.Parameters.AddWithValue("$date",fetched.ToString("O",CultureInfo.InvariantCulture));c.ExecuteNonQuery();transaction.Commit();
    }
    private static void InsertEntity(SqliteConnection db,SqliteTransaction transaction,string number,string name,string city,string state,string type)
    {
        using var c=db.CreateCommand();c.Transaction=transaction;c.CommandText="INSERT OR IGNORE INTO entities VALUES($number,$name,$upper,$city,$state,$type)";
        c.Parameters.AddWithValue("$number",number);c.Parameters.AddWithValue("$name",name);c.Parameters.AddWithValue("$upper",name.ToUpperInvariant());c.Parameters.AddWithValue("$city",city);c.Parameters.AddWithValue("$state",state);c.Parameters.AddWithValue("$type",type);c.ExecuteNonQuery();
    }
}
