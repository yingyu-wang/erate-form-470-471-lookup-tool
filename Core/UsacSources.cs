namespace ERateLookup.Core;

/// <summary>
/// Builds the USAC Open Data URL for one published row.
/// These links identify the dataset the lookup read. They are not EPC login pages.
/// </summary>
public static class UsacSources
{
    public const string OpenDataResource = "https://opendata.usac.org/resource/";

    public const string Form470Dataset = "jt8s-3q52";

    public const string Form471Dataset = "9s6i-myen";

    public const string FundingRequestDataset = "qdmp-ygft";

    public static string Application(FilingForm form, string applicationNumber)
    {
        var dataset = form == FilingForm.Form471 ? Form471Dataset : Form470Dataset;
        return Record(dataset, "application_number", applicationNumber);
    }

    public static string FundingRequest(string fundingRequestNumber, string applicationNumber)
    {
        if (!string.IsNullOrWhiteSpace(fundingRequestNumber))
        {
            return Record(FundingRequestDataset, "funding_request_number", fundingRequestNumber);
        }

        return Application(FilingForm.Form471, applicationNumber);
    }

    public static string Record(string datasetId, string field, string value)
    {
        var dataset = OpenDataResource + datasetId + ".json";
        if (string.IsNullOrWhiteSpace(value))
        {
            return dataset;
        }

        var quoted = UsacClient.Literal(value.Trim());
        return dataset + "?" + Uri.EscapeDataString(field) + "=" + Uri.EscapeDataString(quoted);
    }
}
