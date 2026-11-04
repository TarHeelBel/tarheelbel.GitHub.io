using System.Globalization;
using System.Text.RegularExpressions;
namespace MeetingDirectory;

public sealed record MeetingException {
    public DateOnly OriginalDate {get;init;}
    public bool Skip {get;init;} = true;
    // Rescheduled date/time remains in the meeting's stored time zone.
    public DateTime? NewLocalStart {get;init;}
}
public sealed record Occurrence(DateOnly OriginalDate,DateTimeOffset At);
public sealed record ReminderItem(Guid MeetingId,string Name,string Platform,string Link,Occurrence Occurrence,int Minutes) {
    public string Key=>$"{MeetingId:N}/{Occurrence.OriginalDate:yyyy-MM-dd}/{Occurrence.At.UtcTicks}/{Minutes}";
}
public static class DirectoryFeatures {
    public static IReadOnlyList<ReminderItem> Due(IEnumerable<Meeting> meetings,DateTimeOffset now,ISet<string> delivered) {
        var result=new List<ReminderItem>();
        foreach(var m in meetings.Where(m=>m.ReminderMinutes.HasValue)) {
            var minutes=m.ReminderMinutes!.Value;
            foreach(var occurrence in Occurrences.Between(m,now.AddSeconds(-30),now.AddMinutes(minutes).AddSeconds(1))) {
                var due=occurrence.At.AddMinutes(-minutes);
                var item=new ReminderItem(m.Id,m.Name,m.Platform,m.Link,occurrence,minutes);
                // Catch a reminder on startup/resume if its meeting has not started.
                if(due<=now && occurrence.At>=now.AddSeconds(-30) && !delivered.Contains(item.Key))result.Add(item);
            }
        }
        return result.OrderBy(r=>r.Occurrence.At).ToArray();
    }
    public static (List<Meeting> Meetings,int Added,int Skipped) Merge(IEnumerable<Meeting> current,IEnumerable<Meeting> imported) {
        var result=current.ToList();int added=0,skipped=0;
        foreach(var m in imported) {
            m.Validate();
            if(result.Any(x=>x.Id==m.Id || (x.Name.Equals(m.Name,StringComparison.OrdinalIgnoreCase) && x.Link==m.Link && x.ZoomAccount.Equals(m.ZoomAccount,StringComparison.OrdinalIgnoreCase) && x.Pattern==m.Pattern && (m.Pattern==Pattern.Unscheduled || (x.Start==m.Start && x.Time==m.Time && x.ZoneId==m.ZoneId && x.End==m.End && x.Days.SequenceEqual(m.Days) && x.MonthDay==m.MonthDay && x.Ordinals.SequenceEqual(m.Ordinals) && x.MonthWeekday==m.MonthWeekday))))) {skipped++;continue;}
            // Import never enables alerts or changes personal favorites without asking.
            result.Add(m with{Favorite=false,ReminderMinutes=null});added++;
        }
        if(result.Count>10000)throw new ArgumentException("The combined directory would exceed 10,000 meetings.");
        return (result,added,skipped);
    }
}
public sealed record InvitationDraft(Meeting Meeting,string[] Detected);
public static class Invitations {
    public static string[] Links(string text)=>Regex.Matches(text.Replace("&amp;","&"),"https://[^\\s<>\\\"']+",RegexOptions.IgnoreCase)
        .Select(m=>m.Value.TrimEnd('.',',',';',')',']','}')).Where(u=> {
            try{Meeting.ValidateLink(u,"Zoom");return true;}catch{}
            try{Meeting.ValidateLink(u,"Teams");return true;}catch{}
            return false;
        }).Distinct().ToArray();
    public static InvitationDraft Parse(string text,string selectedLink) {
        if(text.Length>100000)throw new ArgumentException("Paste an invitation shorter than 100,000 characters.");
        string platform;try{Meeting.ValidateLink(selectedLink,"Zoom");platform="Zoom";}catch{Meeting.ValidateLink(selectedLink,"Teams");platform="Teams";}
        var found=new List<string>{"Meeting link","Platform"};
        string Field(string label) {
            var match=Regex.Match(text,@"(?im)^\s*(?:"+label+@")\s*:\s*(.+)$");return match.Success?match.Groups[1].Value.Trim():"";
        }
        var name=Field("Topic|Subject|Meeting name|Title");if(name!="")found.Add("Name");
        var id=Field("Meeting ID");var pass=Field("Passcode|Password");
        if(platform=="Zoom" && id=="") {
            var match=Regex.Match(new Uri(selectedLink).AbsolutePath,@"/(?:j|s)/(\d+)");if(match.Success)id=match.Groups[1].Value;
        }
        if(id!="")found.Add("Meeting ID");if(pass!="")found.Add("Passcode");
        var m=new Meeting{Name=name.Length>200?name[..200]:name,Platform=platform,Link=selectedLink,MeetingId=id,Passcode=pass,Notes=text};
        var dateMatch=Regex.Match(text,@"\b(\d{4}-\d{2}-\d{2})\b");
        if(dateMatch.Success && DateOnly.TryParseExact(dateMatch.Value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)) {m=m with{Start=date};found.Add("Start date");}
        else {
            dateMatch=Regex.Match(text,@"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+\d{1,2},?\s+\d{4}\b",RegexOptions.IgnoreCase);
            if(dateMatch.Success && DateTime.TryParse(dateMatch.Value,CultureInfo.GetCultureInfo("en-US"),DateTimeStyles.None,out var dt)) {m=m with{Start=DateOnly.FromDateTime(dt)};found.Add("Start date");}
        }
        var tm=Regex.Match(text,@"\b(\d{1,2}(?::\d{2})?\s*[AP]M)\b",RegexOptions.IgnoreCase);
        if(tm.Success && DateTime.TryParse(tm.Value,CultureInfo.GetCultureInfo("en-US"),DateTimeStyles.None,out var time)) {m=m with{Time=TimeOnly.FromDateTime(time)};found.Add("Time");}
        var zones=new[]{("Eastern Standard Time",@"\b(?:Eastern(?:\s+(?:Standard|Daylight))?\s+Time|ET|EST|EDT)\b"),("Central Standard Time",@"\b(?:Central(?:\s+(?:Standard|Daylight))?\s+Time|CT|CST|CDT)\b"),("Mountain Standard Time",@"\b(?:Mountain(?:\s+(?:Standard|Daylight))?\s+Time|MT|MST|MDT)\b"),("Pacific Standard Time",@"\b(?:Pacific(?:\s+(?:Standard|Daylight))?\s+Time|PT|PST|PDT)\b"),("UTC",@"\bUTC\b")};
        // Only inspect the invitation's time/date lines for zone abbreviations.
        var zoneText=string.Join("\n",text.Split('\n').Where(l=>Regex.IsMatch(l,@"(?i)(time\s*:|\d\s*(?:am|pm)|time\s*zone)")));
        foreach(var (zone,expression) in zones) if(Regex.IsMatch(zoneText,expression,RegexOptions.IgnoreCase)) {try{m=m with{ZoneId=TimeZoneInfo.FindSystemTimeZoneById(zone).Id};found.Add("Time zone");}catch(TimeZoneNotFoundException){}break;}
        if(Regex.IsMatch(text,@"\b(?:daily|every day)\b",RegexOptions.IgnoreCase)){m=m with{Pattern=Pattern.Daily};found.Add("Daily recurrence");}
        return new(m,found.ToArray());
    }
}
