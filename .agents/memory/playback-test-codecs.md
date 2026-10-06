---
name: Playback test codecs
description: The browser-testing Chromium lacks H.264 even when protected MP4 delivery is correct.
---

Use a genuine VP9-in-MP4 fixture for positive playback checks in the current testing browser. Do not remove ordinary MP4 support or change the app solely because an H.264 test reports a decoder error.

**Why:** Chromium reported no H.264 support but `probably` for VP9. An HTTP-downloaded one-second H.264 file matched its original SHA-256 and was valid in ffprobe; the VP9 MP4 played through the same protected delivery path without errors.

**How to apply:** Check `canPlayType`, served MIME type, byte integrity and media errors separately. Verify ordinary H.264 on a user browser that supports it before a production launch.
