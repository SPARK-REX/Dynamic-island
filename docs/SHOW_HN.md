# Show HN: Dynamic Island for Windows 10/11 – Written in C# and .NET 10 (Open Source)

**Link:** https://github.com/SPARK-REX/Dynamic-island

**Post text:**

Hey Hacker News,

I built an open-source Dynamic Island overlay for Windows 10 and Windows 11 written in C# and .NET 10:
https://github.com/SPARK-REX/Dynamic-island

Most desktop dynamic island ports for Windows are Electron apps that idle around 200–300MB RAM and struggle with smooth 120Hz display scaling. I wanted a native, lightweight utility that feels like it belongs in the OS:

- **WPF Vector Geometry:** Animates shapes via direct vector geometry transforms rather than layout reflows, delivering fluid 120+ FPS animations.
- **Hardware & Media Integrations:** Windows SMTC media controllers (Spotify, Apple Music, YouTube Music, VLC), real-time WASAPI audio volume HUD, battery charging glow, and camera/mic privacy indicators.
- **Edge Snapping & Freeform Dragging:** Drag anywhere across multi-monitor setups; drag near screen borders to auto-dock, or double-click to center.
- **Built-in Voice Assistant (Nixi):** Supports dual backends — cloud via Google Gemini 2.5 Flash for multimodal voice tool-calling, or fully offline via local Ollama models.
- **Low Footprint:** Idles at ~30MB working set with zero background CPU load when idle.

The project is MIT licensed. I would love your thoughts on the animation physics, features, and codebase design!
