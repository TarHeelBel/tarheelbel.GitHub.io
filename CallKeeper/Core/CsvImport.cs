using System.Globalization;
using Microsoft.VisualBasic.FileIO;
namespace MeetingDirectory;
public sealed record CsvImportResult(List<Meeting> Meetings,List<string> Errors);
public static class CsvMeetings {
    public static readonly string[] Headers=["MeetingName","MeetingLink","ZoomAccount","Platform","MeetingID","Passcode","Category","Schedule","StartDate","StartTime","TimeZone","Weekdays","MonthDay","MonthPositions","MonthWeekday","EndDate","Notes"];
    static string Key(string s)=>new(s.Trim().TrimStart('\uFEFF').Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public static CsvImportResult Parse(string text) {
        var meetings=new List<Meeting>();var errors=new List<string>();
        if(text.Length>20*1024*1024)return new([], ["CSV exceeds 20 MB."]);
        using var parser=new TextFieldParser(new StringReader(text)){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};
        parser.SetDelimiters(",");
        try {
            if(parser.EndOfData)return new([], ["The CSV is empty."]);
            var headers=parser.ReadFields()??[];var columns=new Dictionary<string,int>();
            for(int i=0;i<headers.Length;i++) {
                var key=Key(headers[i]);if(key=="")continue;
                if(!columns.TryAdd(key,i))errors.Add($"Duplicate column: {headers[i]}.");
                if(!Headers.Any(h=>Key(h)==key))errors.Add($"Unrecognized column: {headers[i]}. Use the CallKeeper template headers.");
            }
            foreach(var required in new[]{"MeetingName","MeetingLink"})if(!columns.ContainsKey(Key(required)))errors.Add($"Missing required column: {required}.");
            if(errors.Count>0)return new([],errors);
            int records=0;
            while(!parser.EndOfData) {
                var line=parser.LineNumber;var fields=parser.ReadFields()??[];
                if(fields.All(string.IsNullOrWhiteSpace))continue;
                if(++records>10000){errors.Add("The CSV exceeds 10,000 nonblank rows.");break;}
                try {
                    if(fields.Length!=headers.Length)throw new ArgumentException($"Expected {headers.Length} columns but found {fields.Length}. Quote fields containing commas.");
                    string Get(string name)=>columns.TryGetValue(Key(name),out var col)?fields[col].Trim():"";
                    string Need(string name)=>Get(name)!=""?Get(name):throw new ArgumentException($"{name} is required for this schedule.");
                    var link=Need("MeetingLink");var platform=Get("Platform");
                    if(platform==""){try{Meeting.ValidateLink(link,"Zoom");platform="Zoom";}catch{Meeting.ValidateLink(link,"Teams");platform="Teams";}}
                    else platform=platform.Equals("Zoom",StringComparison.OrdinalIgnoreCase)?"Zoom":platform.Equals("Teams",StringComparison.OrdinalIgnoreCase)?"Teams":throw new ArgumentException("Platform must be Zoom or Teams.");
                    var schedule=Key(Get("Schedule"));
                    var pattern=schedule switch {"" or "unscheduled" or "noschedule"=>Pattern.Unscheduled,"once" or "onetime"=>Pattern.Once,"daily"=>Pattern.Daily,"weekly"=>Pattern.Weekly,"monthlydate" or "monthlybydate"=>Pattern.MonthlyDate,"monthlyweekday" or "monthlybyweekday"=>Pattern.MonthlyWeekday,_=>throw new ArgumentException("Schedule must be Unscheduled, Once, Daily, Weekly, MonthlyDate or MonthlyWeekday.")};
                    DateOnly Date(string field)=>DateOnly.TryParseExact(Need(field),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:throw new ArgumentException($"{field} must use yyyy-mm-dd, for example 2026-11-04.");
                    var zone=Get("TimeZone");
                    if(pattern!=Pattern.Unscheduled && zone=="")throw new ArgumentException("TimeZone is required for a scheduled meeting.");
                    zone=zone.ToLowerInvariant() switch {"eastern" or "eastern time" or "et"=>"Eastern Standard Time","central" or "central time" or "ct"=>"Central Standard Time","mountain" or "mountain time" or "mt"=>"Mountain Standard Time","pacific" or "pacific time" or "pt"=>"Pacific Standard Time","utc"=>"UTC",_=>zone};
                    if(zone=="")zone=TimeZoneInfo.Local.Id;
                    _=TimeZoneInfo.FindSystemTimeZoneById(zone);
                    var start=pattern==Pattern.Unscheduled?(Get("StartDate")==""?new DateOnly(2000,1,1):Date("StartDate")):Date("StartDate");
                    TimeOnly time=default;
                    if(pattern!=Pattern.Unscheduled && !TimeOnly.TryParseExact(Need("StartTime"),["H:mm","HH:mm","H:mm:ss","HH:mm:ss","h:mm tt","hh:mm tt"],CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out time))throw new ArgumentException("StartTime must be 24-hour HH:mm or h:mm AM/PM.");
                    var days=pattern==Pattern.Weekly?Parts(Need("Weekdays")).Select(Weekday).Distinct().ToArray():Array.Empty<DayOfWeek>();
                    int monthDay=1;
                    if(pattern==Pattern.MonthlyDate && (!int.TryParse(Need("MonthDay"),out monthDay)||monthDay<1||monthDay>31))throw new ArgumentException("MonthDay must be 1 to 31.");
                    var positions=pattern==Pattern.MonthlyWeekday?Parts(Need("MonthPositions")).Select(Position).Distinct().ToArray():new[]{1};
                    var weekday=pattern==Pattern.MonthlyWeekday?Weekday(Need("MonthWeekday")):DayOfWeek.Tuesday;
                    var m=new Meeting{Name=Need("MeetingName"),Link=link,Platform=platform,ZoomAccount=Get("ZoomAccount"),MeetingId=Get("MeetingID"),Passcode=Get("Passcode"),Category=Get("Category"),Pattern=pattern,Start=start,Time=time,ZoneId=zone,End=Get("EndDate")==""?null:Date("EndDate"),Days=days,MonthDay=monthDay,Ordinals=positions,MonthWeekday=weekday,Notes=Get("Notes")};
                    if(platform=="Teams" && m.ZoomAccount!="")throw new ArgumentException("Leave ZoomAccount blank for Teams meetings.");
                    if(pattern==Pattern.Unscheduled && (Get("StartDate")!=""||Get("StartTime")!=""||Get("Weekdays")!=""||Get("MonthDay")!=""||Get("MonthPositions")!=""||Get("MonthWeekday")!=""||Get("EndDate")!=""))throw new ArgumentException("Choose a Schedule for supplied date/time fields, or clear those fields for an unscheduled link.");
                    m.Validate();meetings.Add(m);
                }catch(Exception e) when(e is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException){errors.Add($"CSV line {line}: {e.Message}");}
                if(errors.Count>=100){errors.Add("Stopped after 100 errors. Correct the CSV and try again.");break;}
            }
        }catch(MalformedLineException e){errors.Add($"CSV line {e.LineNumber}: invalid quoting. {e.Message}");}
        if(meetings.Count==0 && errors.Count==0)errors.Add("The CSV has no meeting rows.");
        return errors.Count>0?new([],errors):new(meetings,errors);
    }
    static string[] Parts(string value)=>value.Split([';',',','|'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
    static DayOfWeek Weekday(string value) {
        var choices=Enum.GetValues<DayOfWeek>().Where(d=>d.ToString().Equals(value,StringComparison.OrdinalIgnoreCase)||d.ToString()[..3].Equals(value,StringComparison.OrdinalIgnoreCase)).ToArray();
        return choices.Length==1?choices[0]:throw new ArgumentException($"Unknown weekday: {value}. Use a name such as Monday or Mon.");
    }
    static int Position(string value)=>value.ToLowerInvariant() switch {"1" or "first"=>1,"2" or "second"=>2,"3" or "third"=>3,"4" or "fourth"=>4,"5" or "fifth"=>5,"-1" or "last"=>-1,_=>throw new ArgumentException($"Unknown monthly position: {value}.")};
}
