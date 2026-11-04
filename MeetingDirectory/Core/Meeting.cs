using System.Text.Json;
namespace MeetingDirectory;
public enum Pattern { Once, Daily, Weekly, MonthlyDate, MonthlyWeekday }
public sealed record Meeting {
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Platform { get; init; } = "Zoom";
    public string Link { get; init; } = "";
    public string MeetingId { get; init; } = "";
    public string Passcode { get; init; } = "";
    public string Notes { get; init; } = "";
    public string ZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public DateOnly Start { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? End { get; init; }
    public TimeOnly Time { get; init; } = new(12,0);
    public Pattern Pattern { get; init; }
    public DayOfWeek[] Days { get; init; } = [];
    public int MonthDay { get; init; } = 1;
    public int[] Ordinals { get; init; } = [1]; // 1..5, -1 for last
    public DayOfWeek MonthWeekday { get; init; } = DayOfWeek.Tuesday;
    public bool Favorite {get;init;}
    public string Category {get;init;} = "";
    public int? ReminderMinutes {get;init;}
    public MeetingException[] Exceptions {get;init;} = [];
    public void Validate() {
        if (Id == Guid.Empty) throw new ArgumentException("Meeting identifier is missing.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length>200) throw new ArgumentException("Enter a meeting name of 1 to 200 characters.");
        if (Platform is not ("Zoom" or "Teams")) throw new ArgumentException("Choose Zoom or Teams.");
        if (Link is null || MeetingId is null || Passcode is null || Notes is null) throw new ArgumentException("Meeting text fields cannot be null.");
        ValidateLink(Link,Platform);
        var zone=TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        if(Category is null || Category.Length>100)throw new ArgumentException("Use a category of at most 100 characters.");
        if(ReminderMinutes is <0 or >1440)throw new ArgumentException("Reminder minutes must be between 0 and 1440.");
        if(Exceptions is null || Exceptions.Length>10000 || Exceptions.Any(e=>e is null))throw new ArgumentException("Meeting exceptions are invalid.");
        if(Exceptions.Select(e=>e.OriginalDate).Distinct().Count()!=Exceptions.Length)throw new ArgumentException("Only one exception is allowed per scheduled date.");
        if (!Enum.IsDefined(Pattern)) throw new ArgumentException("Choose a valid schedule.");
        if (End < Start) throw new ArgumentException("The end date must be on or after the start date.");
        if (Days is null || Ordinals is null) throw new ArgumentException("Schedule choices are missing.");
        if (Days.Any(d=>!Enum.IsDefined(d)) || !Enum.IsDefined(MonthWeekday)) throw new ArgumentException("Invalid weekday.");
        if (Pattern==Pattern.Weekly && Days.Length==0) throw new ArgumentException("Select at least one weekday.");
        if (MonthDay<1 || MonthDay>31) throw new ArgumentException("Choose a monthly date between 1 and 31.");
        if (Pattern==Pattern.MonthlyWeekday && (Ordinals.Length==0 || Ordinals.Any(n=>n != -1 && (n<1 || n>5)))) throw new ArgumentException("Select a monthly position such as first, third or last.");
        if (Pattern==Pattern.Once && Occurrences.ToInstant(Start.ToDateTime(Time),TimeZoneInfo.FindSystemTimeZoneById(ZoneId)) is null)
            throw new ArgumentException("That time does not exist because clocks move forward. Choose another time.");
        foreach(var e in Exceptions) {
            if(!Occurrences.Matches(this,e.OriginalDate))throw new ArgumentException("An exception no longer matches this meeting schedule. Remove it or keep the original schedule.");
            if(!e.Skip && (e.NewLocalStart is not {} local || Occurrences.ToInstant(local,zone) is null))throw new ArgumentException("A changed occurrence needs a valid date and time in the meeting time zone.");
        }
    }
        
    public static void ValidateLink(string link,string platform) {
        if (!Uri.TryCreate(link,UriKind.Absolute,out var u) || u.Scheme!="https" || u.UserInfo!="" || !u.IsDefaultPort)
            throw new ArgumentException("Paste a complete HTTPS meeting link.");
        var h=u.IdnHost.ToLowerInvariant();
        bool Domain(string d)=> h==d || h.EndsWith("."+d,StringComparison.Ordinal);
        var allowed=platform=="Zoom" ? Domain("zoom.us") || Domain("zoom.com") || Domain("zoomgov.com") :
            h is "teams.microsoft.com" or "teams.live.com" or "teams.cloud.microsoft";
        if (!allowed) throw new ArgumentException("The link does not match the selected meeting platform. Use a direct Zoom or Teams meeting link.");
    }
}
public static class Occurrences {
    public static bool Matches(Meeting m,DateOnly d) {
        if (d<m.Start || (m.End is {} end && d>end)) return false;
        return m.Pattern switch {
            Pattern.Once => d==m.Start,
            Pattern.Daily => true,
            Pattern.Weekly => m.Days.Contains(d.DayOfWeek),
            Pattern.MonthlyDate => d.Day==m.MonthDay,
            Pattern.MonthlyWeekday => d.DayOfWeek==m.MonthWeekday &&
                (m.Ordinals.Contains((d.Day-1)/7+1) || (m.Ordinals.Contains(-1) && d.Day+7>DateTime.DaysInMonth(d.Year,d.Month))),
            _=>false
        };
    }
    public static DateTimeOffset? ToInstant(DateTime wall,TimeZoneInfo zone) {
        wall=DateTime.SpecifyKind(wall,DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) return null;
        // Repeated fall-back hour: choose the earlier occurrence, exactly once.
        var offset=zone.IsAmbiguousTime(wall)?zone.GetAmbiguousTimeOffsets(wall).Max():zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall,offset).ToUniversalTime();
    }
    public static IReadOnlyList<Occurrence> Between(Meeting m,DateTimeOffset from,DateTimeOffset until) {
        if(until<=from)return [];
        var zone=TimeZoneInfo.FindSystemTimeZoneById(m.ZoneId);
        var result=new List<Occurrence>();
        var overridden=m.Exceptions.Select(e=>e.OriginalDate).ToHashSet();
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from,zone).DateTime);
        var stop=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(until,zone).DateTime);
        if(day<m.Start)day=m.Start;
        if(m.End is {} end && stop>end)stop=end;
        if(m.Pattern==Pattern.Once && stop>m.Start)stop=m.Start;
        for(;day<=stop;day=day.AddDays(1)) {
            if(!overridden.Contains(day) && Matches(m,day) && ToInstant(day.ToDateTime(m.Time),zone) is {} t && t>=from && t<until)result.Add(new(day,t));
            if(day==DateOnly.MaxValue)break;
        }
        // An exception can move an occurrence from any date into this range.
        foreach(var e in m.Exceptions) if(!e.Skip && e.NewLocalStart is {} local && Matches(m,e.OriginalDate) && ToInstant(local,zone) is {} t && t>=from && t<until)result.Add(new(e.OriginalDate,t));
        return result.OrderBy(x=>x.At).ThenBy(x=>x.OriginalDate).ToArray();
    }
    public static DateTimeOffset? Next(Meeting m,DateTimeOffset now) {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(m.ZoneId);
        DateTimeOffset? best=null;
        foreach(var e in m.Exceptions) {
            if(!e.Skip && e.NewLocalStart is {} local && Matches(m,e.OriginalDate) && ToInstant(local,zone) is {} instant && instant>=now && (best==null || instant<best))best=instant;
        }
        var overridden=m.Exceptions.Select(e=>e.OriginalDate).ToHashSet();
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime);
        if(day<m.Start)day=m.Start;
        // At most one regular start per day. The cap covers even a full set of skipped dates.
        for(int i=0;i<m.Exceptions.Length+740;i++) {
            if(m.End is {} end && day>end)break;
            if(m.Pattern==Pattern.Once && day>m.Start)break;
            if(!overridden.Contains(day) && Matches(m,day) && ToInstant(day.ToDateTime(m.Time),zone) is {} t && t>=now) {
                if(best==null || t<best)best=t;
                break;
            }
            if(day==DateOnly.MaxValue)break;
            day=day.AddDays(1);
        }
        return best;
    }
    public static string Schedule(Meeting m)=>m.Pattern switch {
        Pattern.Once=>$"Once on {m.Start:dddd, MMMM d, yyyy}",
        Pattern.Daily=>"Daily",
        Pattern.Weekly=>"Weekly on "+string.Join(", ",m.Days),
        Pattern.MonthlyDate=>$"Monthly on day {m.MonthDay}",
        _=>"Monthly on "+string.Join(" and ",m.Ordinals.Select(n=>n==-1?"last":new[]{"","first","second","third","fourth","fifth"}[n]))+" "+m.MonthWeekday
    };
}
public sealed class Store(string path) {
    public string PathName=>path;
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    public static List<Meeting> Parse(string text) {
        var items=JsonSerializer.Deserialize<List<Meeting>>(text,Options) ?? throw new InvalidDataException("Meeting data is empty.");
        if(items.Count>10000) throw new InvalidDataException("This directory exceeds 10,000 meetings.");
        foreach(var m in items) { if(m is null) throw new InvalidDataException("A meeting is missing."); m.Validate(); }
        if(items.Select(m=>m.Id).Distinct().Count()!=items.Count) throw new InvalidDataException("Duplicate meeting identifiers.");
        return items;
    }
    public List<Meeting> Load()=>File.Exists(path)?Read(path):[];
    public static List<Meeting> Read(string path) {
        if(new FileInfo(path).Length>20*1024*1024) throw new InvalidDataException("The backup is too large.");
        return Parse(File.ReadAllText(path));
    }
    public void Save(List<Meeting> meetings) {
        var text=JsonSerializer.Serialize(meetings,Options); _=Parse(text);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temp=path+"."+Guid.NewGuid()+".tmp";
        try {
            File.WriteAllText(temp,text);
            if(File.Exists(path)) {
                if(!File.Exists(path+".before-v02"))File.Copy(path,path+".before-v02",false);
                File.Copy(path,path+".previous",true);
            }
            File.Move(temp,path,true);
        } finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public static void Backup(string path,List<Meeting> meetings) => new Store(path).Save(meetings);
}
