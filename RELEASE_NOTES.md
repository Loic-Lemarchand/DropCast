# 🎉 DropCast v1.0.0 — First Release

**DropCast** is a lightweight overlay app that displays memes, images, videos, and text from a Discord channel directly on your screen — perfect for streamers and content creators who want community-driven overlays.

Available for **Windows** and **Android**.

---

## 📦 Downloads

| Platform | File | Description |
|----------|------|-------------|
| 🪟 Windows | `DropCast-Setup-1.0.0.exe` | Installer (requires .NET Framework 4.7.2) |
| 🤖 Android | `DropCast.apk` | APK (Android 8.0+) |

---

## ✨ Features

### 🖥️ Windows
- **Transparent always-on-top overlay** — images, videos, and text appear directly over your screen
- **Video playback** via LibVLC with full audio support
- **Volume control** — adjustable from the floating control panel (0–200%), hotkey `Ctrl+Shift+V`
- **Drag & drop** — drop local files (images, videos) onto the floating drop zone to display them instantly
- **Click-to-dismiss** — optionally click anywhere on the overlay to dismiss the current media
- **Show author info** — display the Discord username and avatar of whoever sent the meme
- **Meme-style text** — bold Impact font captions with a black outline, just like classic memes
- **Server & channel picker** — browse your bot's servers and text channels with a visual picker
- **Channel history** — quickly reconnect to your last 10 used channels
- **Join server by invite** — paste a Discord invite link to add the bot to a new server
- **Video trimming** — use `[start-end]` syntax in messages to play only a portion of a video (e.g. `[30-75]`, `[0:30-1:15]`)
- **YouTube support** — paste YouTube links in Discord and they'll be resolved and played automatically
- **Encrypted bot token** — the Discord bot token is stored encrypted (AES-256) on disk

### 📱 Android
- **System overlay** — displays memes on top of any app (requires overlay permission)
- **Auto-dismiss** — overlays disappear automatically (8s for images/text, on completion for videos)
- **Configurable display zone** — drag and resize the overlay area with an interactive zone editor (rule-of-thirds grid, corner handles)
- **Server & channel picker** — same picker flow as desktop, with recent channel history
- **Join server by invite** — add the bot to new servers directly from the app
- **Video trimming** — same `[start-end]` syntax support as desktop
- **YouTube support** — YouTube links resolved and played inline
- **Bundled bot token** — encrypted token auto-loaded on first launch, no manual setup needed
- **Meme-style text overlay** — Impact font with stroke, rendered natively on Android

### 🔗 Shared
- **Discord bot integration** — connects to Discord via bot token and listens for messages in a selected text channel
- **Multi-media support** — images (JPG, PNG, GIF, WebP), videos (MP4, WebM, MOV), and plain text
- **URL detection** — automatically detects and resolves image/video URLs from message content
- **Encrypted token storage** — identical AES-256-CBC encryption on both platforms, compatible token files

---

## 🎮 Controls

### 🖥️ Windows — Keyboard & Mouse

| Input | Action |
|-------|--------|
| `Ctrl + Shift + V` | Toggle the **control panel** (volume slider, click-to-dismiss, show author) |
| `F10` | Open the **server & channel picker** |
| **Left-click on overlay** | Dismiss the current media *(only when "Clic pour couper" is enabled in the control panel)* |
| **Drag & drop a file** | A drop zone (📂) appears in the bottom-left corner when you drag a file — drop it to display it as an overlay |

### 🖥️ Windows — Control Panel (opened with `Ctrl + Shift + V`)

| Control | Description |
|---------|-------------|
| 🔊 Volume slider | Adjust video/audio volume from 0% to 200% |
| 🖱️ Clic pour couper | When checked, left-clicking the overlay dismisses it instantly |
| 👤 Afficher l'auteur | When checked, shows the Discord username and avatar below the media |

### 🖥️ Windows — Drop Caption Dialog (after drag & drop)

After dropping a local file, a dialog appears with:

| Field | Description |
|-------|-------------|
| Caption | Text displayed below the media (meme-style) |
| Début (s) | Trim start time in seconds *(videos only, optional)* |
| Fin (s) | Trim end time in seconds *(videos only, optional)* |

### 📱 Android

| Action | Description |
|--------|-------------|
| **Démarrer overlay** | Start listening to the selected Discord channel and display overlays |
| **Arrêter** | Stop the overlay service |
| **Server picker** | Select a Discord server from the dropdown |
| **Channel picker** | Select a text channel from the dropdown |
| **Recent channels** | Quickly reconnect to a previously used channel |
| **Invite link** | Paste a `discord.gg/…` link to add the bot to a new server |
| **📐 Configurer la zone** | Opens the zone editor — drag to move, pull corners to resize |
| **Permission overlay** | Opens Android system settings to grant overlay permission |

### 📝 Discord Message Syntax (both platforms)

| Syntax | Example | Effect |
|--------|---------|--------|
| Image/video URL | `https://i.imgur.com/abc.png` | Displays the image/video as an overlay |
| YouTube link | `https://youtube.com/watch?v=...` | Resolves and plays the video |
| Trim range | `[30-75]` | Plays from 30s to 75s |
| Trim (mm:ss) | `[0:30-1:15]` | Plays from 0:30 to 1:15 |
| Trim (open start) | `[-60]` | Plays from start to 60s |
| Trim (open end) | `[30-]` | Plays from 30s to end |
| Plain text | `Hello World` | Displays as a meme-style text overlay |
| Attachment | *(drag file into Discord)* | Displays the attached image/video |

---

## 🛠️ Setup

### Windows
1. Run `DropCast-Setup-1.0.0.exe`
2. Launch DropCast from the Start Menu
3. Select a server and channel from the picker (or paste an invite link)
4. Press **Start** — memes sent in the channel will appear as overlays on your screen

### Android
1. Install `DropCast.apk` (enable "Install from unknown sources" if prompted)
2. Grant overlay permission when asked
3. Select a server and channel
4. Tap **Démarrer overlay** — overlays will appear on top of any app

---

## 📋 Requirements

| Platform | Requirement |
|----------|-------------|
| Windows | .NET Framework 4.7.2 or later (pre-installed on Windows 10+) |
| Android | Android 8.0 (API 26) or later |

---

## ⚠️ Notes
- A **Discord bot token** is required. Create a bot at the [Discord Developer Portal](https://discord.com/developers/applications) and invite it to your server with message-read permissions.
- On Android, the overlay permission must be granted manually in system settings if not prompted automatically.
