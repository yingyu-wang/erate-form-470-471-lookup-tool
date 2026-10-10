using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ERate470Lookup.Core;

public sealed record WindowSchedule(int Year, DateTimeOffset? Opens, DateTimeOffset? Closes, DateTimeOffset CheckedAt, string Source)
{
    public string Message(DateTimeOffset now)
    {
        if(Opens is null)return $"FY{Year} Form 471: the exact opening date is not available in the app's verified schedule. Check USAC's official announcement and return after the filing window opens.\nAn empty lookup does not establish whether the window is open. Schedule checked: {CheckedAt.LocalDateTime:g}.";
        var dates=$"Opening: {Opens.Value:MMMM d, yyyy} (Eastern Time). Closing: {Closes:MMMM d, yyyy} (Eastern Time).";
        if(now<Opens)return $"FY{Year} Form 471 filing window has not opened. {dates}\nCome back after it opens; published data may lag submissions.";
        if(Closes is not null&&now>Closes)return $"FY{Year} Form 471 filing window has closed. {dates}\nHistorical published filings can still be searched.";
        return $"FY{Year} Form 471 filing window is open. {dates}\nYou can check published filings now; data may lag submissions.";
    }
}
public sealed class FilingWindow
{
    public const string Announcements="https://www.usac.org/e-rate/resources/announcements/";
    private readonly HttpClient http;
    private readonly string cacheFolder;
    public FilingWindow(HttpClient http,string cacheFolder){this.http=http;this.cacheFolder=cacheFolder;}
    public async Task<WindowSchedule> GetAsync(int year,CancellationToken ct)
    {
        var path=Path.Combine(cacheFolder,$"window-{year}.json");var now=DateTimeOffset.UtcNow;
        WindowSchedule? saved=null;
        try
        {
            if(File.Exists(path))saved=JsonSerializer.Deserialize<WindowSchedule>(await File.ReadAllTextAsync(path,ct));
            if(saved is not null&&now>=saved.CheckedAt&&now-saved.CheckedAt<TimeSpan.FromHours(24))return saved;
        }
        catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){}
        var url=$"https://www.usac.org/e-rate/resources/e-rate-productivity-center/funding-year-{year}-filing-window/";
        WindowSchedule schedule;
        try
        {
            using var response=await http.GetAsync(url,ct);
            schedule=response.IsSuccessStatusCode?Parse(await response.Content.ReadAsStringAsync(ct),year,now,url):new(year,null,null,now,Announcements);
        }
        catch(HttpRequestException){return saved??new(year,null,null,now,Announcements);}
        // Never discard previously verified dates because a page moved or markup changed.
        if(schedule.Opens is null&&saved?.Opens is not null)return saved;
        try{Directory.CreateDirectory(cacheFolder);await File.WriteAllTextAsync(path,JsonSerializer.Serialize(schedule),ct);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        return schedule;
    }
    public static WindowSchedule Parse(string html,int year,DateTimeOffset checkedAt,string source)
    {
        var text=WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," "));
        text=Regex.Replace(text,@"\s+"," ");
        var title=Regex.Match(text,$@"Funding Year\s+{year}\s+Filing Window",RegexOptions.IgnoreCase);
        if(!title.Success)return new(year,null,null,checkedAt,Announcements);
        var months="(?:January|February|March|April|May|June|July|August|September|October|November|December)";
        var date=$@"{months}\s+\d{{1,2}},\s*{year}";
        // Only accept an explicit date range directly after this year's window heading.
        var match=Regex.Match(text.Substring(title.Index+title.Length,Math.Min(300,text.Length-title.Index-title.Length)),$@"(?<open>{date})\s*[–—-]\s*(?<close>{date})",RegexOptions.IgnoreCase);
        if(!match.Success||!DateTime.TryParse(match.Groups["open"].Value,CultureInfo.GetCultureInfo("en-US"),DateTimeStyles.None,out var opening)||!DateTime.TryParse(match.Groups["close"].Value,CultureInfo.GetCultureInfo("en-US"),DateTimeStyles.None,out var closing)||closing<opening)return new(year,null,null,checkedAt,Announcements);
        var eastern=TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        DateTimeOffset Convert(DateTime d)=>new(d, eastern.GetUtcOffset(d));
        return new(year,Convert(opening.Date),Convert(closing.Date.AddDays(1).AddTicks(-1)),checkedAt,source);
    }
}
