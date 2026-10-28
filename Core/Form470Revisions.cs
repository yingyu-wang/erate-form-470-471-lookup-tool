using System.Globalization;
using System.Text;

namespace ERateLookup.Core;

/// <summary>
/// One locally stored observation of published Form 470 rows for a single application.
/// The rows are copies of USAC Open Data. The comparison between observations is derived.
/// </summary>
public sealed record Form470Revision(
    string ApplicationNumber,
    string Ben,
    string Name,
    int FundingYear,
    string QueryKey,
    long Sequence,
    DateTimeOffset ObservedAt,
    IReadOnlyList<UsacRow> Rows);

/// <summary>
/// One derived difference between two stored Form 470 observations.
/// <see cref="Kind"/> is <see cref="Form470Diff.Added"/>, <see cref="Form470Diff.Removed"/>,
/// <see cref="Form470Diff.Changed"/>, or <see cref="Form470Revisions.NotReturnedKind"/>.
/// </summary>
public sealed record Form470RevisionDifference(
    string Kind,
    string Scope,
    string ServiceRequestId,
    string Field,
    string Before,
    string After);

/// <summary>
/// App-facing text for locally observed Form 470 revisions.
/// Field matching comes from <see cref="Form470Diff"/>. This type does not call USAC.
/// </summary>
public static class Form470Revisions
{
    public const string ApplicationPrefix = "Form 470 ";

    public const string NotReturnedKind = "No longer returned by this Form 470 version query";

    public const string Disclaimer = "[Derived] Differences compare locally stored copies of published USAC Open Data Form 470 rows from dataset jt8s-3q52. This is not a USAC revision log, and it does not determine whether a change was substantial. The funding year is the year requested in the lookup.";

    public const string RefreshFailedNote = "Form 470 revision refresh failed. Previous observed revisions were kept.";

    public const string RefreshCanceledNote = "Form 470 revision refresh canceled. Previous observed revisions were kept.";

    public static string EventApplication(string applicationNumber) => ApplicationPrefix + applicationNumber.Trim();

    public static bool IsChange(FilingChange change) =>
        change.Application.StartsWith(ApplicationPrefix, StringComparison.Ordinal);

    public static string PortfolioNote(int count) =>
        "[Derived] " + count.ToString(CultureInfo.InvariantCulture) + " locally observed Form 470 revision(s) on this device. Not a USAC revision log.";

    public static string EventKey(string applicationNumber, Form470RevisionDifference difference)
    {
        var service = difference.ServiceRequestId.Length == 0 ? "-" : difference.ServiceRequestId;
        return EventApplication(applicationNumber) + " · " + difference.Kind + " · " + difference.Scope + " · " + service + " · " + difference.Field;
    }

    public static string ChangeKind(Form470RevisionDifference difference) =>
        difference.Kind == NotReturnedKind ? NotReturnedKind : difference.Kind + " · " + difference.Field;

    /// <summary>
    /// Differences between two stored copies. An empty later copy is one
    /// <see cref="NotReturnedKind"/> line. That line does not establish withdrawal.
    /// </summary>
    public static List<Form470RevisionDifference> Differences(IReadOnlyList<UsacRow>? before, IReadOnlyList<UsacRow>? after)
    {
        var left = (before ?? []).Where(row => row is not null).ToList();
        var right = (after ?? []).Where(row => row is not null).ToList();
        if (left.Count > 0 && right.Count == 0)
        {
            return [new Form470RevisionDifference(NotReturnedKind, Form470Diff.ApplicationScope, "", "application", "[Derived] previously stored published rows", "")];
        }

        var changes = new List<Form470RevisionDifference>();
        foreach (var version in Versions(left, right))
        {
            foreach (var change in Form470Diff.CompareObservations(Slice(left, version), Slice(right, version)))
            {
                changes.Add(new Form470RevisionDifference(
                    change.Change,
                    change.Scope,
                    change.ServiceRequestId ?? "",
                    version.Length == 0 ? "(blank version) · " + change.Field : version + " · " + change.Field,
                    change.Original ?? "",
                    change.Current ?? ""));
            }
        }

        return changes;
    }

