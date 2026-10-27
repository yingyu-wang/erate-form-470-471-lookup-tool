using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ERateLookup.Core;

public sealed record Watchlist(string Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record WatchlistEntry(string Ben, string Name, string Tags)
{
    public override string ToString() => $"{Ben} · {Name}";
}
public sealed record LookupKey(string Ben, int Year, FilingForm Form)
{
    public string Id => $"{Ben}/{Year}/{(int)Form}";
    public void Validate() => _ = UsacClient.FilingFilter(Ben, Year, Form);
}
public sealed record FilingChange(string Application, string Kind, string Before, string After);
public sealed record SavedLookup(LookupKey Key, DateTimeOffset ObservedAt, long Sequence,
    List<Filing> Filings, List<FilingChange> Changes, bool FirstObservation = false);
public sealed record SavedChange(long Sequence, FilingChange Change, DateTimeOffset ObservedAt);

/// <summary>Durable user data, separate from the disposable HTTP query cache.</summary>
public sealed class PortfolioStore
{
    readonly string connectionString;
    readonly Func<DateTimeOffset> clock;
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public PortfolioStore(string dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(dataDirectory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "portfolio.sqlite3"),
            Pooling = false,
            DefaultTimeout = 15
        }.ToString();
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var version = Command(db, tx, "PRAGMA user_version");
        var schema = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (schema > 3)
            throw new InvalidOperationException("Saved portfolio data belongs to a newer app. Update the app; the data has not been changed.");
        if (schema == 0)
        {
            using var migration = Command(db, tx, """
                CREATE TABLE watchlists(id TEXT PRIMARY KEY,name TEXT NOT NULL);
                CREATE TABLE entries(list_id TEXT NOT NULL REFERENCES watchlists(id) ON DELETE CASCADE,
                  ben TEXT NOT NULL,name TEXT NOT NULL,tags TEXT NOT NULL,PRIMARY KEY(list_id,ben));
                CREATE TABLE sequence(value INTEGER NOT NULL);
                INSERT INTO sequence VALUES(0);
                CREATE TABLE snapshots(query_key TEXT PRIMARY KEY,sequence INTEGER NOT NULL,payload TEXT NOT NULL,previous_payload TEXT);
                INSERT INTO watchlists VALUES('favorites','Favorites');
                PRAGMA user_version=1;
                """);
            migration.ExecuteNonQuery();
        }
        if (schema < 2)
        {
            using var migration = Command(db, tx, """
                ALTER TABLE sequence ADD COLUMN cleared_through INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE change_events(query_key TEXT NOT NULL,sequence INTEGER NOT NULL,application TEXT NOT NULL,
                  observed_utc TEXT NOT NULL,payload TEXT NOT NULL,acknowledged_utc TEXT,
                  PRIMARY KEY(query_key,sequence,application));
                CREATE TABLE lookup_outcomes(query_key TEXT PRIMARY KEY,sequence INTEGER NOT NULL,outcome TEXT NOT NULL,observed_utc TEXT NOT NULL);
                PRAGMA user_version=2;
                """);
            migration.ExecuteNonQuery();
        }
        if (schema < 3)
        {
            // One row per query and funding request. reported_committed_amount stores the
            // export CommittedAmount inference. It is not a funding commitment decision (Q8).
            using var migration = Command(db, tx, """
                CREATE TABLE frn_snapshots(
                  query_key TEXT NOT NULL,
                  funding_request_number TEXT NOT NULL,
                  application_number TEXT NOT NULL,
                  name TEXT NOT NULL,
                  frn_status TEXT NOT NULL,
                  reported_committed_amount TEXT NOT NULL,
                  fcdl_date TEXT NOT NULL,
                  revised_fcdl_date TEXT NOT NULL,
                  disbursed_amount TEXT NOT NULL,
                  sequence INTEGER NOT NULL,
                  observed_utc TEXT NOT NULL,
                  PRIMARY KEY(query_key, funding_request_number));
                CREATE TABLE frn_observations(
                  query_key TEXT PRIMARY KEY,
                  sequence INTEGER NOT NULL,
                  observed_utc TEXT NOT NULL);
                PRAGMA user_version=3;
                """);
            migration.ExecuteNonQuery();
        }
        tx.Commit();
    }
    SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        using var c = db.CreateCommand();
        c.CommandText = "PRAGMA foreign_keys=ON";
        c.ExecuteNonQuery();
        return db;
    }
    static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        var c = db.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (key, value) in args)
            c.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return c;
    }
    static string Text(string value, int limit, string label, bool required = false)
    {
        value = value.Trim();
        if (value.Length > limit || (required && value.Length == 0) || value.Any(char.IsControl))
            throw new ArgumentException($"{label} must contain {(required ? "1" : "0")}–{limit} printable characters.");
        return value;
    }
    public List<Watchlist> Lists()
    {
        using var db = Open();
        using var c = Command(db, null, "SELECT id,name FROM watchlists ORDER BY name,id");
        using var r = c.ExecuteReader();
        var result = new List<Watchlist>();
        while (r.Read())
            result.Add(new(r.GetString(0), r.GetString(1)));
        return result;
    }
    public Watchlist CreateList(string name)
    {
        var list = new Watchlist(Guid.NewGuid().ToString("N"), Text(name, 80, "List name", true));
        using var db = Open();
        using var c = Command(db, null, "INSERT INTO watchlists VALUES($id,$name)", ("$id", list.Id), ("$name", list.Name));
        c.ExecuteNonQuery();
        return list;
    }
    public void RenameList(string id, string name)
    {
        using var db = Open();
        using var c = Command(db, null, "UPDATE watchlists SET name=$name WHERE id=$id", ("$id", id), ("$name", Text(name, 80, "List name", true)));
        if (c.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Watchlist no longer exists.");
    }
    public void DeleteList(string id)
    {
        using var db = Open();
        using var c = Command(db, null, "DELETE FROM watchlists WHERE id=$id", ("$id", id));
        c.ExecuteNonQuery();
    }
    public List<WatchlistEntry> Entries(string id)
    {
        using var db = Open();
        using var c = Command(db, null, "SELECT ben,name,tags FROM entries WHERE list_id=$id ORDER BY name,ben", ("$id", id));
        using var r = c.ExecuteReader();
        var result = new List<WatchlistEntry>();
        while (r.Read())
            result.Add(new(r.GetString(0), r.GetString(1), r.GetString(2)));
        return result;
    }
    public void SaveEntry(string id, WatchlistEntry entry, bool replace = false)
    {
        var ben = entry.Ben.Trim();
        if (ben.Length > 32)
            throw new ArgumentException("BEN is too long.");
        new LookupKey(ben, 2027, FilingForm.Form470).Validate();
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var count = Command(db, tx, "SELECT COUNT(*) FROM entries WHERE list_id=$id", ("$id", id));
        if (!replace && Convert.ToInt32(count.ExecuteScalar()) >= 500)
            throw new InvalidOperationException("A watchlist supports up to 500 BENs.");
        using var c = Command(db, tx, replace
            ? "UPDATE entries SET name=$name,tags=$tags WHERE list_id=$id AND ben=$ben"
            : "INSERT INTO entries(list_id,ben,name,tags) VALUES($id,$ben,$name,$tags)",
            ("$id", id), ("$ben", ben), ("$name", Text(entry.Name, 200, "Display name")), ("$tags", Text(entry.Tags, 300, "Tags")));
        try
        {
            if (c.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Saved BEN no longer exists.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new InvalidOperationException("This BEN is already saved, or the watchlist no longer exists.", ex); }
        tx.Commit();
    }
    public void RemoveEntry(string id, string ben)
    {
        using var db = Open();
        using var c = Command(db, null, "DELETE FROM entries WHERE list_id=$id AND ben=$ben", ("$id", id), ("$ben", ben));
        c.ExecuteNonQuery();
    }
    public int ImportEntries(string id, IReadOnlyList<WatchlistEntry> entries, CancellationToken ct = default)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var existing = Command(db, tx, "SELECT ben FROM entries WHERE list_id=$id", ("$id", id));
        var bens = new HashSet<string>();
        using (var reader = existing.ExecuteReader())
            while (reader.Read())
                bens.Add(reader.GetString(0));
        var pending = entries.Where(e => !bens.Contains(e.Ben)).DistinctBy(e => e.Ben).ToList();
        if (bens.Count + pending.Count > 500)
            throw new InvalidOperationException("Import would exceed 500 BENs. No entries were added.");
        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();
            new LookupKey(entry.Ben, 2027, FilingForm.Form470).Validate();
            if (entry.Ben.Length > 32)
                throw new ArgumentException("BEN is too long.");
            using var c = Command(db, tx, "INSERT INTO entries VALUES($id,$ben,$name,$tags)", ("$id", id), ("$ben", entry.Ben), ("$name", Text(entry.Name, 200, "Display name")), ("$tags", Text(entry.Tags, 300, "Tags")));
            c.ExecuteNonQuery();
        }
        ct.ThrowIfCancellationRequested();
        tx.Commit();
        return pending.Count;
    }
    // Allocated immediately before starting the network observation. Global across app processes.
    public long BeginObservation()
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var c = Command(db, tx, "UPDATE sequence SET value=value+1 RETURNING value");
        var value = Convert.ToInt64(c.ExecuteScalar(), CultureInfo.InvariantCulture);
        tx.Commit();
        return value;
    }
    public SavedLookup? Read(LookupKey key)
    {
        key.Validate();
        using var db = Open();
        return Read(db, null, key);
    }
    static SavedLookup? Read(SqliteConnection db, SqliteTransaction? tx, LookupKey key)
    {
        using var c = Command(db, tx, "SELECT payload FROM snapshots WHERE query_key=$key", ("$key", key.Id));
        return c.ExecuteScalar() is string json ? JsonSerializer.Deserialize<SavedLookup>(json, Json) : null;
    }
    public bool SaveLive(LookupKey key, long sequence, QueryResult<List<Filing>> result, CancellationToken ct = default)
    {
        key.Validate();
        if (result.FromCache)
            return false;
        if (result.Data.Any(f => f.Ben != key.Ben || string.IsNullOrWhiteSpace(f.Number)) || result.Data.Select(f => f.Number).Distinct().Count() != result.Data.Count)
            throw new InvalidOperationException("The returned applications do not form a valid complete snapshot.");
        using var db = Open();
        using var tx = db.BeginTransaction();
        var old = Read(db, tx, key);
        using var watermark = Command(db, tx, "SELECT cleared_through FROM sequence");
        if (sequence <= Convert.ToInt64(watermark.ExecuteScalar()))
            return false;
        if (old is not null && sequence <= old.Sequence)
            return false;
        var changes = old is null ? new List<FilingChange>() : Compare(old.Filings, result.Data);
        var saved = new SavedLookup(key, result.FetchedAt, sequence, result.Data, changes, old is null);
        using var c = Command(db, tx, """
            INSERT INTO snapshots(query_key,sequence,payload,previous_payload) VALUES($key,$seq,$data,$old)
            ON CONFLICT(query_key) DO UPDATE SET sequence=excluded.sequence,payload=excluded.payload,previous_payload=excluded.previous_payload
            """, ("$key", key.Id), ("$seq", sequence), ("$data", JsonSerializer.Serialize(saved, Json)), ("$old", old is null ? null : JsonSerializer.Serialize(old with
        {
            Changes = []
        }, Json)));
        c.ExecuteNonQuery();
        foreach (var change in changes)
        {
            using var add = Command(db, tx, "INSERT INTO change_events(query_key,sequence,application,observed_utc,payload) VALUES($key,$seq,$app,$utc,$data)",
                ("$key", key.Id), ("$seq", sequence), ("$app", change.Application), ("$utc", result.FetchedAt.ToUniversalTime().ToString("O")), ("$data", JsonSerializer.Serialize(change, Json)));
            add.ExecuteNonQuery();
        }
        using var cleanup = Command(db, tx, "DELETE FROM change_events WHERE acknowledged_utc IS NOT NULL AND acknowledged_utc < $cutoff", ("$cutoff", clock().AddDays(-90).ToString("O")));
        cleanup.ExecuteNonQuery();
        ct.ThrowIfCancellationRequested();
        tx.Commit();
        return true;
    }
    public List<SavedChange> UnreadChanges(LookupKey key)
    {
        using var db = Open();
        using var c = Command(db, null, "SELECT sequence,observed_utc,payload FROM change_events WHERE query_key=$key AND acknowledged_utc IS NULL ORDER BY sequence,application", ("$key", key.Id));
        using var r = c.ExecuteReader();
        var changes = new List<SavedChange>();
        while (r.Read())
            changes.Add(new(r.GetInt64(0), JsonSerializer.Deserialize<FilingChange>(r.GetString(2), Json)!, DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture)));
        return changes;
    }
    public void Acknowledge(LookupKey key, long throughSequence)
    {
        using var db = Open();
        using var c = Command(db, null, "UPDATE change_events SET acknowledged_utc=$utc WHERE query_key=$key AND sequence<=$seq AND acknowledged_utc IS NULL", ("$key", key.Id), ("$seq", throughSequence), ("$utc", clock().ToString("O")));
        c.ExecuteNonQuery();
    }
    public void RecordOutcome(LookupKey key, long sequence, string outcome)
    {
        using var db = Open();
        using var c = Command(db, null, """
            INSERT INTO lookup_outcomes VALUES($key,$seq,$outcome,$utc)
            ON CONFLICT(query_key) DO UPDATE SET sequence=excluded.sequence,outcome=excluded.outcome,observed_utc=excluded.observed_utc
            WHERE excluded.sequence>lookup_outcomes.sequence
            """, ("$key", key.Id), ("$seq", sequence), ("$outcome", outcome), ("$utc", clock().ToString("O")));
        c.ExecuteNonQuery();
    }
    public void ClearHistory()
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var c = Command(db, tx, "UPDATE sequence SET value=value+1,cleared_through=value+1; DELETE FROM snapshots; DELETE FROM change_events; DELETE FROM lookup_outcomes; DELETE FROM frn_snapshots; DELETE FROM frn_observations;");
        c.ExecuteNonQuery();
        tx.Commit();
    }
    public static List<FilingChange> Compare(IEnumerable<Filing> before, IEnumerable<Filing> after)
    {
        var a = before.ToDictionary(f => f.Number);
        var b = after.ToDictionary(f => f.Number);
        var changes = new List<FilingChange>();
        foreach (var (id, f) in b)
        {
            if (!a.TryGetValue(id, out var old))
                changes.Add(new(id, "New", "", Describe(f)));
            else if (Describe(old) != Describe(f))
                changes.Add(new(id, "Changed", Describe(old), Describe(f)));
        }
        foreach (var (id, f) in a)
            if (!b.ContainsKey(id))
                changes.Add(new(id, "No longer returned by this query", Describe(f), ""));
        return changes;
    }
    static string Describe(Filing f) => JsonSerializer.Serialize(new
    {
        f.Status,
        f.Certified,
        f.Nickname,
        Services = string.Join("; ", f.Services.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
        f.Pdf,
        f.AllowableContractDate,
        f.RequestedAmount,
        f.PreDiscountAmount
    });

    public static LookupKey FrnKey(LookupKey query) => new(query.Ben.Trim(), query.Year, FilingForm.Form471);

    public FrnBaseline? ReadFrns(LookupKey query)
    {
        var key = FrnKey(query);
        key.Validate();
        using var db = Open();
        return ReadFrns(db, null, key);
    }

    static FrnBaseline? ReadFrns(SqliteConnection db, SqliteTransaction? tx, LookupKey key)
    {
        using var run = Command(db, tx, "SELECT sequence, observed_utc FROM frn_observations WHERE query_key=$key", ("$key", key.Id));
        using var header = run.ExecuteReader();
        if (!header.Read())
            return null;
        var sequence = header.GetInt64(0);
        var observed = DateTimeOffset.Parse(header.GetString(1), CultureInfo.InvariantCulture);
        header.Close();
        using var rows = Command(db, tx, """
            SELECT funding_request_number, application_number, name, frn_status, reported_committed_amount, fcdl_date, revised_fcdl_date, disbursed_amount
            FROM frn_snapshots WHERE query_key=$key ORDER BY funding_request_number
            """, ("$key", key.Id));
        using var reader = rows.ExecuteReader();
        var frns = new List<ReportedFrn>();
        while (reader.Read())
            frns.Add(new ReportedFrn(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return new FrnBaseline(key, sequence, observed, frns);
    }

    public bool SaveFrns(LookupKey query, long sequence, QueryResult<List<UsacRow>> result, CancellationToken ct = default)
    {
        var key = FrnKey(query);
        key.Validate();
        if (result.FromCache)
            return false;
        var incoming = FrnSnapshot.FromRows(key.Ben, result.Data);
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var watermark = Command(db, tx, "SELECT cleared_through FROM sequence");
        if (sequence <= Convert.ToInt64(watermark.ExecuteScalar(), CultureInfo.InvariantCulture))
            return false;
        var old = ReadFrns(db, tx, key);
        if (old is not null && sequence <= old.Sequence)
            return false;
        var observed = result.FetchedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var changes = old is null ? new List<FrnFieldChange>() : FrnSnapshot.Compare(old.Frns, incoming);
        using var clear = Command(db, tx, "DELETE FROM frn_snapshots WHERE query_key=$key", ("$key", key.Id));
        clear.ExecuteNonQuery();
        foreach (var frn in incoming)
        {
            using var add = Command(db, tx, """
                INSERT INTO frn_snapshots(query_key, funding_request_number, application_number, name, frn_status, reported_committed_amount, fcdl_date, revised_fcdl_date, disbursed_amount, sequence, observed_utc)
                VALUES($key,$frn,$app,$name,$status,$reported,$fcdl,$revised,$disbursed,$seq,$utc)
                """,
                ("$key", key.Id), ("$frn", frn.FundingRequestNumber), ("$app", frn.ApplicationNumber), ("$name", frn.Name),
                ("$status", frn.FrnStatus), ("$reported", frn.ReportedCommittedAmount), ("$fcdl", frn.FcdlDate),
                ("$revised", frn.RevisedFcdlDate), ("$disbursed", frn.DisbursedAmount), ("$seq", sequence), ("$utc", observed));
            add.ExecuteNonQuery();
        }
        using var mark = Command(db, tx, """
            INSERT INTO frn_observations(query_key, sequence, observed_utc) VALUES($key,$seq,$utc)
            ON CONFLICT(query_key) DO UPDATE SET sequence=excluded.sequence, observed_utc=excluded.observed_utc
            """, ("$key", key.Id), ("$seq", sequence), ("$utc", observed));
        mark.ExecuteNonQuery();
        foreach (var change in changes)
        {
            var payload = new FilingChange("FRN " + change.FundingRequestNumber, change.Kind, change.Before, change.After);
            using var add = Command(db, tx, "INSERT INTO change_events(query_key,sequence,application,observed_utc,payload) VALUES($key,$seq,$app,$utc,$data)",
                ("$key", key.Id), ("$seq", sequence), ("$app", "FRN " + change.FundingRequestNumber + " · " + change.Kind),
                ("$utc", observed), ("$data", JsonSerializer.Serialize(payload, Json)));
            add.ExecuteNonQuery();
        }
        using var cleanup = Command(db, tx, "DELETE FROM change_events WHERE acknowledged_utc IS NOT NULL AND acknowledged_utc < $cutoff", ("$cutoff", clock().AddDays(-90).ToString("O")));
        cleanup.ExecuteNonQuery();
        ct.ThrowIfCancellationRequested();
        tx.Commit();
        return true;
    }

    public void AcknowledgeFrns(LookupKey query, long throughSequence)
    {
        var key = FrnKey(query);
        using var db = Open();
        using var c = Command(db, null, "UPDATE change_events SET acknowledged_utc=$utc WHERE query_key=$key AND sequence<=$seq AND acknowledged_utc IS NULL AND application LIKE 'FRN %'",
            ("$key", key.Id), ("$seq", throughSequence), ("$utc", clock().ToString("O")));
        c.ExecuteNonQuery();
    }
}

public sealed record FrnBaseline(LookupKey Key, long Sequence, DateTimeOffset ObservedAt, List<ReportedFrn> Frns);
