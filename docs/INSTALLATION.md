# 🚀 Installation & User Guide

This guide walks you through downloading, installing, configuring, and troubleshooting **Dynamic Island for Windows**.

---

## ⚡ Method 1: Instant Download (GitHub Releases) — Recommended

If you just want to use the application without building from source:

1. Go to the [GitHub Releases](https://github.com/SPARK-REX/Dynamic-island-for-windows/releases) page.
2. Download the latest release `.zip` archive (e.g., `DynamicIsland-v1.0.0-win-x64.zip`).
3. Extract the `.zip` file into any folder (e.g. `C:\Tools\DynamicIsland`).
4. Double-click **`DynamicIsland.exe`** (or `run.bat`).
5. Dynamic Island will immediately appear at the top center of your primary monitor!

> [!IMPORTANT]
> ### 🛡️ Windows SmartScreen Warning
> When running for the first time, Windows Defender SmartScreen may display:
> **"Windows protected your PC"**
> 
> **How to bypass safely:**
> 1. Click **"More info"**
> 2. Click **"Run anyway"**
> 
> **Why does this happen?**  
> Dynamic Island is a free and open-source project. Code signing certificates from commercial certificate authorities cost hundreds of dollars annually. The application is completely open-source, and all source code is publicly inspectable and buildable directly from this repository.

---

## 💻 Runtime Requirements

| Package | Prerequisite | Notes |
|---|---|---|
| **Release Zip** | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) | Standard Microsoft runtime (~30 MB). Download if prompted on first launch. |
| **Self-Contained Build** | None | Standalone build containing all necessary dependencies. |
| **Source Build** | [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Automatically installed by `setup.bat` via `winget`. |

---

## 🛠️ Method 2: Building from Source (For Developers)

### Prerequisites:
- **Operating System:** Windows 10 (Build 19041+) or Windows 11 (64-bit)
- **SDK:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher

### Quick Setup:
1. Clone the repository:
   ```cmd
   git clone https://github.com/SPARK-REX/Dynamic-island-for-windows.git
   cd Dynamic-island-for-windows
   ```

2. Run the automated setup script:
   ```cmd
   setup.bat
   ```
   > [!TIP]
   > If the .NET 10 SDK is missing on your machine, `setup.bat` will automatically detect it and install it via `winget` (Windows Package Manager). Simply click 'Yes' or grant permission when prompted!

3. Run the automated test suite:
   ```cmd
   test.bat
   ```
   *(Executes all 26 unit tests covering volume math, geometry, timer formatting, and notification filtering)*

4. Launch the application:
   ```cmd
   run.bat
   ```

---

## 🔔 Enabling Notification Access (Windows 10 & 11)

For Dynamic Island to capture and display push toast notifications (from Telegram, WhatsApp, Discord, Slack, etc.), Windows requires Notification Access permission:

1. Open **Windows Settings** (`Win + I`).
2. Go to **Privacy & security** > **Notifications** (or **App permissions** > **Notifications**).
3. Ensure **"Let desktop apps access your notifications"** (or **"Notification access"**) is toggled **ON**.

*(If notifications are disabled at the OS level, media controls, volume HUD, and focus timer will continue to function normally without interruption).*

---

## ⚙️ How to Configure & Auto-Start with Windows

1. Look for the **Dynamic Island** icon in your **Windows Taskbar System Tray** (near the clock, bottom right).
2. **Right-click the tray icon** to open the context menu:
   - **Start with Windows:** Check this option to automatically run Dynamic Island whenever you turn on your PC.
   - **12-Hour / 24-Hour Clock:** Switch time format instantly.
   - **Focus Timer:** Start preset focus countdowns (5, 10, 15, 25, 45, 60 min) or custom timer.
   - **Hide / Show Island:** Temporarily hide the overlay (or double-click the tray icon).
   - **Exit:** Completely close Dynamic Island.

---

## ❓ Troubleshooting & FAQs

### 1. Does Dynamic Island take away focus when I click it?
No. The overlay is built with `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW` flags, so it will **never steal keyboard focus or interrupt your typing and gaming**.

### 2. Why isn't album art or song title appearing?
Make sure your media player (Spotify, Apple Music, Chrome, Edge, VLC) has media playback controls enabled. Windows System Media Transport Controls (SMTC) will automatically beam current track info to the island.

### 3. How do I move or reset the island's position?
- **Drag:** Left-click and drag the island anywhere on your screen.
- **Reset:** **Double-click** the island to snap it back to top-center.
- **Dock:** Dragging near the bottom or side screen edges will snap and dock the island.
