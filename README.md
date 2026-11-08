# Dynamic Island for Windows 🏝️

[![Build & Test](https://github.com/SPARK-REX/Dynamic-island/actions/workflows/build.yml/badge.svg)](https://github.com/SPARK-REX/Dynamic-island/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![UI Framework](https://img.shields.io/badge/UI-WPF%20%2B%20DirectX%20Vectors-FF6F00)](https://github.com/dotnet/wpf)
[![Audio](https://img.shields.io/badge/Audio-NAudio%20%2B%20WASAPI-26A69A)](https://github.com/naudio/NAudio)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A fluid, ultra-responsive, and lightweight **Dynamic Island** overlay for Windows 10 and Windows 11. Built with **C#**, **.NET 10**, and **WPF vector geometry**, it brings Apple's iconic morphing island experience to desktop Windows with native media control, volume HUD, focus timers, camera/mic privacy indicators, freeform dragging with edge docking, and toast notifications.

---

## ✨ Features

### 🎵 Media Player Integration
- **Compact & Expanded Modes:** Displays mini album art, title, and a live animated 4-bar equalizer when music is playing. Hovering smoothly morphs the island into a full interactive media controller.
- **Universal Source Support:** Seamlessly syncs with Spotify, Apple Music, YouTube Music, Chrome, Edge, VLC, Tidal, and any Windows Media Session (SMTC) source.
- **App Launch & Relaunch:** Clicking the expanded media card brings the active audio player to focus. If closed, pressing play automatically relaunches your last played app and resumes playback.
- **Tactile Transport Controls:** Micro-animated spring-physics buttons for Previous, Play/Pause, and Next.

### 🕒 Smart Scroll-Wheel Views
Inside the expanded island, scroll your mouse wheel or swipe your trackpad to cycle between three views:
1. **Now Playing Card:** Full track metadata, album art, and playback controls.
2. **Focus & Pomodoro Timer:** Quick stepper and countdown trigger.
3. **Big Clock & Calendar:** High-contrast time and date display with zero layout jitter.

### ⏱️ Focus Timer & Custom Dialog
- **Dynamic Countdown Pill:** Shows remaining time directly on the island alongside music bars.
- **Tactile Picker Dialog (`TimerDialog`):** Interactive popout with preset chips (15, 25, 45, 60, 90 min), dynamic interactive drag slider, stepper buttons, and keyboard navigation (`Enter` to start, `Esc` to exit, `Arrow Keys` to adjust).
- **Completion Chime:** Plays a native chime when the timer finishes.

### 🔊 Core Audio Volume HUD
- Real-time master volume listener using **NAudio (WASAPI)**.
- Replaces clunky OS flyouts with a sleek, minimalist volume slider and mute status indicator that dismisses automatically after 1.5 seconds.

### 🛡️ Real-Time Privacy Indicators
- Real-time hardware capability monitoring via Windows Consent Store.
- **Microphone in use:** Glowing orange dot (`#FF9F0A`).
- **Camera / Webcam in use:** Glowing green dot (`#30D158`).
- The island dynamically widens to accommodate privacy dots when active.

### 🔔 Toast & Push Notifications
- Intercepts system push notifications and displays an elegant morphing pill with the app icon/badge, sender title, and message snippet.

### 🎯 Freeform Dragging & Edge Snapping
- **Freely Draggable:** Click and drag the island anywhere on your display or multi-monitor setup to position it wherever you like.
- **Smart Edge Docking:** Dragging near screen borders automatically rotates and docks the island along top, bottom, or side edges.
- **⚡ Instant Re-center (Double-Click):** Double-click anywhere on the island to instantly reset its orientation and snap it right back to top-center!

### ⚡ Extreme Performance & Low Footprint
- **Backdrop Blur:** the island blurs whatever is behind it (Windows 10 2004+). Hidden from screenshots/screen recording as a side effect.
- **Direct Geometry Animation:** Driven directly by vector math on `RectangleGeometry` rather than layout reflows, ensuring buttery 120+ FPS animations on high-refresh monitors.
- **Idle Zero CPU & Memory Diet:** Aggressive background memory compaction (`System.GC.ConserveMemory=5`) reduces idle RAM usage to bare minimums.
- **Overlay Plumbing:** Configured with `WS_EX_TOOLWINDOW` (hidden from Alt-Tab) and `WS_EX_NOACTIVATE` (never steals focus from active games or typing).
- **Single-Instance Mutex:** OS-level atomic Mutex prevents duplicate background processes.

---

## 🤖 AI Voice Assistant (Nixi)

Nixi is a built-in voice assistant that lives on the island. It supports **two modes** — choose whichever fits your workflow and privacy needs.

### 🧠 AI Modes

| Mode | Backend | Requirements | Privacy | Latency |
|---|---|---|---|---|
| **Cloud · Gemini** | Google Gemini 2.5 Flash (API) | Internet + Gemini API key | Audio sent to Google | ~500–1500 ms |
| **Local · Ollama** | Your own model on this PC (via Ollama) | Ollama installed + model pulled | Fully offline, audio never leaves your machine | ~1–3 s (CPU) / ~500 ms (GPU) |

> **Both modes** support the same tool-calling capabilities: open/close any app, media control, volume, screenshots, brightness, web search, time/date/battery, and conversational chat.

---

### ☁️ Cloud Mode — Google Gemini

**Best for:** Highest accuracy, natural conversation, low-latency on decent connections.

#### Get a Gemini API Key (Free Tier)

1. Go to **[Google AI Studio](https://aistudio.google.com/apikey)** (or [Vertex AI](https://cloud.google.com/vertex-ai/generative-ai/docs/start/quickstarts/quickstart-multimodal) for enterprise).
2. Click **"Create API Key"** → copy the key.
3. In Dynamic Island:  
   **Settings → Voice → Assistant model → Cloud · Gemini → "Set / Change Gemini API Key…"**  
   Paste the key and save.

**Or manually:** Create a file named `gemini.key` (one key per line, `#` for comments) in any of these locations — the app auto-discovers it:
```
%APPDATA%\DynamicIsland\gemini.key
<app-folder>\gemini.key
```
You can store multiple keys; the app rotates automatically on rate limits.

**Free tier limits:** ~15 requests/minute, 1,500 requests/day. Paid plans remove quotas.

---

### 🏠 Local Mode — Ollama (Fully Offline)

**Best for:** Total privacy, zero cloud dependency, works on air-gapped machines.

#### One-Time Setup

1. **Install Ollama**  
   Download from **[ollama.com](https://ollama.com/download/windows)** and run the installer.  
   (It installs to `%LOCALAPPDATA%\Programs\Ollama\ollama.exe` and adds itself to PATH.)

2. **Pull a tool-calling model** (recommended):
   ```cmd
   ollama pull qwen2.5:7b
   ```
   Lightweight alternatives: `qwen2.5:3b`, `llama3.2:3b`, `qwen2.5:1.5b` (faster, slightly less capable).

3. **Start the server** (usually auto-starts, but you can run manually):
   ```cmd
   ollama serve
   ```
   Default endpoint: `http://localhost:11434/v1` (OpenAI-compatible).

4. **Switch Dynamic Island to Local mode:**  
   **Settings → Voice → Assistant model → Local · Ollama**  
   The app will auto-start `ollama serve` if needed and pull the model on first use.

#### Customize Local Settings

| Setting | Default | Where to change |
|---|---|---|
| **Model name** | `qwen2.5:7b` | Settings → Voice → Local model name |
| **Endpoint** | `http://localhost:11434/v1` | Settings → Voice → Local endpoint |

> 💡 **Tip:** For best results, use models tagged with tool/function calling support (Qwen 2.5, Llama 3.2, etc.). The default `qwen2.5:7b` balances speed and capability on modern CPUs; drop to `qwen2.5:3b` for snappier responses on older hardware.

---

### ⌨️ Voice Activation

| Method | How to enable | Trigger |
|---|---|---|
| **Push-to-Talk** | Settings → Voice → Push to talk (ON) | Hold **Caps Lock** (default) or your custom hotkey |
| **Wake Word** | Settings → Voice → Wake word (ON) | Say **"Hey Nixi"**, **"Nixi"**, or **"Island"** |

- **Push-to-Talk presets:** Caps Lock, `Ctrl+Space`, `Alt+V`, Mouse 4 — or define any custom key/combo.
- **Wake phrase options:** "Hey Nixi" (default), "Nixi", "Island".
- Both can be active simultaneously; push-to-talk takes priority while held.

---

### 🎯 What Nixi Can Do (Both Modes)

| Category | Example Commands |
|---|---|
| **Apps** | "Open Spotify", "Launch Chrome", "Close Word", "Quit Discord" |
| **Media** | "Play", "Pause", "Next track", "Previous song", "Volume 50", "Mute" |
| **System** | "Screenshot", "Brightness 80%", "Dim screen", "What time is it", "Battery status" |
| **Web** | "Search for best mechanical keyboards", "Google weather Tokyo" |
| **Chat** | "Tell me a joke", "How are you", "Explain quantum computing in one sentence" |

All voice replies are spoken aloud (Gemini TTS in Cloud mode, Windows SAPI in Local mode) **and** shown as a toast on the island.

---

## 🎨 Display & Mascot Settings

The **Display** tab in Settings (`Settings → Display`) lets you customize the island's look and the optional animated mascot.

### 🏝️ Island Theme & Layout

| Setting | Options | Description |
|---|---|---|
| **Look & Feel** | Glassy / Pure Black | Glassy uses Windows backdrop blur (Mica/Acrylic); Pure Black is a solid opaque pill. |
| **Clock Format** | 12-hour / 24-hour | Time display on the island status bar. |
| **Island Scale** | 80% / 100% / 120% | Global size multiplier for the pill and all internal content. |
| **Island Position** | Top Left / Top Center / Top Right | Default screen anchor; double-click anytime to snap back to Top Center. |

---

### 🧸 Mascot (Animated Eyes)

Replace the standard island panels with a pair of expressive animated eyes that react to system state and voice interaction.

| Setting | Options | Description |
|---|---|---|
| **Enable Mascot** | On / Off | Toggle the mascot entirely. When off, standard media/timer/clock panels show. |
| **Accessory** | None / Shades / Bow | Cosmetic add-on for the mascot. |
| **Sleep Until Tapped** | On / Off | When enabled, a sleeping mascot ignores mouse hover and only wakes with a startle animation on click/tap. |
| **Keep Mascot Awake** | On / Off | Prevents the mascot from ever falling asleep (idle animation stays active). |
| **Privacy Dots** | On / Off | Shows microphone (orange) and camera (green) indicator dots on the mascot when hardware is in use. |
| **Listening Animation** | Glowing Ring / Ripple Glow / Rainbow Glow / Side Waves | Visual feedback while Nixi is listening to your voice command. |
| **Eye Color** | 24 presets + Custom picker | Choose from a curated palette (blues, greens, yellows, reds, pinks, purples, white, silver) or open the system color dialog for any hex color. |

> 💡 **Mascot behavior:** The mascot idles with subtle breathing blinks, reacts to media playback (subtle bounce on beat), shows privacy dots when mic/camera are active, and plays the selected listening animation during voice input. "Sleep Until Tapped" and "Keep Awake" are mutually exclusive in practice — enable one or the other.

---

## 🎮 Gestures & Controls

> [!TIP]
> 🎯 **Highlight: Instant Re-Center via Double-Click**  
> You can freely drag and dock the island anywhere across your monitors. Whenever you want to bring it back to its default location, simply **double-click anywhere on the island** to immediately reset its rotation and snap it back to **top-center**!

| Action | Gesture / Input | Description |
|---|---|---|
| **Re-center Island** | ⚡ **Double-click the island** | **Instantly resets rotation and snaps back to top-center** |
| **Move / Drag Island** | Left-click + drag anywhere | Freely position the island anywhere on your monitor |
| **Snap & Dock to Edges** | Drag near top, bottom, or side borders | Docks the island along any screen border with adapted orientation |
| **Expand Media Card** | Hover cursor over the island | Reveals full album art, track details, and playback controls |
| **Cycle Views (Expanded)** | Mouse wheel / 2-finger scroll | Smoothly switches between Now Playing, Focus Timer, and Big Clock |
| **Open Music App** | Left-click on media card | Focuses or restores the active audio application (Spotify, Chrome, etc.) |
| **Adjust Focus Timer** | `↑` / `↓` (±1 min), `←` / `→` (±5 min) | Quick stepper adjustment via keyboard or mouse wheel |
| **Toggle Voice (Push-to-Talk)** | Hold **Caps Lock** (default) | Activates Nixi's microphone while held |
| **Wake Word** | Say **"Hey Nixi"** | Hands-free voice activation |
| **Tray Context Menu** | Right-click the system tray icon | Access presets, clock formats, startup toggle, and settings |

---

## 🚀 Installation & Setup

### Prerequisites & Runtime Requirements

Dynamic Island is built on **.NET 10**. Choose your preferred installation option:

| Distribution Type | Requirements | Notes |
|---|---|---|
| **GitHub Release (Standard)** | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) (~30 MB) | Lightweight binary package, fast download |
| **Self-Contained Build** | None (Zero prerequisites) | Bundles the .NET runtime into the application folder |
| **Build from Source** | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Full developer build environment |

> [!NOTE]
> If you do not have the .NET 10 Desktop Runtime installed, Windows will prompt you with a direct download link on first launch.

---

### Option 1: Download from GitHub Releases (Recommended for Users)

1. Download the latest release `.zip` from [GitHub Releases](https://github.com/SPARK-REX/Dynamic-island/releases).
2. Extract the archive to any folder of your choice (e.g. `C:\Tools\DynamicIsland`).
3. Double-click **`DynamicIsland.exe`**.

> [!IMPORTANT]
> ### 🛡️ Windows SmartScreen Notice
> Because this is a free, open-source application without an expensive commercial code-signing certificate, Windows Defender SmartScreen may display:
> **"Windows protected your PC"**
> 
> **How to proceed:**
> 1. Click **"More info"**
> 2. Click **"Run anyway"**
> 
> The complete source code is 100% open-source and public on GitHub for full verification and transparency.

---

### Option 2: Building from Source (Developers)

1. **Clone the repository:**
   ```cmd
   git clone https://github.com/SPARK-REX/Dynamic-island.git
   cd Dynamic-island
   ```

2. **Build and launch:**
   ```cmd
   rebuild.bat
   ```
   > [!TIP]
   > `rebuild.bat` automatically verifies prerequisites, stops any running instances, cleans and publishes a fresh Release build to `publish\current`, and launches Dynamic Island!

---

## 🔔 Enabling Notification Access (Windows 10 & 11)

For Dynamic Island to capture and display push toast notifications (from Telegram, WhatsApp, Discord, Slack, etc.), Windows requires Notification Access permission:

1. Open **Windows Settings** (`Win + I`).
2. Go to **Privacy & security** > **Notifications** (or **App permissions** > **Notifications**).
3. Ensure **"Let desktop apps access your notifications"** (or **"Notification access"**) is toggled **ON**.

*(If notifications are disabled at the OS level, media controls, volume HUD, and focus timer will continue to function normally without interruption).*

---

## ⚙️ Configuration & System Tray

Right-click the **Dynamic Island** tray icon in your Windows Taskbar system tray to access:
- **12-Hour / 24-Hour Clock:** Switch time format instantly.
- **Focus Timer Presets:** Trigger 5, 10, 15, 25, 45, 60-minute timers or open custom picker.
- **Hide / Show Island:** Temporarily toggle island visibility (or double-click the tray icon).
- **Test Notification:** Preview how toast alerts morph the island.
- **Start with Windows:** Toggle automatic startup at user logon.
- **Exit:** Completely terminate the application.

---

## 📂 Repository Structure

```text
Dynamic-island/
├── .github/workflows/       # GitHub Actions CI automated build & test workflow
├── docs/                    # Installation and Contributing documentation
│   ├── INSTALLATION.md      # Detailed setup, SmartScreen & permissions guide
│   └── CONTRIBUTING.md      # Developer guide & PR rules
├── src/                     # Main source code and application solution
│   ├── DynamicIsland.sln    # Solution file
│   ├── DynamicIsland.csproj # .NET 10 WPF project configuration
│   ├── Program.cs           # App entrypoint and Mutex single-instance lock
│   ├── MainWindow.xaml      # Island UI vector markup
│   ├── MainWindow.xaml.cs   # Core state machine, layout, and animations
│   ├── MainWindow.Media.cs  # SMTC media controls, track persistence, and app relaunch
│   ├── MainWindow.Notifications.cs # Toast engine, focus timer, and privacy indicators
│   ├── MainWindow.Volume.cs # Core Audio WASAPI volume listener and HUD
│   ├── ClipboardDialog.xaml # Clipboard history popup card UI
│   ├── ClipboardDialog.xaml.cs # Clipboard popup logic & animations
│   ├── TimerDialog.xaml     # Focus timer custom picker dialog UI
│   ├── TimerDialog.xaml.cs  # Timer picker interaction logic
│   ├── IslandLogic.cs       # Pure UI math, geometry, and parsing logic
│   ├── NativeMethods.cs     # P/Invoke Win32 definitions
│   ├── Services/            # Decoupled domain services (Clipboard, Audio, Media, Timer, etc.)
│   └── assets/              # Preview screenshots, GIFs, and media
├── rebuild.bat              # 1-click build and launch script
├── README.md                # Project documentation
└── LICENSE                  # MIT License
```

---

## 📁 Storage & Logs

Dynamic Island stores lightweight user preferences and error diagnostics in:
```text
%AppData%\DynamicIsland\
├── clock.txt       # Time format preference (12h/24h)
├── last.json       # Last played media session metadata
├── last.png        # Cached thumbnail art
└── error.log       # Self-pruning error log (capped at 200 KB)
```

---

## 👤 Author

Developed by **Akhin (SPARK-REX)**:
- GitHub: [@SPARK-REX](https://github.com/SPARK-REX)
- Repository: [Dynamic-island](https://github.com/SPARK-REX/Dynamic-island)

---

## 🤝 Contributing

Contributions, bug reports, and feature requests are welcome! See [`docs/CONTRIBUTING.md`](docs/CONTRIBUTING.md) for details.
1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 License

Distributed under the **MIT License**. See [LICENSE](LICENSE) for more information.
