# LoupixDeck.Plugin.Argus

Argus Monitor integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

Windows only. Argus Monitor must be running with its shared-memory data API
available.

## Commands

`Argus.Sensor` — a display command that renders a chosen Argus Monitor sensor
reading onto a touch button (updated every 2 s). Sensors are offered as a
live tree in the touch-button command menu.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.Argus.csproj -c Release
```

Copy the build output together with `plugin.json` into
`LoupixDeck/plugins/argus/`.
