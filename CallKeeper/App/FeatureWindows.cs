using System.Text.Json;
using MeetingDirectory;
namespace MeetingDirectoryApp;

sealed class TextWindow:Form {
    public TextWindow(string title,string text) {
        Text=title;Font=new Font("Segoe UI",12);AutoScaleMode=AutoScaleMode.Dpi;Size=new Size(800,650);StartPosition=FormStartPosition.CenterParent;
        var read=Ui.Named(new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=text},title+" text");
        var close=Ui.Button("&Close",Close);close.Dock=DockStyle.Bottom;CancelButton=close;Controls.Add(read);Controls.Add(close);Shown+=(_,_)=>{read.Focus();read.SelectionStart=0;};
    }
}
sealed class InvitationWindow:Form {
    public Meeting? Draft {get;private set;}
    readonly TextBox invitation=Ui.Named(new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,MaxLength=100000},"Paste full Zoom or Teams invitation");
    readonly ComboBox links=Ui.Named(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill},"Detected meeting link; choose if there is more than one");
    readonly TextBox status=Ui.Named(new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill},"Invitation detection results and fields needing review");
    readonly Button review; InvitationDraft? parsed;
    public InvitationWindow() {
        Text="Paste invitation";Font=new Font("Segoe UI",12);AutoScaleMode=AutoScaleMode.Dpi;Size=new Size(850,720);MinimumSize=new Size(650,500);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(12)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,65));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,35));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text="Paste the full invitation below. Analyze finds direct meeting links and suggests details for your review.",AutoSize=true},0,0);layout.Controls.Add(invitation,0,1);
        layout.Controls.Add(Ui.Button("&Analyze invitation",Analyze),0,2);layout.Controls.Add(new Label{Text="Detected &meeting links:",AutoSize=true},0,3);layout.Controls.Add(links,0,4);layout.Controls.Add(status,0,5);
        review=Ui.Button("&Review before saving",()=>{if(parsed is not null){Draft=parsed.Meeting;DialogResult=DialogResult.OK;Close();}});review.Enabled=false;
        var cancel=Ui.Button("Cancel",Close);CancelButton=cancel;layout.Controls.Add(Ui.Buttons(review,cancel),0,6);Controls.Add(layout);
        links.SelectedIndexChanged+=(_,_)=>Detect();invitation.TextChanged+=(_,_)=>{review.Enabled=false;parsed=null;links.Items.Clear();status.Text="Invitation changed. Choose Analyze invitation.";};
        Shown+=(_,_)=>invitation.Focus();
    }
    void Analyze() {
        try {
            var found=Invitations.Links(invitation.Text);links.Items.Clear();links.Items.AddRange(found);
            if(found.Length==0){status.Text="No direct Zoom or Teams HTTPS meeting link found. Paste a direct invitation; shortened or protected email links are not decoded.";status.Focus();return;}
            links.SelectedIndex=0;links.Focus();
        }catch(Exception e){Ui.Error(this,e);}
    }
    void Detect() {
        if(links.SelectedItem is not string link){review.Enabled=false;parsed=null;return;}
        try {
            parsed=Invitations.Parse(invitation.Text,link);review.Enabled=true;
            var required=new[]{"Name","Start date","Time","Time zone"}.Where(x=>!parsed.Detected.Contains(x));
            status.Text="Detected: "+string.Join(", ",parsed.Detected)+".\r\nReview all fields. Not detected: "+string.Join(", ",required)+".\r\nUnrecognized date/time fields use defaults in the editor. Recurrence must be checked manually; only explicit daily recurrence is recognized. Original invitation is kept in Notes. Nothing is saved yet.";
        }catch(Exception e){parsed=null;review.Enabled=false;Ui.Error(this,e);}
    }
}
sealed class SelectionWindow:Form {
    public List<Meeting> SelectedMeetings {get;private set;}=[];
    public bool IncludePrivate=>privateFields.Checked;
    readonly CheckedListBox list=Ui.Named(new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true},"Meetings to include; arrow keys move, Space checks");
    readonly TextBox details=Ui.Named(new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill},"Highlighted meeting details");
    readonly CheckBox privateFields=Ui.Named(new CheckBox{Text="Include notes and separate passcode fields",AutoSize=true},"Include notes and separate passcode fields; meeting links themselves can still contain passcodes");
    readonly List<Meeting> meetings;
    public SelectionWindow(string title,IEnumerable<Meeting> meetings,Guid? selected,bool exporting) {
        this.meetings=meetings.OrderBy(m=>m.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        Text=title;Font=new Font("Segoe UI",12);AutoScaleMode=AutoScaleMode.Dpi;Size=new Size(900,730);MinimumSize=new Size(650,500);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(12)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,55));layout.RowStyles.Add(new RowStyle(SizeType.Percent,45));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text=exporting?"Check meetings to export. Links may contain passcodes even when separate private fields are excluded.":"Review and check meetings to add. Existing meetings are kept; duplicates are skipped. Imported reminders stay off.",AutoSize=true},0,0);
        layout.Controls.Add(list,0,1);layout.Controls.Add(details,0,2);
        layout.Controls.Add(Ui.Buttons(Ui.Button("Select &all",()=>CheckAll(true)),Ui.Button("Select &none",()=>CheckAll(false))),0,3);
        if(exporting)layout.Controls.Add(privateFields,0,4);
        var okay=Ui.Button(exporting?"&Export checked":"&Import checked",()=>{SelectedMeetings=list.CheckedIndices.Cast<int>().Select(i=>this.meetings[i]).ToList();if(SelectedMeetings.Count==0){MessageBox.Show(this,"Check at least one meeting.","Select meetings");return;}DialogResult=DialogResult.OK;Close();});
        var cancel=Ui.Button("Cancel",Close);CancelButton=cancel;layout.Controls.Add(Ui.Buttons(okay,cancel),0,5);Controls.Add(layout);
        for(int i=0;i<this.meetings.Count;i++)list.Items.Add(this.meetings[i].Name+"; "+this.meetings[i].Platform+"; "+Occurrences.Schedule(this.meetings[i]),exporting?this.meetings[i].Id==selected:true);
        list.SelectedIndexChanged+=(_,_)=>{if(list.SelectedIndex<0)return;var m=this.meetings[list.SelectedIndex];details.Text=$"{m.Name}\r\n{m.Platform}\r\n{m.Link}\r\n{Occurrences.Schedule(m)} at {m.Time:h:mm tt}\r\nTime zone: {TimeZoneInfo.FindSystemTimeZoneById(m.ZoneId).DisplayName}\r\nCategory: {m.Category}\r\nExceptions: {m.Exceptions.Length}\r\nNotes: {m.Notes}";};
        Shown+=(_,_)=>{if(list.Items.Count>0)list.SelectedIndex=0;list.Focus();};
    }
    void CheckAll(bool value){for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,value);}
}
sealed class ExceptionsWindow:Form {
    public MeetingException[] Result {get;private set;}=[];
    readonly Meeting meeting;
    readonly List<MeetingException> exceptions;
    readonly ListBox list=Ui.Named(new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true},"Saved exceptions for this meeting");
    readonly DateTimePicker date=Ui.Named(new DateTimePicker{Format=DateTimePickerFormat.Long,Dock=DockStyle.Fill},"Original scheduled date in the meeting time zone");
    readonly ComboBox action=Ui.Named(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill},"Exception action");
    readonly DateTimePicker newDate=Ui.Named(new DateTimePicker{Format=DateTimePickerFormat.Long,Dock=DockStyle.Fill},"New date in meeting time zone");
    readonly DateTimePicker newTime=Ui.Named(new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="h:mm tt",ShowUpDown=true,Dock=DockStyle.Fill},"New time in meeting time zone");
    bool saved;
    bool dirty;
    string pending="";
    string Inputs()=>date.Value.Date.ToString("O")+"/"+action.SelectedIndex+"/"+newDate.Value.Date.ToString("O")+"/"+newTime.Value.TimeOfDay;
    public ExceptionsWindow(Meeting meeting,DateOnly? selected) {
        this.meeting=meeting;exceptions=meeting.Exceptions.ToList();
        Text="Exceptions for "+meeting.Name;Font=new Font("Segoe UI",12);AutoScaleMode=AutoScaleMode.Dpi;Size=new Size(880,800);MinimumSize=new Size(680,650);StartPosition=FormStartPosition.CenterParent;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=12,Padding=new Padding(12)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(int i=2;i<12;i++)root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label{Text="Changes affect this directory only. Use dates and times in "+TimeZoneInfo.FindSystemTimeZoneById(meeting.ZoneId).DisplayName+". Add or update an exception, then Save exceptions.",AutoSize=true},0,0);root.Controls.Add(list,0,1);
        root.Controls.Add(new Label{Text="Original scheduled &date:",AutoSize=true},0,2);root.Controls.Add(date,0,3);
        root.Controls.Add(action,0,4);root.Controls.Add(new Label{Text="New date (when changing the time):",AutoSize=true},0,5);root.Controls.Add(newDate,0,6);root.Controls.Add(new Label{Text="New time:",AutoSize=true},0,7);root.Controls.Add(newTime,0,8);
        root.Controls.Add(Ui.Buttons(Ui.Button("&Add or update exception",Add),Ui.Button("&Remove highlighted exception",Remove)),0,9);
        root.Controls.Add(new Label{Text="Removing an exception restores the normal occurrence. A moved occurrence can be on another date.",AutoSize=true},0,10);
        var save=Ui.Button("&Save exceptions",()=>{try{if(pending!="" && pending!=Inputs()){MessageBox.Show(this,"Choose Add or update exception before saving these date and time changes.","Apply exception first");return;}(meeting with{Exceptions=exceptions.ToArray()}).Validate();Result=exceptions.ToArray();saved=true;DialogResult=DialogResult.OK;Close();}catch(Exception e){Ui.Error(this,e);}});
        var cancel=Ui.Button("Cancel",Close);CancelButton=cancel;root.Controls.Add(Ui.Buttons(save,cancel),0,11);Controls.Add(root);
        action.Items.AddRange(["Skip this occurrence","Change this occurrence's date or time"]);action.SelectedIndex=0;action.SelectedIndexChanged+=(_,_)=>{newDate.Enabled=newTime.Enabled=action.SelectedIndex==1;};newDate.Enabled=newTime.Enabled=false;
        var zone=TimeZoneInfo.FindSystemTimeZoneById(meeting.ZoneId);var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.Now,zone).DateTime);
        date.Value=(selected??(today<meeting.Start?meeting.Start:today)).ToDateTime(TimeOnly.MinValue);newDate.Value=date.Value;newTime.Value=DateTime.Today.Add(meeting.Time.ToTimeSpan());
        list.SelectedIndexChanged+=(_,_)=>{if(list.SelectedIndex<0)return;var e=exceptions[list.SelectedIndex];date.Value=e.OriginalDate.ToDateTime(TimeOnly.MinValue);action.SelectedIndex=e.Skip?0:1;if(e.NewLocalStart is {} dt){newDate.Value=dt.Date;newTime.Value=dt;}pending=Inputs();};
        RefreshList();Shown+=(_,_)=>{pending=Inputs();date.Focus();};
        FormClosing+=(_,e)=>{if(!saved && (dirty || (pending!="" && pending!=Inputs())) && MessageBox.Show(this,"Discard your exception changes?","Unsaved exceptions",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)e.Cancel=true;};
    }
    void Add() {
        try {
            var d=DateOnly.FromDateTime(date.Value);
            var item=new MeetingException{OriginalDate=d,Skip=action.SelectedIndex==0,NewLocalStart=action.SelectedIndex==0?null:DateTime.SpecifyKind(newDate.Value.Date+newTime.Value.TimeOfDay,DateTimeKind.Unspecified)};
            var next=exceptions.Where(x=>x.OriginalDate!=d).Append(item).OrderBy(x=>x.OriginalDate).ToArray();
            (meeting with{Exceptions=next}).Validate();exceptions.Clear();exceptions.AddRange(next);dirty=true;RefreshList();list.SelectedIndex=exceptions.FindIndex(e=>e.OriginalDate==d);pending=Inputs();list.Focus();
        }catch(Exception e){Ui.Error(this,e);}
    }
    void Remove(){if(list.SelectedIndex<0)return;exceptions.RemoveAt(list.SelectedIndex);dirty=true;RefreshList();pending=Inputs();list.Focus();}
    void RefreshList(){list.Items.Clear();foreach(var e in exceptions)list.Items.Add($"{e.OriginalDate:D}: "+(e.Skip?"Skipped":$"Moved to {e.NewLocalStart:f}"));}
}
sealed class ReminderLog {
    readonly string path;
    public HashSet<string> Delivered {get;private set;}=[];
    public ReminderLog(string path) {
        this.path=path;
        try{if(File.Exists(path)&&new FileInfo(path).Length<2*1024*1024)Delivered=JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path))??[];}catch{ /* A damaged alert log must not block opening the directory. */ }
    }
    public bool Save() {
        var temp=path+".tmp";
        try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(temp,JsonSerializer.Serialize(Delivered.TakeLast(5000)));File.Move(temp,path,true);return true;}
        catch{return false;}
    }
}
sealed class ReminderWindow:Form {
    readonly ListBox list=Ui.Named(new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true},"Meeting reminders; select one then Open meeting");
    readonly TextBox details=Ui.Named(new TextBox{ReadOnly=true,Multiline=true,Dock=DockStyle.Fill},"Reminder details");
    readonly List<ReminderItem> items=[];
    public ReminderWindow() {
        Text="Meeting reminders";Font=new Font("Segoe UI",12);AutoScaleMode=AutoScaleMode.Dpi;Size=new Size(800,480);StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,60));layout.RowStyles.Add(new RowStyle(SizeType.Percent,40));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text="A meeting is starting soon. Opening it is your choice.",AutoSize=true},0,0);layout.Controls.Add(list,0,1);layout.Controls.Add(details,0,2);
        var open=Ui.Button("&Open meeting",Open);var dismiss=Ui.Button("&Dismiss highlighted",()=>{if(list.SelectedIndex<0)return;var i=list.SelectedIndex;items.RemoveAt(i);list.Items.RemoveAt(i);if(items.Count==0)Close();else list.SelectedIndex=Math.Min(i,items.Count-1);});var close=Ui.Button("&Close all",Close);CancelButton=close;
        layout.Controls.Add(Ui.Buttons(open,dismiss,close),0,3);Controls.Add(layout);
        list.SelectedIndexChanged+=(_,_)=>{if(list.SelectedIndex>=0){var r=items[list.SelectedIndex];details.Text=r.Name+"\r\n"+r.Platform+"\r\nStarts "+r.Occurrence.At.ToLocalTime().ToString("f")+" in your local time.";}};
        list.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Open();}};
    }
    public void Add(IEnumerable<ReminderItem> added) {
        foreach(var r in added){items.Add(r);list.Items.Add(r.Name+"; "+r.Platform+"; "+r.Occurrence.At.ToLocalTime().ToString("h:mm tt"));}
        if(list.SelectedIndex<0 && list.Items.Count>0)list.SelectedIndex=0;
        if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
        Activate();list.Focus();
    }
    void Open(){if(list.SelectedIndex<0)return;var r=items[list.SelectedIndex];try{Ui.Launch(r.Link,r.Platform);}catch(Exception e){Ui.Error(this,e);}}
}
static class HelpText {
    public const string Value="""
CALLKEEPER 0.4

MENU BAR
Press Alt or F10 for the menu bar. Alt+F opens File; Alt+M Meeting; Alt+V View; Alt+H Help. Use arrows to browse and Enter to activate. Escape closes a menu. File contains invitations, imports, exports, backup, restore and Exit. Meeting contains Add, Edit, Open, Favorite, Exceptions and Delete. View contains Today, Upcoming, Favorites, All, Search, Category and Refresh. Help contains the User guide, Keyboard shortcuts and About. Commands needing a selected meeting are disabled when none is selected. The current view and favorite state are checked in their menus.
Only Open meeting and Add meeting buttons remain on the main window. All other commands are available through the menus. Existing keyboard shortcuts continue to work.

QUICK KEYS
Ctrl+N adds a meeting. Ctrl+F searches. Ctrl+E edits the selected meeting. Ctrl+I opens Paste invitation. Ctrl+1 Today's meetings; Ctrl+2 Upcoming; Ctrl+3 Favorites; Ctrl+4 All meetings. Enter on the meeting list opens the selected link. F5 refreshes; F1 opens this help. Tab and Shift+Tab move between controls; arrows browse choices; Space checks days and monthly positions.

TODAY, UPCOMING AND FAVORITES
Today includes all starts on today's date in your computer's current local time zone, including calls that have already started. No end times or live attendance are tracked. Upcoming lists each meeting's next occurrence. Favorites lists your marked meetings even if they have no upcoming occurrence. Search and category filters apply to each view. Saving a meeting reveals it in All meetings. Meeting > Mark as favorite toggles the selected meeting; it is also available in the editor.

CATEGORIES
Choose or type one category in the meeting editor, such as Community, Work, Classes or Personal. The main window can filter by that category, All categories, or Uncategorized. Categories are optional; searching still matches name and notes.

PASTE INVITATION
Paste the full invitation and choose Analyze invitation. Choose a link if multiple links are found, then Review before saving. Supported direct Zoom and Teams links are recognized without visiting websites. Topic/subject, explicit dates in ISO or English month format, AM/PM time, common US time zones, ID, passcode and explicit daily recurrence may be suggested. Other recurrence patterns must be set manually. Missing fields have defaults: review name, date, time, time zone and recurrence before saving. The full original invitation is kept in Notes. Shortened links, protected email links, attachments and calendar files are not decoded. No invitation is saved until you press Save in the meeting editor.

OPTIONAL REMINDERS
In the meeting editor choose Off, At meeting time, 5, 10, 15 or 30 minutes before, 1 hour before, or 1 day before. Reminders work only while this application is open, including when minimized. They do not wake the computer and do not run after you exit. The app checks every 15 seconds. On startup or resume a missed advance reminder can appear if the meeting has not yet started. A reminder window and the Windows alert sound notify you; actual speech depends on your screen reader and Windows sound settings. Select a reminder and activate Open meeting, Dismiss highlighted, or Close all. A local alert log avoids repeating already delivered reminders across restarts. Imported shared meetings have reminders off until you enable them.

EXCEPTIONS
Select a meeting and choose Meeting > Manage exceptions. Enter the original scheduled date in the meeting's own time zone. Choose Skip this occurrence or Change this occurrence's date or time. For a change, choose the new date and time in the same time zone. Activate Add or update exception, then Save exceptions. Removing an exception restores the normal occurrence. This only changes the directory, never the host's actual meeting. A schedule edit that invalidates existing exceptions is rejected; first remove those exceptions if the series itself must change.

SHARING
File > Export selected meetings opens a checked list. Only checked meetings are exported. Notes and separate passcode fields are excluded unless you choose to include them, but links themselves can contain passcodes. Favorites and reminders are personal and excluded. No file is sent automatically. File > Import shared meetings lets you review and select entries; it adds new entries and skips duplicates without changing existing ones. Existing identifiers are considered duplicates; separately created entries with the same name, link and schedule are also skipped. A duplicate never overwrites your version. Use Edit for intentional changes. Imported reminders stay off and favorites stay unmarked.

BACKUP AND UPGRADE
Back up includes all meeting data. Restore replaces the directory after confirmation; Import shared meetings adds instead. Existing version 0.1 directories load automatically. Before the first 0.2 save, the prior data is retained as meetings.json.before-v02. After upgrading use the new executable: editing with the old application can discard fields it does not understand. Data files and backups include readable links and passcodes, so protect them. No cloud syncing, analytics, account system or automatic joining is included.

SCHEDULE RULES
Schedules use the stored meeting time zone and its daylight saving rules. A nonexistent spring-forward time is skipped for recurring meetings and rejected for a one-time meeting or moved occurrence. A repeated fall-back time uses its earlier occurrence once. Dates absent from a month are skipped. Exceptions can move occurrences into or out of Today and Upcoming. If two occurrences are moved to the same instant, Today shows both and reminders treat them separately.

SUPPORTED LINKS
Use complete direct HTTPS Zoom links under zoom.us, zoom.com or zoomgov.com, or Teams links at teams.microsoft.com, teams.live.com or teams.cloud.microsoft. Your configured meeting application or browser opens them. The meeting host may require sign-in. Meeting ID and passcode fields are reference information; they do not modify the link.
""";
}
