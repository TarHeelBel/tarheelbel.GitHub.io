using MeetingDirectory;
namespace MeetingDirectoryApp;
sealed record ZoneItem(TimeZoneInfo Zone) { public override string ToString()=>Zone.DisplayName; }
sealed class Editor:Form {
    public Meeting? Result {get;private set;}
    readonly Meeting original;
    bool saved;
    readonly TextBox name=Ui.Named(new TextBox(),"Meeting name");
    readonly ComboBox platform=Choice("Platform","Zoom","Teams");
    readonly TextBox link=Ui.Named(new TextBox(),"Full meeting link");
    readonly TextBox meetingId=Ui.Named(new TextBox(),"Zoom meeting ID, optional");
    readonly TextBox passcode=Ui.Named(new TextBox(),"Zoom passcode, optional");
    readonly ComboBox pattern=Choice("Schedule","One-time","Daily","Weekly","Monthly by date","Monthly by weekday");
    readonly DateTimePicker start=Ui.Named(new DateTimePicker{Format=DateTimePickerFormat.Long},"Date or recurring start date");
    readonly DateTimePicker time=Ui.Named(new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="h:mm tt",ShowUpDown=true},"Meeting time in selected meeting time zone");
    readonly ComboBox zone=Ui.Named(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},"Meeting time zone");
    readonly DateTimePicker end=Ui.Named(new DateTimePicker{Format=DateTimePickerFormat.Long,ShowCheckBox=true},"Optional end date; check to set");
    readonly CheckedListBox days=Ui.Named(new CheckedListBox{CheckOnClick=true,Height=135},"Weekly weekdays; arrow keys move, Space checks");
    readonly NumericUpDown monthDay=Ui.Named(new NumericUpDown{Minimum=1,Maximum=31},"Monthly day number");
    readonly CheckedListBox ordinals=Ui.Named(new CheckedListBox{CheckOnClick=true,Height=135},"Monthly positions; select first and third, or other positions, using Space");
    readonly ComboBox weekday=Choice("Monthly weekday",Enum.GetNames<DayOfWeek>());
    readonly CheckBox favorite=Ui.Named(new CheckBox{Text="Favorite",AutoSize=true},"Favorite meeting");
    readonly ComboBox category=Ui.Named(new ComboBox{DropDownStyle=ComboBoxStyle.DropDown,MaxLength=100},"Category; choose or type your own");
    readonly ComboBox reminder=Choice("Reminder; works while CallKeeper is open","Off","At meeting time","5 minutes before","10 minutes before","15 minutes before","30 minutes before","1 hour before","1 day before");
    readonly TextBox notes=Ui.Named(new TextBox{Multiline=true,Height=85,ScrollBars=ScrollBars.Vertical,AcceptsReturn=true},"Meeting notes");
    static ComboBox Choice(string name,params string[] values) {
        var c=Ui.Named(new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},name);c.Items.AddRange(values);c.SelectedIndex=0;return c;
    }
    public Editor(Meeting? existing,IEnumerable<string>? categories=null,bool draft=false) {
        original=existing??new Meeting();Text=draft?"Review invitation before saving":existing is null?"Add meeting":"Edit meeting";
        Font=new Font("Segoe UI",12);AutoScaleMode=AutoScaleMode.Dpi;Size=new Size(850,850);MinimumSize=new Size(650,550);StartPosition=FormStartPosition.CenterParent;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Padding=new Padding(10)};
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
        var fields=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(5)};
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));
        int row=0;
        void Field(string label,Control control) {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l=new Label{Text=label,AutoSize=true,MaximumSize=new Size(260,0),Margin=new Padding(3,10,3,3)};
            control.Dock=DockStyle.Fill;control.Margin=new Padding(3,7,3,7);control.TabIndex=row*2+1;l.TabIndex=row*2;
            fields.Controls.Add(l,0,row);fields.Controls.Add(control,1,row++);
        }
        Field("&Name:",name);Field("&Platform:",platform);Field("Meeting &link:",link);
        Field("Zoom meeting &ID (optional):",meetingId);Field("Zoom passcode (optional):",passcode);
        Field("Re&currence:",pattern);Field("&Date or start date:",start);Field("&Time in meeting time zone:",time);Field("Time &zone:",zone);
        Field("&End date (optional):",end);Field("Weekly da&ys:",days);Field("Monthly date n&umber:",monthDay);
        Field("Monthly p&ositions:",ordinals);Field("Monthly &weekday:",weekday);
        Field("Favorite:",favorite);Field("Category:",category);Field("Reminder (keep app open):",reminder);Field("&Notes:",notes);
        scroll.Controls.Add(fields);root.Controls.Add(scroll,0,0);
        var save=Ui.Button("&Save",Save);var cancel=Ui.Button("Cancel",()=>{DialogResult=DialogResult.Cancel;Close();});
        cancel.DialogResult=DialogResult.Cancel;CancelButton=cancel;
        root.Controls.Add(Ui.Buttons(save,cancel),0,1);Controls.Add(root);
        days.Items.AddRange(Enum.GetNames<DayOfWeek>());ordinals.Items.AddRange(["First","Second","Third","Fourth","Fifth","Last"]);
        foreach(var z in TimeZoneInfo.GetSystemTimeZones())zone.Items.Add(new ZoneItem(z));
        name.Text=original.Name;platform.SelectedItem=original.Platform;link.Text=original.Link;meetingId.Text=original.MeetingId;passcode.Text=original.Passcode;
        pattern.SelectedIndex=(int)original.Pattern;start.Value=original.Start.ToDateTime(TimeOnly.MinValue);
        time.Value=DateTime.Today.Add(original.Time.ToTimeSpan());end.Value=(original.End??original.Start).ToDateTime(TimeOnly.MinValue);end.Checked=original.End!=null;
        zone.SelectedIndex=zone.Items.Cast<ZoneItem>().ToList().FindIndex(z=>z.Zone.Id==original.ZoneId);
        for(int i=0;i<7;i++)days.SetItemChecked(i,original.Days.Contains((DayOfWeek)i));
        for(int i=0;i<6;i++)ordinals.SetItemChecked(i,original.Ordinals.Contains(i==5?-1:i+1));
        favorite.Checked=original.Favorite;
        category.Items.AddRange((categories??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).Order().ToArray());category.Text=original.Category;
        int[] choices=[-1,0,5,10,15,30,60,1440];var ri=Array.IndexOf(choices,original.ReminderMinutes??-1);
        if(ri<0){reminder.Items.Add(original.ReminderMinutes+" minutes before");ri=reminder.Items.Count-1;}
        reminder.SelectedIndex=ri;
        monthDay.Value=original.MonthDay;weekday.SelectedIndex=(int)original.MonthWeekday;notes.Text=original.Notes;
        if(existing is null){days.SetItemChecked((int)DateTime.Today.DayOfWeek,true);monthDay.Value=DateTime.Today.Day;}
        pattern.SelectedIndexChanged+=(_,_)=>UpdateFields();platform.SelectedIndexChanged+=(_,_)=>UpdateFields();UpdateFields();
        Shown+=(_,_)=>name.Focus();
        // Require explicit Save, leaving Enter available for multiline notes.
        FormClosing+=(_,e)=>{if(!saved && Changed() && MessageBox.Show(this,"Discard your unsaved changes?","Close meeting editor",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)e.Cancel=true;};
    }
    void UpdateFields() {
        var p=(Pattern)pattern.SelectedIndex;end.Enabled=p!=Pattern.Once;days.Enabled=p==Pattern.Weekly;monthDay.Enabled=p==Pattern.MonthlyDate;
        ordinals.Enabled=weekday.Enabled=p==Pattern.MonthlyWeekday;meetingId.Enabled=passcode.Enabled=platform.SelectedIndex==0;
    }
    Meeting Collect()=>original with {
        Favorite=favorite.Checked,Category=category.Text.Trim(),ReminderMinutes=reminder.SelectedIndex==0?null:reminder.SelectedIndex<8?new[]{0,5,10,15,30,60,1440}[reminder.SelectedIndex-1]:original.ReminderMinutes,
        Name=name.Text.Trim(),Platform=platform.Text,Link=link.Text.Trim(),MeetingId=platform.SelectedIndex==0?meetingId.Text.Trim():"",Passcode=platform.SelectedIndex==0?passcode.Text:"",Notes=notes.Text,
        Pattern=(Pattern)pattern.SelectedIndex,Start=DateOnly.FromDateTime(start.Value),Time=TimeOnly.FromDateTime(time.Value),
        ZoneId=(zone.SelectedItem as ZoneItem)?.Zone.Id??"",End=end.Enabled&&end.Checked?DateOnly.FromDateTime(end.Value):null,
        Days=days.CheckedIndices.Cast<int>().Select(i=>(DayOfWeek)i).ToArray(),MonthDay=(int)monthDay.Value,
        Ordinals=ordinals.CheckedIndices.Cast<int>().Select(i=>i==5?-1:i+1).ToArray(),MonthWeekday=(DayOfWeek)weekday.SelectedIndex
    };
    string? initial;
    protected override void OnShown(EventArgs e) {base.OnShown(e);initial=System.Text.Json.JsonSerializer.Serialize(Collect());}
    bool Changed()=>initial!=null && initial!=System.Text.Json.JsonSerializer.Serialize(Collect());
    void Save() {
        try {var m=Collect();m.Validate();Result=m;saved=true;DialogResult=DialogResult.OK;Close();}
        catch(Exception e){Ui.Error(this,e);}
    }
}
