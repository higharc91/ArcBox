# ADR 0001: SDK and Host Target Frameworks

- **Status:** Accepted
- **Date:** 2026-10-18
- **Context:** Task 2 (Godot ↔ SignalR spike and target-framework decision)

## Context

ArcBox games run in a server-authoritative ASP.NET Core environment (.NET 10), while the TV host runs in Godot 4.x .NET. We needed to prove whether Godot 4.x .NET can connect to an ASP.NET Core SignalR hub using `Microsoft.AspNetCore.SignalR.Client` and reference a .NET 10 class library directly, or whether the SDK and game libraries would need to multitarget `net8.0;net10.0`.

## Investigation & Spike Results

- **Godot Version:** Godot Engine v4.7.2.stable.mono.official.ed1daf0bf
- **Godot .NET SDK:** `Godot.NET.Sdk/4.7.2`
- **Result:** Step 5 passed with .NET 10 (`net10.0`).
  - `spikes/HelloHub` hosted a SignalR `PingHub` over HTTP at `http://localhost:5199/hub`.
  - `spikes/SpikeLib` targeted `net10.0`.
  - `src/ArcBox.Host` targeted `net10.0` using `Godot.NET.Sdk/4.7.2` with `Microsoft.AspNetCore.SignalR.Client` (version 10.0.*) and `CopyLocalLockFileAssemblies` enabled.
  - In headless execution (`godot --headless --path src/ArcBox.Host`), the Godot host connected, invoked `Ping("hello")`, received `pong:hello`, accessed `SpikeInfo.Value` ("spikelib-ok"), and printed:
    ```
    pong:hello spikelib-ok
    ```

## Decision

- Set `ArcSdkTargetFrameworks` to `net10.0` in `Directory.Build.props`.
- `ArcBox.Host` targets `net10.0` directly without requiring multi-targeting to `net8.0`.

## Consequences

- SDK, games, and server all build and target unified `net10.0`.
- No dual-compilation (`net8.0;net10.0`) overhead or `#if` branching required across `ArcBox.Sdk` or game logic libraries.
