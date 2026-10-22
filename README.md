# JelliBox — Jellyfin client for Xbox Series X

Clean, dark, 10-foot UI. Native UWP (C# + XAML), sideloaded via Xbox Developer Mode.  
Design: Netflix/Apple TV aesthetic — #0D0D0D background, #00A4DC Jellyfin-blue accent, Segoe UI.

## Features

- Hero banner (backdrop + logo) on home screen
- Continue Watching carousel
- Per-library "Latest" carousels
- Detail page — backdrop, poster, overview, play/resume/mark-watched
- Full-screen video player with Jellyfin scrobbling (start/progress/stop)
- Direct-play preferred; falls back to HLS transcode
- Gamepad navigation throughout (A = play/pause, B = back, LB/RB = seek)
- Credentials stored in Windows Credential Vault

## Prerequisites

| Tool | Version |
|------|---------|
| Windows 10/11 | 22H2+ recommended |
| Visual Studio 2022 | 17.x, with **Universal Windows Platform** workload |
| Xbox One / Series X | Developer Mode enabled |

> **Xbox Developer Mode**: Settings → System → Console info → Register for Xbox Insider  
> then Dev Mode Activation app from the Store.

## Build

```
1. Open JelliBox.sln in Visual Studio 2022
2. Restore NuGet packages (CommunityToolkit.Mvvm 8.3.2 auto-restores)
3. Set platform to x64 (Xbox Series X is x64)
4. Build → Release | x64
```

### Required assets (create before first build)

Place these PNGs in `JelliBox/Assets/` — standard UWP icon sizes:

| File | Size |
|------|------|
| `Square44x44Logo.png` | 44 × 44 |
| `Square150x150Logo.png` | 150 × 150 |
| `Wide310x150Logo.png` | 310 × 150 |
| `StoreLogo.png` | 50 × 50 |
| `SplashScreen.png` | 620 × 300 |

Quick placeholder (PowerShell, run once):
```powershell
$sizes = @(
  @{name="Square44x44Logo";w=44;h=44},
  @{name="Square150x150Logo";w=150;h=150},
  @{name="Wide310x150Logo";w=310;h=150},
  @{name="StoreLogo";w=50;h=50},
  @{name="SplashScreen";w=620;h=300}
)
Add-Type -AssemblyName System.Drawing
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s.w,$s.h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::FromArgb(13,13,13))
  $bmp.Save("Assets\$($s.name).png", [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}
```

## Sideload to Xbox

1. On Xbox: **Dev Home** → My Games & Apps → note the **Device Portal** URL (e.g. `http://192.168.x.x:11443`)
2. In Visual Studio: **Project → Properties → Debug** → set "Remote Machine" to that IP
3. **Or** deploy manually:
   - Build → Create App Packages → Sideloading (skip signing)
   - Open Device Portal in browser → Apps → Deploy → upload the generated `.msix` / `.appxbundle`
4. Launch from Dev Home → My Games & Apps

## Connecting to Jellyfin

- Enter your **local network URL**: `http://192.168.x.x:8096` (or your domain if exposed)
- The Xbox and your Jellyfin server must be on the same network (or VPN)
- Token is stored in Credential Vault; tap "⏏" on Home to sign out

## Architecture

```
App.xaml.cs          — Service singletons (Api, Settings, Navigation)
Services/
  JellyfinApiService — All Jellyfin REST calls + scrobbling
  SettingsService    — LocalSettings + PasswordVault
  NavigationService  — Frame wrapper
Models/
  MediaItem          — Jellyfin item DTO
  PlaybackInfo       — Auth + playback result types
ViewModels/          — ObservableObject + RelayCommand (CommunityToolkit.Mvvm)
Views/               — XAML pages + minimal code-behind
Controls/
  MediaCard          — Reusable card: thumbnail, title, progress, focus animation
Styles/AppStyles.xaml — Colours, brushes, button/textbox styles
Converters.cs        — Bool/Null/String → Visibility converters
```
