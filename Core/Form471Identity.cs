namespace ERateLookup.Core;

public sealed class Form471IdentityMismatchException : InvalidOperationException
{
    public Form471IdentityMismatchException(string message) : base(message)
    {
    }

    public string? ApplicationNumber
    {
        get; init;
    }
    public string RequestedBen { get; init; } = "";
    public string EpcOrganizationId { get; init; } = "";
    public string ReportedBen { get; init; } = "";
}

public static class Form471Identity
{
    public static void Validate(string requestedBen, IReadOnlyList<UsacRow> rows)
    {
        foreach (var row in rows)
        {
            var application = string.IsNullOrWhiteSpace(row.Number) ? "(missing application number)" : row.Number;
            var epc = row.EpcOrganizationId?.Trim() ?? "";
            var reportedBen = row.Ben?.Trim() ?? "";
            if (epc.Length == 0)
            {
                throw new Form471IdentityMismatchException(
                    $"Form 471 application {application} did not include epc_organization_id, so it cannot be confirmed as BEN {requestedBen}. This is not a no-filings result.")
                {
                    ApplicationNumber = row.Number,
                    RequestedBen = requestedBen,
                    EpcOrganizationId = epc,
                    ReportedBen = reportedBen
                };
            }

            if (!string.Equals(epc, requestedBen, StringComparison.Ordinal))
            {
                throw new Form471IdentityMismatchException(
                    $"Form 471 application {application} epc_organization_id '{epc}' does not match requested BEN {requestedBen}. The row was not treated as a filing for this BEN.")
                {
                    ApplicationNumber = row.Number,
                    RequestedBen = requestedBen,
                    EpcOrganizationId = epc,
                    ReportedBen = reportedBen
                };
            }

            if (reportedBen.Length > 0 && !string.Equals(reportedBen, epc, StringComparison.Ordinal))
            {
                throw new Form471IdentityMismatchException(
                    $"Form 471 application {application} billed entity '{reportedBen}' does not match epc_organization_id '{epc}' (requested BEN {requestedBen}).")
                {
                    ApplicationNumber = row.Number,
                    RequestedBen = requestedBen,
                    EpcOrganizationId = epc,
                    ReportedBen = reportedBen
                };
            }

            row.Ben = epc;
        }
    }

    public static void ValidatePublishedRows(IReadOnlyList<UsacRow> rows)
    {
        foreach (var row in rows)
        {
            var application = string.IsNullOrWhiteSpace(row.Number) ? "(missing application number)" : row.Number;
            var epc = row.EpcOrganizationId?.Trim() ?? "";
            var reportedBen = row.Ben?.Trim() ?? "";
            if (epc.Length == 0)
            {
                throw new Form471IdentityMismatchException(
                    $"Form 471 application {application} did not include epc_organization_id, so the applicant cannot be confirmed. This is not a no-filings result.")
                {
                    ApplicationNumber = row.Number,
                    RequestedBen = "",
                    EpcOrganizationId = epc,
                    ReportedBen = reportedBen
                };
            }

            if (reportedBen.Length > 0 && !string.Equals(reportedBen, epc, StringComparison.Ordinal))
            {
                throw new Form471IdentityMismatchException(
                    $"Form 471 application {application} billed entity '{reportedBen}' does not match epc_organization_id '{epc}'. The row was not treated as a confirmed applicant.")
                {
                    ApplicationNumber = row.Number,
                    RequestedBen = "",
                    EpcOrganizationId = epc,
                    ReportedBen = reportedBen
                };
            }

            row.Ben = epc;
        }
    }
}