    public static string RevisionSummary(Form470Revision revision)
    {
        var rows = revision.Rows ?? [];
        var current = Slice(rows, Form470Diff.CurrentVersion);
        var view = current.Count > 0 ? current : Slice(rows, Form470Diff.OriginalVersion);
        if (view.Count == 0)
        {
            view = rows.Where(row => row is not null).ToList();
        }

        if (view.Count == 0)
        {
            return "[Derived] No published rows in this observation. This does not establish that the form was withdrawn.";
        }

        var status = First(view, row => row.Status470);
        if (status.Length == 0)
        {
            status = First(view, row => row.Status);
        }

        var modified = First(view, row => row.Modified);
        var services = view.Select(row => row.ServiceRequestId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Count();
        var documents = view.Count(row => !string.IsNullOrWhiteSpace(row.RfpDocuments?.Url) || !string.IsNullOrWhiteSpace(row.RfpUploadDate));
        return "[Derived] status " + Display(status) + "; last modified " + Display(modified) + "; service requests " + services.ToString(CultureInfo.InvariantCulture) + "; RFP documents " + documents.ToString(CultureInfo.InvariantCulture) + ". Values are copied from the stored published rows.";
    }

    public static string SelectionSummary(string applicationNumber, IReadOnlyList<Form470Revision> revisions, string? nickname = null)
    {
        var number = (applicationNumber ?? "").Trim();
        var ordered = Order(revisions);
        var text = new StringBuilder();
        text.Append("Form 470 ").AppendLine(number);
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            text.Append("Nickname: ").AppendLine(nickname.Trim());
        }

        if (ordered.Count == 0)
        {
            text.Append("No locally observed revisions yet.");
            return text.ToString().TrimEnd();
        }

        text.Append(ordered.Count.ToString(CultureInfo.InvariantCulture)).Append(" locally observed revision(s). Latest: ").Append(RevisionSummary(ordered[^1]));
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Full history and diff text for a Form 470 the user is viewing.
    /// History is oldest first. Comparison text is labeled derived.
    /// </summary>
    public static string ViewText(string applicationNumber, IReadOnlyList<Form470Revision> revisions, string? nickname = null)
    {
        var number = (applicationNumber ?? "").Trim();
        var ordered = Order(revisions);
        var text = new StringBuilder();
        text.Append("Form 470 ").AppendLine(number);
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            text.Append("Nickname: ").AppendLine(nickname.Trim());
        }

        text.AppendLine(Disclaimer);
        if (ordered.Count == 0)
        {
            text.AppendLine("No locally observed revisions yet. A live Form 470 lookup stores published rows on this device. Nothing was inferred from a missing observation.");
            return text.ToString().TrimEnd();
        }

        text.Append("Observed revisions (").Append(ordered.Count.ToString(CultureInfo.InvariantCulture)).AppendLine("), oldest first");
        for (var i = 0; i < ordered.Count; i++)
        {
            var revision = ordered[i];
            text.Append(i + 1).Append(" · sequence ").Append(revision.Sequence.ToString(CultureInfo.InvariantCulture));
            text.Append(" · observed ").Append(revision.ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            text.Append(" · BEN ").Append(revision.Ben).Append(" · FY").Append(revision.FundingYear.ToString(CultureInfo.InvariantCulture));
            text.Append(" · ").AppendLine(RevisionSummary(revision));
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            text.Append("[Derived] Changes from observation ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" to ").AppendLine((i + 1).ToString(CultureInfo.InvariantCulture));
            var diffs = Differences(ordered[i - 1].Rows, ordered[i].Rows);
            if (diffs.Count == 0)
            {
                text.AppendLine("No field differences.");
            }
            else
            {
                foreach (var diff in diffs)
                {
                    AppendDifference(text, diff);
                }
            }
        }

        text.AppendLine("Published Original vs Current on the latest stored observation");
        var latestRows = ordered[^1].Rows ?? [];
        if (latestRows.Count == 0)
        {
            text.AppendLine("No published rows in the latest observation. This does not establish withdrawal or denial.");
        }
        else
        {
            var published = Form470Diff.Compare(latestRows);
            if (published.Count == 0)
            {
                text.AppendLine("No published field differences.");
            }
            else
            {
                foreach (var change in published)
                {
                    text.Append(change.Change).Append(" · ").Append(change.Scope).Append(" · ");
                    text.Append(string.IsNullOrEmpty(change.ServiceRequestId) ? "-" : change.ServiceRequestId);
                    text.Append(" · ").AppendLine(change.Field);
                    text.Append("Before: ").AppendLine(change.Original ?? "");
                    text.Append("After: ").AppendLine(change.Current ?? "");
                }
            }

            text.AppendLine(Form470Diff.Disclaimer);
        }

        return text.ToString().TrimEnd();
    }

    public static async Task ObserveLookupAsync(UsacClient client, PortfolioStore store, string ben, int year, bool refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        var key = new LookupKey(ben.Trim(), year, FilingForm.Form470);
        key.Validate();
        // Allocated before the read so a slower request cannot outrank one that
        // started later, and ClearHistory during the fetch invalidates this attempt.
        var sequence = store.BeginObservation();
        var versions = await client.GetForm470VersionRowsAsync(key.Ben, null, year, null, refresh, cancellationToken).ConfigureAwait(false);
        if (versions.FromCache || versions.Data is null || versions.Data.Count == 0)
        {
            return;
        }

        store.SaveForm470Revisions(key, sequence, versions, cancellationToken);
    }

    static void AppendDifference(StringBuilder text, Form470RevisionDifference diff)
    {
        var service = diff.ServiceRequestId.Length == 0 ? "-" : diff.ServiceRequestId;
        text.Append(diff.Kind).Append(" · ").Append(diff.Scope).Append(" · ").Append(service).Append(" · ").AppendLine(diff.Field);
        text.Append("Before: ").AppendLine(diff.Before);
        text.Append("After: ").AppendLine(diff.After);
    }

    static List<Form470Revision> Order(IReadOnlyList<Form470Revision>? revisions) =>
        (revisions ?? []).Where(revision => revision is not null).OrderBy(revision => revision.Sequence).ThenBy(revision => revision.ObservedAt).ToList();

    static string Display(string value) => value.Length == 0 ? "(blank)" : value;

    static string First(IReadOnlyList<UsacRow> rows, Func<UsacRow, string?> read)
    {
        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var value = read(row)?.Trim() ?? "";
            if (value.Length > 0)
            {
                return value;
            }
        }

        return "";
    }

    static List<UsacRow> Slice(IReadOnlyList<UsacRow> rows, string version) =>
        rows.Where(row => row is not null && string.Equals(VersionOf(row), version, StringComparison.Ordinal)).ToList();

    static IEnumerable<string> Versions(IReadOnlyList<UsacRow> before, IReadOnlyList<UsacRow> after)
    {
        yield return Form470Diff.OriginalVersion;
        yield return Form470Diff.CurrentVersion;
        foreach (var version in before.Concat(after).Where(row => row is not null).Select(VersionOf).Where(version => version != Form470Diff.OriginalVersion && version != Form470Diff.CurrentVersion).Distinct(StringComparer.Ordinal).OrderBy(version => version, StringComparer.Ordinal))
        {
            yield return version;
        }
    }

    static string VersionOf(UsacRow row) => row.Version?.Trim() ?? "";
}
