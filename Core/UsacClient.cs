using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace ERateLookup.Core;

public enum FilingForm
{
    Form470 = 470,
    Form471 = 471
}

public sealed class UsacRow
{
    [JsonPropertyName("application_number")] public string Number { get; set; } = "";
    [JsonPropertyName("billed_entity_number")] public string Ben { get; set; } = "";
    [JsonPropertyName("epc_organization_id")] public string EpcOrganizationId { get; set; } = "";
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
    [JsonPropertyName("fcc_form_470_status")] public string Status470 { get; set; } = "";
    [JsonPropertyName("allowable_contract_date")] public string AllowableContractDate { get; set; } = "";
    [JsonPropertyName("funding_request_amount")] public string RequestedAmount { get; set; } = "";
    [JsonPropertyName("pre_discount_eligible_amount")] public string PreDiscountAmount { get; set; } = "";
    [JsonPropertyName("pdf_url")] public string PdfUrl { get; set; } = "";
    [JsonPropertyName("funding_request_number")] public string FundingRequestNumber { get; set; } = "";
    [JsonPropertyName("form_pdf")]
    public UrlField? Pdf
    {
        get; set;
    }
}

public sealed class UrlField
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed record Entity(string Ben, string Name, string City, string State)
{
    public override string ToString() => $"{Name} | BEN {Ben} | {City}, {State}";
}

public sealed record Filing(
    string Number,
    string Ben,
    string Name,
    string Nickname,
    string Certified,
    string Services,
    string Pdf,
    string Status = "Certified",
    string AllowableContractDate = "",
    string RequestedAmount = "",
    string PreDiscountAmount = "");

public sealed record QueryResult<T>(T Data, DateTimeOffset FetchedAt, bool FromCache);
public sealed record EntityResults(List<Entity> Matches, bool Limited);
public sealed record CacheEntry(DateTimeOffset FetchedAt, List<UsacRow> Rows);

public sealed class UsacClient : IDisposable
{
    public const string Endpoint = "https://opendata.usac.org/resource/jt8s-3q52.json";
    public const string Form471Endpoint = "https://opendata.usac.org/resource/9s6i-myen.json";
    const string Form471Select =
        "application_number,epc_organization_id,organization_name as billed_entity_name,org_city as billed_entity_city,org_state as billed_entity_state,certified_datetime as certified_date_time,last_modified_timestamp as last_modified_date_time,form_version,nickname as form_nickname,chosen_category_of_service as service_category,form_471_status_name as filing_status,file_url as pdf_url,funding_request_amount,pre_discount_eligible_amount";

    readonly HttpClient http;
    readonly Func<DateTimeOffset> clock;
    readonly TimeSpan ttl = TimeSpan.FromHours(2);
    readonly SqliteConnection cacheConnection;
    readonly SemaphoreSlim cacheGate = new(1, 1);
    int disposed;

