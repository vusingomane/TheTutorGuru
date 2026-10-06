---
name: Sidecar date serialization
description: Cross-language timestamp serialization pitfall for Replit object-storage signing.
---

Send object-storage signing expiry timestamps as UTC strings ending in `Z`, not escaped `+00:00` strings.

**Why:** The signing sidecar rejected .NET JSON expiry strings containing an escaped plus sign with an empty HTTP 400 response. Identical payloads failed through both .NET and Node; the same expiry serialized with `Z` succeeded.

**How to apply:** Keep signing expiry serialization consistent with JavaScript's `Date.toISOString()`. Do not diagnose this response as a credentials or network issue before checking the serialized timestamp.
