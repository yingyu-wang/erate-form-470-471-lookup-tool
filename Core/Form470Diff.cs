using System.Globalization;

namespace ERateLookup.Core;

public sealed record Form470FieldChange(
    string ApplicationNumber,
    string Ben,
    string Name,
    string Change,
    string Scope,
    string ServiceRequestId,
    string Field,
    string Original,
    string Current);

/// <summary>
/// Compares published Form 470 Original and Current rows from dataset jt8s-3q52.
/// Service lines are matched on <c>service_request_id</c>. The comparison does not
/// decide whether a change was substantial, and it does not store earlier lookups.
/// </summary>
public static class Form470Diff
{
    public const string Added = "Added";
    public const string Removed = "Removed";
    public const string Changed = "Changed";
    public const string VersionGap = "VersionGap";
    public const string Unexpected = "Unexpected";
    public const string ApplicationScope = "Application";
    public const string ServiceScope = "Service";
    public const string OriginalVersion = "Original";
    public const string CurrentVersion = "Current";
    public const string NotPublished = "not published";
    public const string Disclaimer = "This comparison reads published Original and Current rows from USAC Open Data dataset jt8s-3q52. It does not determine whether a change was substantial.";

    public static IReadOnlyList<Form470FieldChange> Compare(IEnumerable<UsacRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var changes = new List<Form470FieldChange>();
        foreach (var application in rows.GroupBy(row => row.Number ?? "").OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            changes.AddRange(CompareApplication(application.Key, application.ToList()));
        }

        return changes;
    }

    static IEnumerable<Form470FieldChange> CompareApplication(string applicationNumber, List<UsacRow> rows)
    {
        var original = rows.Where(row => row.Version == OriginalVersion).ToList();
        var current = rows.Where(row => row.Version == CurrentVersion).ToList();
        var ben = Prefer(current, original, row => row.Ben);
        var name = Prefer(current, original, row => row.Name);
        if (original.Count == 0 || current.Count == 0)
        {
            yield return Change(applicationNumber, ben, name, VersionGap, ApplicationScope, "", "form_version", original.Count == 0 ? NotPublished : OriginalVersion, current.Count == 0 ? NotPublished : CurrentVersion);
        }

        foreach (var version in rows
            .Select(row => row.Version ?? "")
            .Where(version => version != OriginalVersion && version != CurrentVersion)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(version => version, StringComparer.Ordinal))
        {
            yield return Change(applicationNumber, ben, name, Unexpected, ApplicationScope, "", "form_version", "", version.Length == 0 ? "(blank)" : version);
        }

        if (original.Count == 0 || current.Count == 0)
        {
            yield break;
        }

        foreach (var field in ApplicationFields)
        {
            var left = Aggregate(original, field);
            var right = Aggregate(current, field);
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                yield return Change(applicationNumber, ben, name, Changed, ApplicationScope, "", field.Name, left, right);
            }
        }

        var originalServices = ServiceGroups(original);
        var currentServices = ServiceGroups(current);
        foreach (var serviceId in originalServices.Keys.Except(currentServices.Keys, StringComparer.Ordinal).OrderBy(id => id, ServiceIdComparer.Instance))
        {
            yield return Change(applicationNumber, ben, name, Removed, ServiceScope, serviceId, "service", ServiceSummary(originalServices[serviceId]), "");
        }

        foreach (var serviceId in originalServices.Keys.Intersect(currentServices.Keys, StringComparer.Ordinal).OrderBy(id => id, ServiceIdComparer.Instance))
        {
            var leftRows = originalServices[serviceId];
            var rightRows = currentServices[serviceId];
            foreach (var field in ServiceFields)
            {
                var left = Aggregate(leftRows, field);
                var right = Aggregate(rightRows, field);
                if (!string.Equals(left, right, StringComparison.Ordinal))
                {
                    yield return Change(applicationNumber, ben, name, Changed, ServiceScope, serviceId, field.Name, left, right);
                }
            }

            var leftDocuments = RfpDocuments(leftRows);
            var rightDocuments = RfpDocuments(rightRows);
            if (!string.Equals(leftDocuments, rightDocuments, StringComparison.Ordinal))
            {
                yield return Change(applicationNumber, ben, name, Changed, ServiceScope, serviceId, "rfp_documents", leftDocuments, rightDocuments);
            }
        }