    public UsacClient(HttpClient http, string cacheDirectory, Func<DateTimeOffset>? clock = null)
    {
        this.http = http;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(cacheDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(cacheDirectory, "cache.sqlite3"),
            Pooling = false,
            DefaultTimeout = 15
        }.ToString();
        cacheConnection = new SqliteConnection(connectionString);
        cacheConnection.Open();
        using var command = cacheConnection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS query_cache (query_key TEXT PRIMARY KEY, fetched_utc TEXT NOT NULL, payload_json TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    public (int Current, int Next) NewCycleFundingYears()
    {
        var current = ERateCalendar.CurrentFundingYear(clock());
        return (current, current + 1);
    }

    public static string Literal(string text) => "'" + text.Replace("'", "''") + "'";

    public static string EncodeQuery(IReadOnlyDictionary<string, string> parameters) =>
        string.Join("&", parameters.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));

    public static string FilingFilter(string ben, int year)
    {
        if (ben.Length == 0 || !ben.All(char.IsAsciiDigit))
            throw new ArgumentException("BEN must contain digits only.");
        if (year < 2016 || year > 2100)
            throw new ArgumentException("Invalid funding year.");
        return $"billed_entity_number={Literal(ben)} AND funding_year={Literal(year.ToString(CultureInfo.InvariantCulture))} AND fcc_form_470_status='Certified'";
    }

    public static string FilingFilter(string ben, int year, FilingForm form)
    {
        var filter = FilingFilter(ben, year);
        return form switch
        {
            FilingForm.Form470 => filter,
            FilingForm.Form471 => $"epc_organization_id={Literal(ben)} AND funding_year={Literal(year.ToString(CultureInfo.InvariantCulture))} AND certified_datetime IS NOT NULL",
            _ => throw new ArgumentException("Unsupported form type.")
        };
    }

    public async Task<QueryResult<List<UsacRow>>> QueryAsync(Dictionary<string, string> parameters, bool refresh, CancellationToken ct, string endpoint = Endpoint)
    {
        var query = EncodeQuery(parameters);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint + "?" + query)));
        if (!refresh)
        {
            var cached = await ReadCacheAsync(hash, ct).ConfigureAwait(false);
            if (cached is not null)
                return cached;
        }

        using var response = await SendAsync(endpoint + "?" + query, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("USAC is limiting requests. Please try again shortly.");
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var rows = await JsonSerializer.DeserializeAsync<List<UsacRow>>(stream, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new JsonException("USAC returned an empty response body.");
        var fetched = clock();
        await WriteCacheAsync(hash, fetched, rows, ct).ConfigureAwait(false);
        return new QueryResult<List<UsacRow>>(rows, fetched, false);
    }

    async Task<QueryResult<List<UsacRow>>?> ReadCacheAsync(string hash, CancellationToken ct)
    {
        await cacheGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            PruneExpired();
            using var command = cacheConnection.CreateCommand();
            command.CommandText = "SELECT fetched_utc,payload_json FROM query_cache WHERE query_key=$key";
            command.Parameters.AddWithValue("$key", hash);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;
            if (!DateTimeOffset.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cachedFetched))
                return null;
            var age = clock() - cachedFetched;
            if (age < TimeSpan.Zero || age >= ttl)
                return null;
            try
            {
                var cachedRows = JsonSerializer.Deserialize<List<UsacRow>>(reader.GetString(1));
                return cachedRows is null ? null : new QueryResult<List<UsacRow>>(cachedRows, cachedFetched, true);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            cacheGate.Release();
        }
    }

    async Task WriteCacheAsync(string hash, DateTimeOffset fetched, List<UsacRow> rows, CancellationToken ct)
    {
        await cacheGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            using var command = cacheConnection.CreateCommand();
            command.CommandText = "INSERT INTO query_cache(query_key,fetched_utc,payload_json) VALUES($key,$time,$json) ON CONFLICT(query_key) DO UPDATE SET fetched_utc=excluded.fetched_utc,payload_json=excluded.payload_json";
            command.Parameters.AddWithValue("$key", hash);
            command.Parameters.AddWithValue("$time", fetched.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(rows));
            command.ExecuteNonQuery();
            PruneExpired();
        }
        finally
        {
            cacheGate.Release();
        }
    }

    void PruneExpired()
    {
        var now = clock();
        var expired = new List<string>();
        using (var select = cacheConnection.CreateCommand())
        {
            select.CommandText = "SELECT query_key, fetched_utc FROM query_cache";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var raw = reader.GetString(1);
                if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fetched) || now - fetched >= ttl)
                    expired.Add(key);
            }
        }

        foreach (var key in expired)
        {
            using var delete = cacheConnection.CreateCommand();
            delete.CommandText = "DELETE FROM query_cache WHERE query_key=$key";
            delete.Parameters.AddWithValue("$key", key);
            delete.ExecuteNonQuery();
        }
    }

    async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < 2 && !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct).ConfigureAwait(false);
                continue;
            }

            if (attempt >= 2 || !(response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
                return response;
            var retry = response.Headers.RetryAfter;
            var wait = retry?.Delta ?? (retry?.Date is { } date ? date - clock() : TimeSpan.FromSeconds(attempt + 1));
            if (wait > TimeSpan.FromSeconds(30))
                return response;
            response.Dispose();
            await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, ct).ConfigureAwait(false);
        }
    }

    public async Task<QueryResult<EntityResults>> FindEntitiesAsync(string name, string state, bool refresh, CancellationToken ct)
    {
        var fields = "billed_entity_number,billed_entity_name,billed_entity_city,billed_entity_state";
        var where = $"contains(upper(billed_entity_name),{Literal(name.ToUpperInvariant())})";
        if (!string.IsNullOrWhiteSpace(state))
            where += $" AND billed_entity_state={Literal(state.ToUpperInvariant())}";
        var response = await QueryAsync(new Dictionary<string, string>
        {
            ["$select"] = fields,
            ["$group"] = fields,
            ["$where"] = where,
            ["$order"] = "billed_entity_name",
            ["$limit"] = "101"
        }, refresh, ct).ConfigureAwait(false);
        var matches = response.Data.DistinctBy(r => r.Ben).Take(100).Select(r => new Entity(r.Ben, r.Name, r.City, r.State)).ToList();
        return new QueryResult<EntityResults>(new EntityResults(matches, response.Data.Count > 100), response.FetchedAt, response.FromCache);
    }

    public Task<QueryResult<List<Filing>>> GetFilingsAsync(string ben, int year, bool refresh, CancellationToken ct) =>
        GetFilingsAsync(ben, year, FilingForm.Form470, refresh, ct);

    public async Task<QueryResult<List<Filing>>> GetFilingsAsync(string ben, int year, FilingForm form, bool refresh, CancellationToken ct)
    {
        var filter = FilingFilter(ben, year, form);
        var all = new List<UsacRow>();
        var fetched = DateTimeOffset.MaxValue;
        var cached = true;
        for (var offset = 0; offset < 50000; offset += 1000)
        {
            var parameters = new Dictionary<string, string>
            {
                ["$where"] = filter,
                ["$order"] = form == FilingForm.Form470
                    ? "application_number,service_request_id,service_request_rfp_attachment"
                    : "application_number,form_version,last_modified_timestamp",
                ["$limit"] = "1000",
                ["$offset"] = offset.ToString(CultureInfo.InvariantCulture)
            };
            if (form == FilingForm.Form471)
                parameters["$select"] = Form471Select;
            var page = await QueryAsync(parameters, refresh, ct, form == FilingForm.Form471 ? Form471Endpoint : Endpoint).ConfigureAwait(false);
            all.AddRange(page.Data);
            if (page.FetchedAt < fetched)
                fetched = page.FetchedAt;
            cached &= page.FromCache;
            if (page.Data.Count < 1000)
            {
                if (form == FilingForm.Form471)
                    Form471Identity.Validate(ben, all);
                return new QueryResult<List<Filing>>(Collapse(all), fetched, cached);
            }
        }

        throw new InvalidOperationException("Result limit reached; a complete lookup could not be made.");
    }

    public async Task<QueryResult<bool>> HasNewCycleForm470Async(string ben, bool refresh, CancellationToken ct)
    {
        var (current, next) = NewCycleFundingYears();
        _ = FilingFilter(ben, next);
        var years = Literal(current.ToString(CultureInfo.InvariantCulture)) + "," + Literal(next.ToString(CultureInfo.InvariantCulture));
        var page = await QueryAsync(new Dictionary<string, string>
        {
            ["$select"] = "application_number",
            ["$where"] = $"billed_entity_number={Literal(ben)} AND funding_year in ({years}) AND fcc_form_470_status='Certified'",
            ["$limit"] = "1"
        }, refresh, ct).ConfigureAwait(false);
        return new QueryResult<bool>(page.Data.Count > 0, page.FetchedAt, page.FromCache);
    }

    public static List<Filing> Collapse(IEnumerable<UsacRow> rows)
    {
        var projected = rows.GroupBy(r => r.Number).Select(group =>
        {
            var current = group.Where(r => r.Version == "Current").ToList();
            if (current.Count == 0)
                current = group.ToList();
            var row = current
                .OrderByDescending(r => UsacTimestamps.TryParse(r.Modified, out var modified) ? modified : DateTimeOffset.MinValue)
                .ThenByDescending(r => r.Modified, StringComparer.Ordinal)
                .First();
            var services = string.Join("; ", current
                .Select(r => string.Join(" / ", new[] { r.Category, r.Service, r.Function }.Where(s => !string.IsNullOrWhiteSpace(s))))
                .Distinct()
                .OrderBy(s => s, StringComparer.Ordinal));
            var certifiedAt = UsacTimestamps.TryParse(row.Certified, out var certified) ? certified : (DateTimeOffset?)null;
            var filing = new Filing(
                row.Number,
                row.Ben,
                row.Name,
                row.Nickname,
                UsacTimestamps.CalendarDate(row.Certified),
                services,
                row.Pdf?.Url ?? (row.PdfUrl.Length > 0 ? row.PdfUrl : group.Select(r => r.PdfUrl).FirstOrDefault(u => u.Length > 0) ?? ""),
                !string.IsNullOrWhiteSpace(row.Status) ? row.Status : !string.IsNullOrWhiteSpace(row.Status470) ? row.Status470 : "Unknown",
                row.AllowableContractDate,
                row.RequestedAmount,
                row.PreDiscountAmount);
            return (filing, certifiedAt);
        });
        return projected
            .OrderByDescending(item => item.certifiedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.filing.Certified, StringComparer.Ordinal)
            .Select(item => item.filing)
            .ToList();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        cacheGate.Wait();
        try
        {
            cacheConnection.Dispose();
        }
        finally
        {
            cacheGate.Release();
            cacheGate.Dispose();
        }
    }

    void ThrowIfDisposed()
    {
        if (disposed != 0)
            throw new ObjectDisposedException(nameof(UsacClient));
    }
}
