using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
namespace ERateLookup.Core;

public static class DataExporter
{
    /// <summary>Worksheet name for a command-line or app lookup workbook.</summary>
    public const string LookupSheetName = "E-rate lookup";

    /// <summary>Worksheet name for a Form 470 Original vs Current diff workbook.</summary>
    public const string Form470DiffSheetName = "Form 470 diff";

    public static void WriteCsv(string path, IEnumerable<string[]> rows, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        WriteCsv(writer, rows, ct);
    }

    public static void WriteCsv(TextWriter writer, IEnumerable<string[]> rows, CancellationToken ct = default)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            writer.WriteLine(string.Join(",", row.Select(value =>
            {
                if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
                    value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            })));
        }
    }
    public static void WriteExcel(string path, List<string[]> rows, CancellationToken ct = default) =>
        WriteExcel(path, rows, LookupSheetName, ct);

    /// <summary>
    /// Writes one worksheet. Every cell is an inline string, including amounts.
    /// A value that CSV prefixes with <c>'</c> is stored as the original text.
    /// The sheet has no formula elements. The header row is frozen.
    /// </summary>
    public static void WriteExcel(string path, List<string[]> rows, string sheetName, CancellationToken ct = default)
    {
        var safeSheetName = SheetNameAttribute(sheetName);
        ct.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        WriteText(zip, "[Content_Types].xml", """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
        """);
        WriteText(zip, "_rels/.rels", """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
        """);
        WriteText(zip, "xl/workbook.xml", $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{safeSheetName}" sheetId="1" r:id="rId1"/></sheets></workbook>
        """);
        WriteText(zip, "xl/_rels/workbook.xml.rels", """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
        """);
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
        using var xml = XmlWriter.Create(stream, new()
        {
            Encoding = new UTF8Encoding(false)
        });
        xml.WriteStartDocument();
        xml.WriteStartElement("worksheet", ns);
        xml.WriteStartElement("sheetViews", ns);
        xml.WriteStartElement("sheetView", ns);
        xml.WriteAttributeString("workbookViewId", "0");
        xml.WriteStartElement("pane", ns);
        xml.WriteAttributeString("ySplit", "1");
        xml.WriteAttributeString("topLeftCell", "A2");
        xml.WriteAttributeString("activePane", "bottomLeft");
        xml.WriteAttributeString("state", "frozen");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteStartElement("cols", ns);
        xml.WriteStartElement("col", ns);
        xml.WriteAttributeString("min", "1");
        xml.WriteAttributeString("max", Math.Max(1, rows.Count > 0 ? rows[0].Length : 1).ToString());
        xml.WriteAttributeString("width", "24");
        xml.WriteAttributeString("customWidth", "1");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteStartElement("sheetData", ns);
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            xml.WriteStartElement("row", ns);
            xml.WriteAttributeString("r", (i + 1).ToString());
            for (var j = 0; j < rows[i].Length; j++)
            {
                xml.WriteStartElement("c", ns);
                xml.WriteAttributeString("r", Column(j + 1) + (i + 1));
                xml.WriteAttributeString("t", "inlineStr");
                xml.WriteStartElement("is", ns);
                xml.WriteStartElement("t", ns);
                xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                xml.WriteString(rows[i][j]);
                xml.WriteEndElement();
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        if (rows.Count > 0)
        {
            xml.WriteStartElement("autoFilter", ns);
            xml.WriteAttributeString("ref", "A1:" + Column(rows[0].Length) + rows.Count);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    public static void WriteJson<TRow>(string path, ExportMetadata metadata, IReadOnlyList<TRow> rows, CancellationToken ct = default)
    {
        using var buffer = new StringWriter();
        WriteJson(buffer, metadata, rows, ct);
        WriteCompletedText(path, buffer.ToString(), ct);
    }

    public static void WriteJson<TRow>(TextWriter writer, ExportMetadata metadata, IReadOnlyList<TRow> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(rows);
        ct.ThrowIfCancellationRequested();
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            WriteProperty(json, "form", metadata.Form);
            json.WriteBoolean("linked", metadata.Linked);
            if (metadata.Form470Count is int form470Count)
            {
                json.WriteNumber("form470Count", form470Count);
            }

            if (metadata.Form471Count is int form471Count)
            {
                json.WriteNumber("form471Count", form471Count);
            }

            WriteProperty(json, "ben", metadata.Ben);
            WriteProperty(json, "applicantName", metadata.ApplicantName);
            WriteProperty(json, "fundingYear", metadata.FundingYear);
            WriteProperty(json, "applicationNumber", metadata.ApplicationNumber);
            WriteProperty(json, "fundingRequestNumber", metadata.FundingRequestNumber);
            json.WriteString("fetchedAt", metadata.FetchedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteNumber("count", rows.Count);
            json.WritePropertyName("rows");
            json.WriteStartArray();
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                JsonSerializer.Serialize(json, row, RowJsonOptions);
            }

            ct.ThrowIfCancellationRequested();
            json.WriteEndArray();
            json.WriteEndObject();
            json.Flush();
        }

        ct.ThrowIfCancellationRequested();
        writer.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        writer.Flush();
    }

    /// <summary>
    /// Writes CSV, Excel, or JSON based on the file extension.
    /// <c>.json</c> uses <see cref="WriteJson{TRow}"/>. <c>.csv</c> uses <see cref="WriteCsv(string, IEnumerable{string[]}, CancellationToken)"/>.
    /// <c>.xlsx</c> and a path with no extension use <see cref="WriteExcel"/>.
    /// </summary>
    public static void WritePrepared(string path, List<string[]> table, ExportMetadata metadata, IReadOnlyList<ExportJsonRow> jsonRows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(jsonRows);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            WriteJson(path, metadata, jsonRows, ct);
            return;
        }

        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            WriteCsv(path, table, ct);
            return;
        }

        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || extension.Length == 0)
        {
            WriteExcel(path, table, ct);
            return;
        }

        throw new ArgumentException("Choose a .csv, .xlsx, or .json file.");
    }

    /// <summary>
    /// Replaces the extension with the save-dialog filter the user chose.
    /// <paramref name="filterIndex"/> is 1-based, matching <c>SaveFileDialog.FilterIndex</c>.
    /// </summary>
    public static string ApplySaveFilterExtension(string path, int filterIndex, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Choose a file.");
        if (extensions is null || filterIndex < 1 || filterIndex > extensions.Length)
            throw new ArgumentOutOfRangeException(nameof(filterIndex));
        var extension = extensions[filterIndex - 1];
        if (!extension.StartsWith('.'))
            extension = "." + extension;
        return Path.ChangeExtension(path, extension);
    }

    /// <summary>
    /// Same envelope as <see cref="WriteJson{TRow}"/>, with each row keyed by the supplied column names.
    /// The command-line JSON path and the app JSON path do not call this.
    /// </summary>
    public static void WriteColumnJson(string path, ExportMetadata metadata, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, CancellationToken ct = default)
    {
        using var buffer = new StringWriter();
        WriteColumnJson(buffer, metadata, columns, rows, ct);
        WriteCompletedText(path, buffer.ToString(), ct);
    }

    public static void WriteColumnJson(TextWriter writer, ExportMetadata metadata, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        var shaped = new Dictionary<string, string>[rows.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            ct.ThrowIfCancellationRequested();
            var source = rows[r];
            var shapedRow = new Dictionary<string, string>(columns.Count, StringComparer.Ordinal);
            for (var c = 0; c < columns.Count; c++)
            {
                shapedRow[columns[c]] = source.Length > c ? source[c] : "";
            }

            shaped[r] = shapedRow;
        }

        WriteJson(writer, metadata, shaped, ct);
    }

    static void WriteCompletedText(string path, string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    static void WriteProperty(Utf8JsonWriter json, string name, int? value)
    {
        if (value is int number)
        {
            json.WriteNumber(name, number);
        }
        else
        {
            json.WriteNull(name);
        }
    }

    static void WriteProperty(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }

    static readonly JsonSerializerOptions RowJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var stream = zip.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }
    static string SheetNameAttribute(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName) || sheetName.Length > 31 || sheetName.IndexOfAny([':', '\\', '/', '?', '*', '[', ']']) >= 0)
        {
            throw new ArgumentException("Excel sheet name must be 1 to 31 characters and cannot contain : \\ / ? * [ ].");
        }

        return sheetName.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string Column(int n)
    {
        var result = "";
        while (n > 0)
        {
            n--;
            result = (char)('A' + n % 26) + result;
            n /= 26;
        }
        return result;
    }
}
