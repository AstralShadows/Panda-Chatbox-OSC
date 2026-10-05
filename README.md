# Panda-Chatbox-OSC
Just another useless chat box


I want to give credit for the idea and UI Idea from Boihanny 

All code made by me in C# and Innosetup this is designed for easy setup and above all else UI is supposed to be simple and easy to use with a adjustable background and other adjustable things
it has full spotify support using windows media manager has fun features like PC up time other stats like PC hardware and more if you have any sugestions please feel free to let me know my discord is astral_shadows

## Status profiles and integrations

- Organize rotating statuses into named collections and choose in-order, random, or favorites-only rotation within the active collection.
- Create app-matched profiles using the foreground app name, executable, or window title. Scheduled collections take priority over app matching and support overnight time ranges.
- Pick a profile manually to override automation; choose **Auto** to return to scheduled/app-based selection.
- Use live status placeholders: `{time}`, `{date}`, `{app}` / `{game}`, `{song}`, `{player}`, `{cpu}`, `{ram}`, `{vram}`, `{battery}`, and `{uptime}`.
- Use the Integrations tab for quick chatbox content and media-source switches; keep display formatting and service configuration in Settings.
- Export or import settings backups in JSON, including statuses, collections, profiles, and schedules.

## Release notifications

The app checks the public GitHub Releases page at startup and every six hours while open. When a newer stable version is available, a separate update window appears with options to view the release, close it for now, or permanently disable update popups. The update notice below the VRChat send controls can still be used to open the release page. Updates are not downloaded or installed automatically.

Tag this build as `v3.3.1`; use semantic version tags for future releases (for example, `v3.3.2`) so installed copies can compare versions. Keep the `<Version>` in `PandaChatbox.csproj` and `AppVersion` / `VersionInfoVersion` in `PandaChatbox.iss` in sync, then upload the newly built installer to that GitHub release.