        foreach (var serviceId in currentServices.Keys.Except(originalServices.Keys, StringComparer.Ordinal).OrderBy(id => id, ServiceIdComparer.Instance))
        {
            yield return Change(applicationNumber, ben, name, Added, ServiceScope, serviceId, "service", "", ServiceSummary(currentServices[serviceId]));
        }
    }

    static Form470FieldChange Change(string applicationNumber, string ben, string name, string change, string scope, string serviceRequestId, string field, string original, string current) =>
        new(applicationNumber, ben, name, change, scope, serviceRequestId, field, original, current);

    static Dictionary<string, List<UsacRow>> ServiceGroups(IReadOnlyList<UsacRow> rows)
    {
        var groups = new Dictionary<string, List<UsacRow>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ServiceRequestId))
            {
                continue;
            }

            var serviceId = row.ServiceRequestId.Trim();
            if (!groups.TryGetValue(serviceId, out var list))
            {
                list = new List<UsacRow>();
                groups.Add(serviceId, list);
            }

            list.Add(row);
        }

        return groups;
    }

    static string ServiceSummary(IReadOnlyList<UsacRow> rows)
    {
        var parts = new[]
        {
            Aggregate(rows, new FieldSpec("service_category", row => row.Category, false)),
            Aggregate(rows, new FieldSpec("service_type", row => row.Service, false)),
            Aggregate(rows, new FieldSpec("function", row => row.Function, false))
        };
        return string.Join(" / ", parts.Where(part => part.Length > 0));
    }

    static string Aggregate(IReadOnlyList<UsacRow> rows, FieldSpec field)
    {
        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var value = Normalize(field, field.Read(row));
            if (!field.Checkbox && value.Length == 0)
            {
                continue;
            }

            values.Add(value);
        }

        if (field.Checkbox && values.Count == 0)
        {
            return "false";
        }

        return string.Join(" | ", values);
    }

    static string Normalize(FieldSpec field, string value)
    {
        var text = value.Trim();
        if (!field.Checkbox)
        {
            return text;
        }

        if (text.Length == 0 || text.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return "false";
        }

        if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return "true";
        }

        return text;
    }

    static string RfpDocuments(IEnumerable<UsacRow> rows)
    {
        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var url = row.RfpDocuments?.Url?.Trim() ?? "";
            var uploaded = row.RfpUploadDate?.Trim() ?? "";
            if (url.Length == 0 && uploaded.Length == 0)
            {
                continue;
            }

            if (url.Length == 0)
            {
                values.Add(uploaded);
            }
            else if (uploaded.Length == 0)
            {
                values.Add(url);
            }
            else
            {
                values.Add(url + " (" + uploaded + ")");
            }
        }

        return string.Join(" | ", values);
    }

    static string Prefer(IReadOnlyList<UsacRow> current, IReadOnlyList<UsacRow> original, Func<UsacRow, string> read)
    {
        var fromCurrent = current.Select(read).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(fromCurrent))
        {
            return fromCurrent.Trim();
        }

        var fromOriginal = original.Select(read).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(fromOriginal) ? "" : fromOriginal.Trim();
    }

    static readonly FieldSpec[] ApplicationFields =
    [
        new("billed_entity_number", row => row.Ben, false),
        new("billed_entity_name", row => row.Name, false),
        new("form_nickname", row => row.Nickname, false),
        new("fcc_form_470_status", row => row.Status470, false),
        new("certified_date_time", row => row.Certified, false),
        new("last_modified_date_time", row => row.Modified, false),
        new("allowable_contract_date", row => row.AllowableContractDate, false),
        new("number_of_eligible_entities", row => row.EligibleEntities, false),
        new("contact_name", row => row.ContactName, false),
        new("contact_email", row => row.ContactEmail, false),
        new("contact_phone", row => row.ContactPhone, false),
        new("technical_contact_name", row => row.TechnicalContactName, false),
        new("technical_contact_email", row => row.TechnicalContactEmail, false),
        new("technical_contact_phone", row => row.TechnicalContactPhone, false),
        new("authorized_person_name", row => row.AuthorizedPersonName, false),
        new("authorized_person_email", row => row.AuthorizedPersonEmail, false),
        new("authorized_person_phone", row => row.AuthorizedPersonPhone, false),
        new("category_one_description", row => row.CategoryOneDescription, false),
        new("category_two_description", row => row.CategoryTwoDescription, false),
        new("rfp_identifier", row => row.RfpIdentifier, true)
    ];

    static readonly FieldSpec[] ServiceFields =
    [
        new("service_category", row => row.Category, false),
        new("service_type", row => row.Service, false),
        new("function", row => row.Function, false),
        new("other_function", row => row.OtherFunction, false),
        new("quantity", row => row.Quantity, false),
        new("unit", row => row.Unit, false),
        new("minimum_capacity", row => row.MinimumCapacity, false),
        new("maximum_capacity", row => row.MaximumCapacity, false),
        new("manufacturer", row => row.Manufacturer, false),
        new("other_manufacturer", row => row.OtherManufacturer, false),
        new("entities", row => row.Entities, false),
        new("installation_initial", row => row.InstallationInitial, true),
        new("maintenance_technical_support", row => row.MaintenanceTechnicalSupport, true),
        new("service_request_rfp_attachment", row => row.RfpAttachmentCount, false)
    ];

    readonly record struct FieldSpec(string Name, Func<UsacRow, string> Read, bool Checkbox);

    sealed class ServiceIdComparer : IComparer<string>
    {
        public static readonly ServiceIdComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var left = x ?? "";
            var right = y ?? "";
            var leftNumber = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue);
            var rightNumber = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue);
            if (leftNumber && rightNumber)
            {
                var numeric = leftValue.CompareTo(rightValue);
                if (numeric != 0)
                {
                    return numeric;
                }
            }

            return string.CompareOrdinal(left, right);
        }
    }
}
