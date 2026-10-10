using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace ERate470Lookup.Core;

public enum FilingForm { Form470 = 470, Form471 = 471 }

public sealed class UsacRow
{
    [JsonPropertyName("application_number")] public string Number { get; set; } = "";
    [JsonPropertyName("billed_entity_number")] public string Ben { get; set; } = "";
    [JsonPropertyName("billed_entity_name")] public string Name { get; set; } = "";
    [JsonPropertyName("billed_entity_city")] public string City { get; set; } = "";
    [JsonPropertyName("billed_entity_state")] public string State { get; set; } = "";
    [JsonPropertyName("certified_date_time")] public string Certified { get; set; } = "";
    [JsonPropertyName("last_modified_date_time")] public string Modified { get; set; } = "";
    [JsonPropertyName("form_version")] public string Version { get; set; } = "";
    [JsonPropertyName("form_nickname")] public string Nickname { get; set; } = "";
    [JsonPropertyName("service_category")] public string Category { get; set; } = "";
    [JsonPropertyName("service_type")] public string Service { get; set; } = "";
    [JsonPropertyName("function")] public string Function { get; set; } = "";
    [JsonPropertyName("filing_status")] public string Status { get; set; } = "";
    [JsonPropertyName("pdf_url")] public string PdfUrl { get; set; } = "";
    [JsonPropertyName("form_pdf")] public UrlField? Pdf { get; set; }
}
public sealed class UrlField { [JsonPropertyName("url")] public string Url { get; set; } = ""; }
public sealed record Entity(string Ben, string Name, string City, string State)
{
    public override string ToString() => $"{Name} | BEN {Ben} | {City}, {State}";
}
public sealed record Filing(string Number, string Ben, string Name, string Nickname, string Certified, string Services, string Pdf, string Status = "Certified");
public sealed record QueryResult<T>(T Data, DateTimeOffset FetchedAt, bool FromCache);
public sealed record EntityResults(List<Entity> Matches, bool Limited);
public sealed record CacheEntry(DateTimeOffset FetchedAt, List<UsacRow> Rows);

