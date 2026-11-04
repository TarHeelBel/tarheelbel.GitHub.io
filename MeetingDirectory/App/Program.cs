using System.Diagnostics;
using MeetingDirectory;
namespace MeetingDirectoryApp;
static class Program {
    [STAThread] static void Main() {
        ApplicationConfiguration.Initialize();
        using var mutex=new Mutex(true,"Local\\MeetingDirectory-6240B2D5",out var first);
        if(!first){MessageBox.Show("Meeting Directory is already open. Close it before opening another version, or use Alt+Tab to find it.","Meeting Directory");return;}
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>MessageBox.Show("The operation could not be completed. "+e.Exception.Message,"Meeting Directory");
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MeetingDirectory","meetings.json");
        try { var store=new Store(path); Application.Run(new MainWindow(store,store.Load())); }
        catch(Exception e) {MessageBox.Show("Meeting Directory could not open. Your saved file has not been changed.\r\n"+e.Message+"\r\nData location: "+path,"Meeting Directory",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
static class Ui {
    public static T Named<T>(T c,string name) where T:Control {c.AccessibleName=name;return c;}
    public static FlowLayoutPanel Buttons(params Control[] buttons) {
        var p=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};p.Controls.AddRange(buttons);return p;
    }
    public static Button Button(string text,Action action) {var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(105,35)};b.Click+=(_,_)=>action();return b;}
    public static void Error(IWin32Window owner,Exception e)=>MessageBox.Show(owner,e.Message,"Please check",MessageBoxButtons.OK,MessageBoxIcon.Error);
    public static void Launch(string link,string platform) {Meeting.ValidateLink(link,platform);Process.Start(new ProcessStartInfo(link){UseShellExecute=true});}
}
sealed record Row(Meeting Meeting,DateTimeOffset? Next,DateOnly? OriginalDate=null,bool Today=false) {
    public override string ToString()=>Meeting.Name+"; "+Meeting.Platform+(Meeting.Favorite?"; favorite":"")+(Meeting.Category!=""?"; "+Meeting.Category:"")+"; "+(Next is {} t? t.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt")+(Today && t<DateTimeOffset.Now?"; started earlier today":""):"No upcoming occurrence");
}
sealed class MainWindow:Form {
    readonly Store store; List<Meeting> meetings;
    readonly TextBox search=Ui.Named(new TextBox{Dock=DockStyle.Fill},"Search meetings by name or notes");
    readonly ComboBox view=Ui.Named(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill},"Meeting view");
    readonly ComboBox category=Ui.Named(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill},"Filter by category");
    readonly ListBox list=Ui.Named(new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true,IntegralHeight=false},"Meetings; times are in your computer's local time zone");
    readonly TextBox details=Ui.Named(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical},"Selected meeting details");
    readonly ToolStripMenuItem favorite = new("Mark as &favorite");
    readonly List<ToolStripMenuItem> selectionCommands=[];
    readonly List<ToolStripMenuItem> viewCommands=[];
    readonly System.Windows.Forms.Timer timer=new(){Interval=15000};
    readonly ReminderLog reminderLog;
    ReminderWindow? reminders;
    bool refreshingCategories;
    public MainWindow(Store store,List<Meeting> meetings) {
        this.store=store;this.meetings=meetings;reminderLog=new ReminderLog(Path.Combine(Path.GetDirectoryName(store.PathName)!,"reminders.json"));
        Text="Meeting Directory 0.3";Font=new Font("Segoe UI",12);AutoScaleMode=AutoScaleMode.Dpi;
        Size=new Size(1050,870);MinimumSize=new Size(720,650);StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(12)};
        for(int i=0;i<10;i++)layout.RowStyles.Add(new RowStyle(i==6?SizeType.Percent:i==8?SizeType.Percent:SizeType.AutoSize,i==6?50:i==8?50:0));
        layout.Controls.Add(new Label{Text="&Search meetings:",AutoSize=true},0,0);layout.Controls.Add(search,0,1);
        layout.Controls.Add(new Label{Text="View:",AutoSize=true},0,2);layout.Controls.Add(view,0,3);
        layout.Controls.Add(new Label{Text="Cate&gory filter:",AutoSize=true},0,4);layout.Controls.Add(category,0,5);
        layout.Controls.Add(list,0,6);layout.Controls.Add(new Label{Text="Meeting &details (your local zone: "+TimeZoneInfo.Local.DisplayName+")",AutoSize=true},0,7);
        layout.Controls.Add(details,0,8);
        layout.Controls.Add(Ui.Buttons(Ui.Button("&Open meeting",Open),Ui.Button("&Add meeting",()=>Edit(null))),0,9);
        Controls.Add(layout);
        var menu=BuildMenus();MainMenuStrip=menu;Controls.Add(menu);
        view.Items.AddRange(["Today's meetings","Upcoming meetings","Favorites","All meetings"]);view.SelectedIndex=0;UpdateCategories();
        search.TextChanged+=(_,_)=>RefreshRows();view.SelectedIndexChanged+=(_,_)=>RefreshRows();category.SelectedIndexChanged+=(_,_)=>{if(!refreshingCategories)RefreshRows();};
        list.SelectedIndexChanged+=(_,_)=>ShowDetails();list.DoubleClick+=(_,_)=>Open();
        list.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Open();}};
        timer.Tick+=(_,_)=>{RefreshRows();CheckReminders();};timer.Start();
        FormClosed+=(_,_)=>{timer.Dispose();reminders?.Close();};
        Shown+=(_,_)=>{RefreshRows();list.Focus();CheckReminders();};
    }
    MenuStrip BuildMenus() {
        var bar=new MenuStrip{AccessibleName="Meeting Directory menu bar",Font=Font,Dock=DockStyle.Top,TabStop=false};
        var file=new ToolStripMenuItem("&File");
        var meeting=new ToolStripMenuItem("&Meeting");
        var views=new ToolStripMenuItem("&View");
        var help=new ToolStripMenuItem("&Help");
        ToolStripMenuItem Item(ToolStripMenuItem parent,string text,Action action,Keys shortcut=Keys.None,bool needsSelection=false) {
            var item=new ToolStripMenuItem(text){ShortcutKeys=shortcut,ShowShortcutKeys=true};
            item.Click+=(_,_)=>action();parent.DropDownItems.Add(item);
            if(needsSelection)selectionCommands.Add(item);
            return item;
        }
        Item(file,"&Paste invitation...",PasteInvitation,Keys.Control|Keys.I);
        Item(file,"&Import shared meetings...",Import);
        Item(file,"&Export selected meetings...",Export);
        file.DropDownItems.Add(new ToolStripSeparator());
        Item(file,"&Back up...",Backup);Item(file,"&Restore backup...",Restore);
        file.DropDownItems.Add(new ToolStripSeparator());Item(file,"E&xit",Close);
        Item(meeting,"&Add meeting...",()=>Edit(null),Keys.Control|Keys.N);
        Item(meeting,"&Edit meeting...",()=>{if(Selected is {} m)Edit(m);},Keys.Control|Keys.E,true);
        Item(meeting,"&Open meeting",Open,needsSelection:true);
        meeting.DropDownItems.Add(new ToolStripSeparator());
        favorite.Click+=(_,_)=>ToggleFavorite();meeting.DropDownItems.Add(favorite);selectionCommands.Add(favorite);
        Item(meeting,"Manage e&xceptions...",Exceptions,needsSelection:true);
        meeting.DropDownItems.Add(new ToolStripSeparator());Item(meeting,"&Delete meeting...",Delete,needsSelection:true);
        meeting.DropDownOpening+=(_,_)=>{
            foreach(var item in selectionCommands)item.Enabled=Selected!=null;
            favorite.Text=Selected?.Favorite==true?"Remove &favorite":"Mark as &favorite";
            favorite.Checked=Selected?.Favorite==true;
        };
        var labels=new[]{"&Today's meetings","&Upcoming meetings","&Favorites","&All meetings"};
        for(int i=0;i<labels.Length;i++) {
            int index=i;
            var item=Item(views,labels[i],()=>{view.SelectedIndex=index;list.Focus();},Keys.Control|(Keys)((int)Keys.D1+i));
            item.CheckOnClick=false;viewCommands.Add(item);
        }
        views.DropDownItems.Add(new ToolStripSeparator());
        Item(views,"&Search meetings",()=>search.Focus(),Keys.Control|Keys.F);
        Item(views,"Filter by &category",()=>category.Focus());
        Item(views,"&Refresh",()=>RefreshRows(),Keys.F5);
        views.DropDownOpening+=(_,_)=>{for(int i=0;i<viewCommands.Count;i++)viewCommands[i].Checked=view.SelectedIndex==i;};
        Item(help,"&User guide",Help,Keys.F1);
        Item(help,"&Keyboard shortcuts",()=>{using var window=new TextWindow("Keyboard shortcuts",ShortcutText);window.ShowDialog(this);});
        help.DropDownItems.Add(new ToolStripSeparator());
        Item(help,"&About Meeting Directory",()=>MessageBox.Show(this,"Meeting Directory 0.3\r\nZoom and Teams meeting organizer.\r\nIndependent application; not affiliated with Zoom or Microsoft.\r\nYour directory is stored on this computer.","About Meeting Directory"));
        bar.Items.AddRange([file,meeting,views,help]);return bar;
    }
    const string ShortcutText="Alt or F10: menu bar. Alt+F: File. Alt+M: Meeting. Alt+V: View. Alt+H: Help. Use arrows to browse menus and Enter to activate a command. Escape closes a menu.\r\n\r\nCtrl+N: Add meeting. Ctrl+E: Edit selected meeting. Ctrl+I: Paste invitation. Ctrl+F: Search. Ctrl+1: Today. Ctrl+2: Upcoming. Ctrl+3: Favorites. Ctrl+4: All meetings. F5: Refresh. F1: User guide. Enter on the meeting list: Open meeting. Tab and Shift+Tab move through the main controls. Alt+O: Open meeting button. Alt+A: Add meeting button. Alt+F4: Close window.";
    Meeting? Selected=>(list.SelectedItem as Row)?.Meeting;
    IEnumerable<string> Categories=>meetings.Select(m=>m.Category).Where(c=>c!="").Distinct(StringComparer.CurrentCultureIgnoreCase);
    void UpdateCategories() {
        var keep=category.SelectedIndex>=2?category.Text:null;var empty=category.SelectedIndex==1;
        refreshingCategories=true;category.Items.Clear();category.Items.AddRange(["All categories","Uncategorized"]);
        foreach(var c in Categories.Order(StringComparer.CurrentCultureIgnoreCase))category.Items.Add(c);
        category.SelectedIndex=keep!=null && category.Items.Contains(keep)?category.Items.IndexOf(keep):empty?1:0;refreshingCategories=false;
    }
    void RefreshRows(Guid? prefer=null) {
        if(IsDisposed)return;
        var keep=prefer??Selected?.Id;var oldDate=(list.SelectedItem as Row)?.OriginalDate;var now=DateTimeOffset.UtcNow;var query=search.Text.Trim();
        var filtered=meetings.Where(m=>m.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)||m.Notes.Contains(query,StringComparison.CurrentCultureIgnoreCase));
        if(category.SelectedIndex==1)filtered=filtered.Where(m=>m.Category=="");
        else if(category.SelectedIndex>=2)filtered=filtered.Where(m=>m.Category.Equals(category.Text,StringComparison.CurrentCultureIgnoreCase));
        IEnumerable<Row> rows;
        if(view.SelectedIndex==0) {
            var day=DateTime.Today;
            var from=Occurrences.ToInstant(day,TimeZoneInfo.Local)??new DateTimeOffset(day);
            var until=Occurrences.ToInstant(day.AddDays(1),TimeZoneInfo.Local)??new DateTimeOffset(day.AddDays(1));
            rows=filtered.SelectMany(m=>Occurrences.Between(m,from,until).Select(o=>new Row(m,o.At,o.OriginalDate,true))).OrderBy(r=>r.Next).ThenBy(r=>r.Meeting.Name);
        }else {
            if(view.SelectedIndex==2)filtered=filtered.Where(m=>m.Favorite);
            rows=filtered.Select(m=>new Row(m,Occurrences.Next(m,now)));
            rows=view.SelectedIndex==1?rows.Where(r=>r.Next!=null).OrderBy(r=>r.Next).ThenBy(r=>r.Meeting.Name):rows.OrderBy(r=>r.Meeting.Name,StringComparer.CurrentCultureIgnoreCase);
        }
        var next=rows.ToArray();
        // Avoid rebuilding an unchanged focused list on every timer tick.
        var old=list.Items.Cast<Row>().ToArray();
        if(prefer==null && next.Length==old.Length && next.Select(r=>r.ToString()).SequenceEqual(old.Select(r=>r.ToString()))){if(next.Length==0)ShowDetails();return;}
        list.BeginUpdate();list.Items.Clear();list.Items.AddRange(next);
        var found=Array.FindIndex(next,r=>r.Meeting.Id==keep && (oldDate==null || r.OriginalDate==oldDate));
        if(found<0)found=Array.FindIndex(next,r=>r.Meeting.Id==keep);
        if(list.Items.Count>0)list.SelectedIndex=found>=0?found:0;
        list.EndUpdate();ShowDetails();
    }
    void ShowDetails() {
        if(list.SelectedItem is not Row r){details.Text="No meetings in this view. Use Add, clear Search or Category, or choose All meetings.";favorite.Text="Mark as &favorite";return;}
        var m=r.Meeting;var zone=TimeZoneInfo.FindSystemTimeZoneById(m.ZoneId);favorite.Text=m.Favorite?"Remove &favorite":"Mark as &favorite";
        details.Text=$"{m.Name}\r\n{m.Platform}\r\nFavorite: {(m.Favorite?"Yes":"No")}\r\nCategory: {(m.Category==""?"Uncategorized":m.Category)}\r\n{Occurrences.Schedule(m)} at {m.Time:h:mm tt} in {zone.DisplayName}\r\nStarts: {m.Start:D}\r\nEnds: {(m.End is {} end?end.ToString("D"):"No end date")}\r\n"+
            (r.Next is {} next?$"{(r.Today?"Today's start":"Next start")} in your local time: {next.ToLocalTime():f}\r\nIn meeting time: {TimeZoneInfo.ConvertTime(next,zone):f}\r\n":"No upcoming occurrence. You can still open the link.\r\n")+
            $"Reminder: {(m.ReminderMinutes is {} minutes?minutes==0?"At meeting time":minutes+" minutes before":"Off")}; reminders require this application to stay open.\r\nExceptions: {m.Exceptions.Length}\r\nLink: {m.Link}\r\nMeeting ID: {m.MeetingId}\r\nPasscode: {m.Passcode}\r\nNotes: {m.Notes}";
    }
    void Open() {if(Selected is {} m)try{Ui.Launch(m.Link,m.Platform);}catch(Exception e){Ui.Error(this,e);}}
    void Commit(List<Meeting> next,Guid? select=null,bool reveal=false) {
        store.Save(next);meetings=next;UpdateCategories();
        if(reveal){search.Clear();category.SelectedIndex=0;view.SelectedIndex=3;}
        RefreshRows(select);list.Focus();
    }
    void Edit(Meeting? original,bool draft=false) {
        using var editor=new Editor(original,Categories,draft);
        if(editor.ShowDialog(this)!=DialogResult.OK || editor.Result is not {} result)return;
        try{var next=meetings.Where(m=>m.Id!=result.Id).ToList();next.Add(result);Commit(next,result.Id,true);}
        catch(Exception e){Ui.Error(this,e);}
    }
    void ToggleFavorite() {if(Selected is {} m)try{Commit(meetings.Select(x=>x.Id==m.Id?m with{Favorite=!m.Favorite}:x).ToList(),m.Id);}catch(Exception e){Ui.Error(this,e);}}
    void Exceptions() {
        if(Selected is not {} m)return;
        using var dialog=new ExceptionsWindow(m,(list.SelectedItem as Row)?.OriginalDate);
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{Commit(meetings.Select(x=>x.Id==m.Id?m with{Exceptions=dialog.Result}:x).ToList(),m.Id);}catch(Exception e){Ui.Error(this,e);}
    }
    void Delete() {
        if(Selected is not {} m)return;
        if(MessageBox.Show(this,$"Delete {m.Name}? This removes it from your directory and does not cancel the actual meeting.","Delete meeting",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        try{Commit(meetings.Where(x=>x.Id!=m.Id).ToList());}catch(Exception e){Ui.Error(this,e);}
    }
    void PasteInvitation() {
        using var paste=new InvitationWindow();if(paste.ShowDialog(this)!=DialogResult.OK || paste.Draft is not {} draft)return;
        Edit(draft,true);
    }
    void Export() {
        using var choose=new SelectionWindow("Export selected meetings",meetings,Selected?.Id,true);if(choose.ShowDialog(this)!=DialogResult.OK)return;
        using var save=new SaveFileDialog{Filter="Meeting Directory shared meetings (*.json)|*.json",FileName="Shared-meetings.json"};if(save.ShowDialog(this)!=DialogResult.OK)return;
        try {
            ProtectLiveData(save.FileName);
            var outgoing=choose.SelectedMeetings.Select(m=>m with{Favorite=false,ReminderMinutes=null,Notes=choose.IncludePrivate?m.Notes:"",Passcode=choose.IncludePrivate?m.Passcode:""}).ToList();
            Store.Backup(save.FileName,outgoing);MessageBox.Show(this,$"Exported {outgoing.Count} meetings. You can send the file yourself.","Export complete");
        }catch(Exception e){Ui.Error(this,e);}
    }
    void Import() {
        using var file=new OpenFileDialog{Filter="Meeting Directory shared meetings (*.json)|*.json"};if(file.ShowDialog(this)!=DialogResult.OK)return;
        try {
            var imported=Store.Read(file.FileName);
            using var choose=new SelectionWindow("Review shared meetings to import",imported,null,false);if(choose.ShowDialog(this)!=DialogResult.OK)return;
            var merge=DirectoryFeatures.Merge(meetings,choose.SelectedMeetings);
            Commit(merge.Meetings,null,true);MessageBox.Show(this,$"Added {merge.Added} meetings. Skipped {merge.Skipped} existing meetings. Your existing meetings were kept. Imported reminders are off.","Import complete");
        }catch(Exception e){Ui.Error(this,e);}
    }
    void ProtectLiveData(string path) {
        var candidate=Path.GetFullPath(path);var live=Path.GetFullPath(store.PathName);
        if(candidate.Equals(live,StringComparison.OrdinalIgnoreCase) || candidate.Equals(live+".previous",StringComparison.OrdinalIgnoreCase) || candidate.Equals(live+".before-v02",StringComparison.OrdinalIgnoreCase) || candidate.Equals(Path.Combine(Path.GetDirectoryName(live)!,"reminders.json"),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a file outside the application's live data and recovery files.");
    }
    void Backup() {
        using var dialog=new SaveFileDialog{Filter="Meeting Directory backup (*.json)|*.json",FileName="MeetingDirectory-"+DateTime.Now.ToString("yyyy-MM-dd")+".json"};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{ProtectLiveData(dialog.FileName);Store.Backup(dialog.FileName,meetings);MessageBox.Show(this,"Backup saved. It includes meeting links, passcodes, categories, favorites, reminders and exceptions.","Backup complete");}catch(Exception e){Ui.Error(this,e);}
    }
    void Restore() {
        using var dialog=new OpenFileDialog{Filter="Meeting Directory backup (*.json)|*.json"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try {
            var imported=Store.Read(dialog.FileName);
            if(MessageBox.Show(this,$"Replace your {meetings.Count} meetings with the {imported.Count} meetings in this backup? To add shared meetings instead, cancel and use Import shared meetings.","Restore backup",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)Commit(imported,null,true);
        }catch(Exception e){Ui.Error(this,e);}
    }
    void CheckReminders() {
        if(IsDisposed)return;
        var due=DirectoryFeatures.Due(meetings,DateTimeOffset.UtcNow,reminderLog.Delivered);
        if(due.Count==0)return;
        foreach(var item in due)reminderLog.Delivered.Add(item.Key);
        var logged=reminderLog.Save();
        if(reminders is null || reminders.IsDisposed) {reminders=new ReminderWindow();reminders.Show(this);}
        reminders.Add(due);System.Media.SystemSounds.Asterisk.Play();
        if(!logged)MessageBox.Show(this,"The reminder appeared, but its delivery could not be saved. It may appear again after restarting.","Reminder history");
    }
    void Help() {
        using var help=new TextWindow("Meeting Directory help",HelpText.Value+"\r\n\r\nData: "+store.PathName);help.ShowDialog(this);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys key) {
        switch(key){case Keys.Control|Keys.F:search.Focus();return true;case Keys.Control|Keys.N:Edit(null);return true;case Keys.Control|Keys.E:if(Selected!=null)Edit(Selected);return true;case Keys.Control|Keys.I:PasteInvitation();return true;case Keys.Control|Keys.D1:view.SelectedIndex=0;return true;case Keys.Control|Keys.D2:view.SelectedIndex=1;return true;case Keys.Control|Keys.D3:view.SelectedIndex=2;return true;case Keys.Control|Keys.D4:view.SelectedIndex=3;return true;case Keys.F5:RefreshRows();return true;case Keys.F1:Help();return true;}
        return base.ProcessCmdKey(ref msg,key);
    }
}
