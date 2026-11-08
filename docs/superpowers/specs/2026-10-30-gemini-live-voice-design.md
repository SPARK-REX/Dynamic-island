# Gemini Live Voice Design

## Goal

Reduce voice-command delay by streaming microphone audio to Gemini Live while the user speaks. Keep wake-word listening and push-to-talk. Remove offline command recognition because spoken commands will require internet access.

## User experience

- The existing wake-word listener remains active and starts a voice turn.
- Push-to-talk remains available. Ctrl stays the default shortcut.
- Add a **Push-to-talk shortcut** item to the existing tray context menu. It opens a small shortcut-capture dialog and displays the current shortcut in the menu.
- The selected shortcut persists across app restarts. The default applies when no saved value exists or the saved value is invalid.
- After either trigger, DynamicIsland opens a Gemini Live session and streams microphone audio. Wake-word turns end after speech silence; push-to-talk turns end when the shortcut is released.
- Gemini handles commands and conversation. Existing tool handlers continue to execute supported actions. Replies continue through existing notification and speech output.
- Network or Live API failures produce a clear error response and return the assistant to idle.

## Architecture

Replace the post-recording `generateContent` upload with a per-turn Live API WebSocket session. Use .NET `ClientWebSocket` and existing NAudio capture. Convert microphone frames to raw little-endian 16 kHz, mono, 16-bit PCM, then enqueue frames for a background sender so capture callbacks never block on network I/O.

Send Live setup once per turn with system instructions, supported function declarations, input transcription, and text response modality. Parse server messages for transcripts, function calls, text replies, turn completion, and errors. Route function calls through existing command handlers and return tool responses to the session when required. Send audio activity end when local VAD detects the end of a wake-word turn or when push-to-talk is released. Cancel and dispose the session on timeout, app shutdown, or capture failure.

Retain local speech recognition only for wake-word detection. Remove the offline `FastCommand` grammar and its direct command execution path. Keep configurable push-to-talk detection in the global keyboard hook. A shortcut change must not cause ordinary key combinations to start or stop capture unexpectedly.

Persist the push-to-talk shortcut through `SettingsService`. Add the shortcut item to the existing `_tray.ContextMenuStrip`; do not create a new tray icon or settings window.

## Failure handling

- Missing API key: do not open a microphone session; report that online voice commands require a configured Gemini key.
- WebSocket setup, send, or receive failure: close the turn, report the failure, and restore idle state.
- Unknown or unsupported tool call: return a tool error without terminating the application.
- Audio conversion or device failure: stop capture and clean up the session and buffers.
- Shortcut capture rejects modifier-only or unsupported keys and keeps the previous shortcut.

## Acceptance criteria

1. Wake-word activation streams audio before the user finishes speaking; command response no longer waits for a complete WAV upload.
2. Push-to-talk works with the default Ctrl shortcut and with a user-selected shortcut after restart.
3. Tray menu shows the current shortcut and opens shortcut capture.
4. Offline fast-command recognition and local command execution are removed; local wake-word detection remains.
5. Gemini tool calls still reach existing app, media, volume, brightness, search, and screenshot handlers.
6. Ending a turn, network failure, and app shutdown release capture, WebSocket, and queued audio resources.

## Verification

Add focused tests for shortcut persistence and validation, command routing from Live tool-call messages, and PCM frame conversion. Build the WPF app. Manually verify wake-word streaming, default and changed push-to-talk shortcuts, Gemini tool execution, network failure recovery, and clean shutdown.

## Constraints

- Voice command execution requires internet access and a valid Gemini API key.
- Wake-word detection remains local so it can activate online streaming.
- Existing tray menu and notification/TTS presentation remain in use.
