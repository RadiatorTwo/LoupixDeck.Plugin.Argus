# LoupixDeck.Plugin.Argus

Argus Monitor integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

Windows only. Argus Monitor must be running with its data API turned on
("Enable Argus Monitor Data API" in Argus's settings). When it is not, the plugin
reports this as an unmet requirement: the LoupixDeck Plugins page marks it
"Needs attention" and "Show Status" in the plugin settings says why.

## Commands

Both commands draw pixel tiles in the 5×7 bitmap-font style of the hardware-display design
(`research/hardware-info-display-design`): no anti-aliasing, a 72×72 grid centred on the key,
amber/red alert states that never rely on colour alone, and a 72-second history chart.
Sensors are sampled once a second; clock and load readings are smoothed.

- `Argus.Sensor` — one sensor per command, offered as a live tree in the touch-button
  command menu. Chain up to four on one button for a multi-row tile.
- `Argus.Pages` — component pages (CPU, GPU, RAM, NET, DISK and a CPU summary). The
  `Pages` parameter lists them separated by `|` (`cpu|gpu|sum`); several `Argus.Pages`
  commands chained on one button form one cycle in order. Pressing the button shows its
  next page; a single page makes a fixed tile without the n/N index. Pages without data
  are skipped. Per-button paging needs a LoupixDeck host with SDK 1.26.0; on older hosts
  buttons with the same page list page together. NET follows the adapter with the most
  traffic over the last few seconds (Argus reports rates only, no totals), DISK sums the
  transfer rates of all drives.

Settings: transparent background, and the CPU's TjMax (CPU temperature turns amber at
TjMax − 15 °C and red at TjMax − 5 °C).

## Credits

The plugin icon is based on the lizard from [Fluent Emoji](https://github.com/microsoft/fluentui-emoji)
by Microsoft (MIT License, Copyright (c) Microsoft Corporation), recoloured and reshaded. The plugin
is not affiliated with or endorsed by Argus Monitor.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.Argus.csproj -c Release
```

Copy the build output together with `plugin.json` into
`LoupixDeck/plugins/argus/`.
