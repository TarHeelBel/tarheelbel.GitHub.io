# CallKeeper

A Windows meeting organizer designed around screen readers and keyboard navigation. Save, find and open Zoom and Microsoft Teams meetings from one directory.

## Download version 0.6.1

[Download CallKeeper 0.6.1 for Windows](https://github.com/TarHeelBel/tarheelbel.GitHub.io/releases/download/v0.6.1/CallKeeper_0.6.1_Windows.zip).

GitHub's **Build CallKeeper** workflow also builds a Windows download and publishes it under this repository's Releases after the checks pass.

Extract the downloaded ZIP and open **CallKeeper.exe**. No VS Code, PowerShell commands or separate .NET installation are needed. The application is an unsigned preview for 64-bit Windows; Windows may display a publisher notice.

## Upgrading from Meeting Directory

CallKeeper is the new name for Meeting Directory. Back up your meetings, close Meeting Directory, extract the new ZIP into a new folder, and open **CallKeeper.exe**. Your existing meetings, favorites, categories, exceptions and reminders load automatically for the same Windows user. The data directory deliberately remains `%LOCALAPPDATA%\MeetingDirectory`. The two programs share the same single-instance lock so they cannot edit that data at the same time.

## Features

- One-time, daily, weekly and monthly meetings, including first-and-third and last-weekday patterns.
- Per-meeting time zones and local-time displays.
- Today, Upcoming, Favorites and All views, plus search and categories.
- Invitation analysis with review before saving.
- Optional reminders while the application is open, including minimized.
- Skipping or moving individual occurrences.
- Selective meeting sharing and importing without replacing existing entries.
- Local storage, backup, restore and standard File, Meeting, View and Help menus.

Opening a link uses your normal Zoom or Teams application or browser. Host sign-in requirements still apply. Reminders do not run after the application exits or wake the computer.

## Keyboard use

Press **Alt** or **F10** for menus. Ctrl+N adds; Ctrl+E edits; Ctrl+I pastes an invitation; Ctrl+F searches; Ctrl+1 through Ctrl+4 switch views. Enter on the meeting list opens its link; F1 opens help.

[Read the full user guide](docs/User_Guide.txt).

## Build and validation

Use .NET 10 SDK on Windows:

```text
dotnet run --project CallKeeper/Checks/Checks.csproj -c Release
dotnet publish CallKeeper/App/App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o release
```

Run these commands from the repository root. The build runs automated checks for CSV review, recurring links, reminders, and older data. The organizer tested version 0.5 with JAWS. The new version 0.6 dialogs need hands-on screen-reader testing.

## Data and feedback

Your saved directory is stored in `%LOCALAPPDATA%\MeetingDirectory`. Backups contain readable meeting links and passcodes. Never upload personal directories, backups, passwords or private invitation text to this repository.

For feedback, use this repository's Issues page. Include the application version, Windows and screen-reader versions, steps, and the exact announcement or error with private details removed.

Independent project; not affiliated with Zoom or Microsoft.


Version 0.5 adds Ctrl+S in Add/Edit Meeting and File > Import CSV, with Zoom account labels and filtering. See docs/CSV_Import_Guide.txt and templates/CallKeeper_Import_Template.xlsx. Account labels organize your directory; they do not change Zoom sign-in.

Version 0.6 adds clipboard commands, startup settings, bulk label changes, CSV link review, and one-time replacement links.
