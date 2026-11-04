MEETING DIRECTORY 0.3 SOURCE

Build with .NET 10 SDK on Windows, or enable Windows targeting to cross-publish.
Run checks:
    dotnet run --project Checks/Checks.csproj -c Release
Publish a self-contained x64 executable:
    dotnet publish App/App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o release
On a non-Windows build host, also pass -p:EnableWindowsTargeting=true.
The matching compiled executable is provided in the outer ZIP. No GitHub repository was created. The included workflow can build the project if its contents are placed at a repository root.

61 checks passed on Linux; interactive Windows/JAWS verification is pending for 0.3. No external test libraries or package dependencies are required. Before distributing widely, test keyboard navigation, field names, checked lists, reminder focus and sound, import/export, exceptions, time-zone views, high contrast and scaling with JAWS/NVDA/Narrator on Windows.

The data format remains a JSON meeting array. New fields have safe defaults for legacy input. Future versions must preserve these fields on round trips. Reminder delivery history is a separate local file and is intentionally not shared. Backup/Restore include all meeting settings; export strips favorites/reminders and optionally notes/passcode fields. Links may still contain passcodes. Imported meetings never overwrite existing entries. Review reminder and duplicate behavior before changing it.

Reminder limits: application must stay open, no wake or background service. Dialog alerts checked every 15 seconds. No external messages, sync, analytics or account login. The executable is unsigned.
