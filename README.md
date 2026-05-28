# Jellyfin.Plugin.RetroStretch

[![Build](https://img.shields.io/github/actions/workflow/status/oscars-couch/Jellyfin.Plugin.RetroStretch/release.yml?branch=main)](https://github.com/oscars-couch/Jellyfin.Plugin.RetroStretch/actions)
[![License: GPL v2](https://img.shields.io/badge/License-GPL%20v2-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/oscars-couch/Jellyfin.Plugin.RetroStretch)](https://github.com/oscars-couch/Jellyfin.Plugin.RetroStretch/releases)

A Jellyfin plugin that pre-stretches 4:3 sources to 16:9 at transcode time, so
HDMI-to-RCA converter setups feeding a CRT TV render the original 4:3 geometry
correctly instead of a pillared / squished image.

## The problem

The converter device assumes its HDMI input is 16:9 and horizontally squeezes
that 1920×1080 buffer to fit the CRT's 4:3 panel. If you feed it native 4:3
content, the player will pillar-box it inside the 16:9 frame, the converter
then squeezes the whole thing — including the pillars — and you end up with a
narrow strip of squished picture in the middle of the CRT.

This plugin fixes that by stretching the 4:3 pixels to fill the full 16:9
frame *before* it leaves the server. The converter's squeeze then restores
correct geometry on the CRT.

## What it does

For sessions whose `DeviceName` or `Client` substring-matches a configured
list:

1. Optionally overrides Jellyfin's playback negotiation to force a transcode
   (no direct play, no remux). Without this, clients that can direct-play the
   source codec bypass the plugin entirely.
2. Intercepts `ITranscodeManager.StartFfMpeg` and, if the source is 4:3,
   rewrites the scale step in ffmpeg's `-vf` chain to produce the configured
   target dimensions (default 1920×1080).
3. Also sets `-aspect 16:9` on the output so the player honors the new DAR
   instead of the source's inherited SAR.

## Hardware-accelerated transcoding

Filter rewriting is HW-aware. The decorator recognises and edits these scale
steps in place, preserving the rest of the chain so HW transcodes stay on the
GPU:

- `scale_vaapi` (Intel / AMD VA-API)
- `scale_qsv` and `vpp_qsv` (Intel QSV)
- `scale_cuda` (NVIDIA NVENC)
- `scale_opencl`
- `scale_vt` (Apple VideoToolbox)
- `scale_npp` (older NVIDIA)
- `scale_rkrga` (Rockchip)
- plain SW `scale=...`

If the chain uses `-filter_complex` (subtitle burn-in, overlays, etc.) the
plugin bails out and lets the transcode play as-is rather than risk
corrupting the graph.

## Install

### From a release zip

1. Download `Jellyfin.Plugin.RetroStretch-vX.Y.Z.Z.zip` from the
   [releases page](https://github.com/oscars-couch/Jellyfin.Plugin.RetroStretch/releases).
2. Extract into a versioned folder under Jellyfin's plugin directory:

   ```text
   /var/lib/jellyfin/plugins/RetroStretch_1.0.0.0/
       Jellyfin.Plugin.RetroStretch.dll
       meta.json
   ```

3. Restart Jellyfin (`sudo systemctl restart jellyfin` on Debian-based setups).

### Build & install from source

Requires the .NET 9 SDK.

```sh
dotnet publish --configuration Release --output bin
```

Then copy `bin/Jellyfin.Plugin.RetroStretch.dll` and `meta.json` into the
versioned plugin folder above and restart Jellyfin.

## Configure

Dashboard → Plugins → Retro Stretch:

- **Enabled** — master toggle.
- **Force transcode on matched devices** — disable direct play / direct
  stream for matched sessions. Default on; turn off only if you want to
  selectively opt out per playback.
- **Target device identifiers** — comma-separated substrings matched
  case-insensitively against `DeviceName` and `Client`. The "Connected
  sessions" picker below lists every active client; click *Add* on a row to
  drop its device name into the list.
- **Target width / height** — the dimensions Jellyfin's scaler will produce
  for matching 4:3 sources. Leave at 1920×1080 unless your client requests
  otherwise.

## Limitations

The plugin can't help in three cases (all logged at info level when they
happen):

1. **Direct play / video copy** — if the client direct-plays or Jellyfin
   chooses to remux without re-encoding, there is no scale filter to rewrite.
   The "Force transcode" toggle exists specifically to avoid this.
2. **`-filter_complex` chains** — subtitle burn-in and overlay paths use
   `-filter_complex` with labeled graph nodes that are unsafe to rewrite
   blindly. The plugin passes those transcodes through unchanged.
3. **Pillarboxed 4:3 inside a 16:9 container** — only sources with a
   metadata DAR in the 4:3 range (1.30–1.36) are detected. Pillarbox
   auto-detection via cropdetect would require an extra synchronous
   ffmpeg invocation per transcode start and was deliberately skipped.

## Compatibility

Built and tested against Jellyfin **10.11.10**. The plugin depends on
`ITranscodeManager` being a single replaceable singleton (it is, as of
10.11.10) and on Jellyfin's MVC pipeline accepting filter registration via
`Configure<MvcOptions>` (it does).

If a future Jellyfin release reshuffles `TranscodeManager`'s constructor
parameters, the `ActivatorUtilities.CreateInstance` call in
`PluginServiceRegistrator` may fail and the plugin will be marked
*Malfunctioned*. The rest of Jellyfin continues to work — direct-play falls
back to the original behavior.

## License

[GPL-2.0](LICENSE), matching Jellyfin upstream.