public sealed class UsacClient
{
    public const string Endpoint = "https://opendata.usac.org/resource/jt8s-3q52.json";
    public const string Form471Endpoint = "https://opendata.usac.org/resource/9s6i-myen.json";
    private readonly HttpClient http;
    private readonly string connectionString;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan ttl = TimeSpan.FromHours(2);
    public UsacClient(HttpClient http, string cacheDirectory, Func<DateTimeOffset>? clock=null)
    {
        this.http=http; this.clock=clock??(()=>DateTimeOffset.UtcNow); Directory.CreateDirectory(cacheDirectory);
        connectionString=new SqliteConnectionStringBuilder{DataSource=Path.Combine(cacheDirectory,"cache.sqlite3"),Pooling=false}.ToString();
        using var db=new SqliteConnection(connectionString);db.Open();
        using var command=db.CreateCommand();command.CommandText="CREATE TABLE IF NOT EXISTS query_cache (query_key TEXT PRIMARY KEY, fetched_utc TEXT NOT NULL, payload_json TEXT NOT NULL)";command.ExecuteNonQuery();
    }
    public static string Literal(string text) => "'" + text.Replace("'", "''") + "'";
    public static string EncodeQuery(IReadOnlyDictionary<string, string> parameters) => string.Join("&", parameters.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value)));
    public static string FilingFilter(string ben, int year)
    {
        if (ben.Length==0 || !ben.All(char.IsAsciiDigit)) throw new ArgumentException("BEN must contain digits only.");
        if (year<2016 || year>2100) throw new ArgumentException("Invalid funding year.");
        return $"billed_entity_number={Literal(ben)} AND funding_year={Literal(year.ToString(CultureInfo.InvariantCulture))} AND fcc_form_470_status='Certified'";
    }
    public static string FilingFilter(string ben, int year, FilingForm form)
    {
        var filter = FilingFilter(ben, year); // Validates BEN and year.
        return form switch
        {
            FilingForm.Form470 => filter,
            FilingForm.Form471 => $"epc_organization_id={Literal(ben)} AND funding_year={Literal(year.ToString(CultureInfo.InvariantCulture))} AND certified_datetime IS NOT NULL",
            _ => throw new ArgumentException("Unsupported form type.")
        };
    }
    public async Task<QueryResult<List<UsacRow>>> QueryAsync(Dictionary<string,string> parameters, bool refresh, CancellationToken ct, string endpoint = Endpoint)
    {
        var query=EncodeQuery(parameters);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint+"?"+query)));
        if(!refresh)
        {
            using var db=new SqliteConnection(connectionString);await db.OpenAsync(ct);
            using var command=db.CreateCommand();command.CommandText="SELECT fetched_utc,payload_json FROM query_cache WHERE query_key=$key";command.Parameters.AddWithValue("$key",hash);
            using var reader=await command.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))
            {
                if(DateTimeOffset.TryParse(reader.GetString(0),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var cachedFetched))
                {
                    var age=clock()-cachedFetched;
                    if(age>=TimeSpan.Zero && age<ttl)
                    {
                        try { var cachedRows=JsonSerializer.Deserialize<List<UsacRow>>(reader.GetString(1));if(cachedRows is not null)return new(cachedRows,cachedFetched,true); }
                        catch(JsonException) { /* Corrupt cached payload is refreshed. */ }
                    }
                }
            }
        }
        using var response=await http.GetAsync(endpoint+"?"+query, ct);
        if (response.StatusCode==HttpStatusCode.TooManyRequests) throw new HttpRequestException("USAC is limiting requests. Please try again shortly.");
        response.EnsureSuccessStatusCode();
        using var stream=await response.Content.ReadAsStreamAsync(ct);
        var rows=await JsonSerializer.DeserializeAsync<List<UsacRow>>(stream,cancellationToken:ct) ?? throw new JsonException("USAC returned an empty response body.");
        var fetched=clock();
        using(var db=new SqliteConnection(connectionString))
        {
            await db.OpenAsync(ct);using var command=db.CreateCommand();
            command.CommandText="INSERT INTO query_cache(query_key,fetched_utc,payload_json) VALUES($key,$time,$json) ON CONFLICT(query_key) DO UPDATE SET fetched_utc=excluded.fetched_utc,payload_json=excluded.payload_json";
            command.Parameters.AddWithValue("$key",hash);command.Parameters.AddWithValue("$time",fetched.ToString("O",CultureInfo.InvariantCulture));command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(rows));
            await command.ExecuteNonQueryAsync(ct);
        }
        return new(rows,fetched,false);
    }
    public async Task<QueryResult<EntityResults>> FindEntitiesAsync(string name, string state, bool refresh, CancellationToken ct)
    {
        var fields="billed_entity_number,billed_entity_name,billed_entity_city,billed_entity_state";
        var where=$"contains(upper(billed_entity_name),{Literal(name.ToUpperInvariant())})";
        if(!string.IsNullOrWhiteSpace(state)) where+=$" AND billed_entity_state={Literal(state.ToUpperInvariant())}";
        var response=await QueryAsync(new() { ["$select"]=fields,["$group"]=fields,["$where"]=where,["$order"]="billed_entity_name",["$limit"]="101" }, refresh,ct);
        var matches=response.Data.DistinctBy(r=>r.Ben).Take(100).Select(r=>new Entity(r.Ben,r.Name,r.City,r.State)).ToList();
        return new(new(matches,response.Data.Count>100),response.FetchedAt,response.FromCache);
    }
    public async Task<QueryResult<List<Filing>>> GetFilingsAsync(string ben, int year, bool refresh, CancellationToken ct)
        => await GetFilingsAsync(ben,year,FilingForm.Form470,refresh,ct);
    public async Task<QueryResult<List<Filing>>> GetFilingsAsync(string ben, int year, FilingForm form, bool refresh, CancellationToken ct)
    {
        var filter=FilingFilter(ben,year,form);var all=new List<UsacRow>();var fetched=DateTimeOffset.MaxValue;var cached=true;
        for(var offset=0;offset<50000;offset+=1000)
        {
            var parameters=new Dictionary<string,string>{["$where"]=filter,["$order"]=form==FilingForm.Form470?"application_number,service_request_id,service_request_rfp_attachment":"application_number,form_version,last_modified_timestamp",["$limit"]="1000",["$offset"]=offset.ToString(CultureInfo.InvariantCulture)};
            if(form==FilingForm.Form471)
                parameters["$select"]="application_number,epc_organization_id as billed_entity_number,organization_name as billed_entity_name,org_city as billed_entity_city,org_state as billed_entity_state,certified_datetime as certified_date_time,last_modified_timestamp as last_modified_date_time,form_version,nickname as form_nickname,chosen_category_of_service as service_category,form_471_status_name as filing_status,file_url as pdf_url";
            var page=await QueryAsync(parameters,refresh,ct,form==FilingForm.Form471?Form471Endpoint:Endpoint);
            all.AddRange(page.Data);if(page.FetchedAt<fetched) fetched=page.FetchedAt;cached &= page.FromCache;
            if(page.Data.Count<1000) return new(Collapse(all),fetched,cached);
        }
        throw new InvalidOperationException("Result limit reached; a complete lookup could not be made.");
    }
    public async Task<QueryResult<bool>> HasNewCycleForm470Async(string ben,bool refresh,CancellationToken ct)
    {
        _=FilingFilter(ben,2027);
        var page=await QueryAsync(new(){["$select"]="application_number",["$where"]=$"billed_entity_number={Literal(ben)} AND funding_year in ('2026','2027') AND fcc_form_470_status='Certified'",["$limit"]="1"},refresh,ct);
        return new(page.Data.Count>0,page.FetchedAt,page.FromCache);
    }
    public static List<Filing> Collapse(IEnumerable<UsacRow> rows) => rows.GroupBy(r=>r.Number).Select(group=>
    {
        var current=group.Where(r=>r.Version=="Current").ToList();if(current.Count==0) current=group.ToList();
        var row=current.OrderByDescending(r=>r.Modified,StringComparer.Ordinal).First();
        var services=string.Join("; ",current.Select(r=>string.Join(" / ",new[] {r.Category,r.Service,r.Function}.Where(s=>!string.IsNullOrWhiteSpace(s)))).Distinct().OrderBy(s=>s,StringComparer.Ordinal));
        return new Filing(row.Number,row.Ben,row.Name,row.Nickname,row.Certified.Length>=10?row.Certified[..10]:row.Certified,services,row.Pdf?.Url??(row.PdfUrl.Length>0?row.PdfUrl:group.Select(r=>r.PdfUrl).FirstOrDefault(u=>u.Length>0)??""),string.IsNullOrWhiteSpace(row.Status)?"Certified":row.Status);
    }).OrderByDescending(r=>r.Certified,StringComparer.Ordinal).ToList();
}
