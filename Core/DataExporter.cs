using System.IO.Compression;
using System.Text;
using System.Xml;
namespace ERate470Lookup.Core;

public static class DataExporter
{
    public static void WriteCsv(string path,IEnumerable<string[]> rows)
    {
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        foreach(var row in rows)writer.WriteLine(string.Join(",",row.Select(value=>
        {
            if(value.Length>0&&"=+-@\t\r".Contains(value[0]))value="'"+value;
            return "\""+value.Replace("\"","\"\"")+"\"";
        })));
    }
    public static void WriteExcel(string path,List<string[]> rows)
    {
        using var file=new FileStream(path,FileMode.Create,FileAccess.Write);
        using var zip=new ZipArchive(file,ZipArchiveMode.Create);
        WriteText(zip,"[Content_Types].xml","""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
        """);
        WriteText(zip,"_rels/.rels","""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
        """);
        WriteText(zip,"xl/workbook.xml","""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="E-rate lookup" sheetId="1" r:id="rId1"/></sheets></workbook>
        """);
        WriteText(zip,"xl/_rels/workbook.xml.rels","""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
        """);
        const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();using var xml=XmlWriter.Create(stream,new(){Encoding=new UTF8Encoding(false)});
        xml.WriteStartDocument();xml.WriteStartElement("worksheet",ns);
        xml.WriteStartElement("sheetViews",ns);xml.WriteStartElement("sheetView",ns);xml.WriteAttributeString("workbookViewId","0");xml.WriteStartElement("pane",ns);xml.WriteAttributeString("ySplit","1");xml.WriteAttributeString("topLeftCell","A2");xml.WriteAttributeString("activePane","bottomLeft");xml.WriteAttributeString("state","frozen");xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndElement();
        xml.WriteStartElement("cols",ns);xml.WriteStartElement("col",ns);xml.WriteAttributeString("min","1");xml.WriteAttributeString("max",Math.Max(1,rows.Count>0?rows[0].Length:1).ToString());xml.WriteAttributeString("width","24");xml.WriteAttributeString("customWidth","1");xml.WriteEndElement();xml.WriteEndElement();
        xml.WriteStartElement("sheetData",ns);
        for(var i=0;i<rows.Count;i++)
        {
            xml.WriteStartElement("row",ns);xml.WriteAttributeString("r",(i+1).ToString());
            for(var j=0;j<rows[i].Length;j++)
            {
                xml.WriteStartElement("c",ns);xml.WriteAttributeString("r",Column(j+1)+(i+1));xml.WriteAttributeString("t","inlineStr");xml.WriteStartElement("is",ns);xml.WriteStartElement("t",ns);xml.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");xml.WriteString(rows[i][j]);xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        if(rows.Count>0){xml.WriteStartElement("autoFilter",ns);xml.WriteAttributeString("ref","A1:"+Column(rows[0].Length)+rows.Count);xml.WriteEndElement();}
        xml.WriteEndElement();xml.WriteEndDocument();
    }
    private static void WriteText(ZipArchive zip,string name,string text)
    {using var stream=zip.CreateEntry(name).Open();using var writer=new StreamWriter(stream,new UTF8Encoding(false));writer.Write(text);}
    private static string Column(int n){var result="";while(n>0){n--;result=(char)('A'+n%26)+result;n/=26;}return result;}
}
