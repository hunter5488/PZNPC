# Shadows of Doubt Co-op (prototype)

A BepInEx mod that lets 2–4 players walk the same Shadows of Doubt city together and see each other.

**Status: v0.1, untested in game.** The networking is covered by automated tests. The parts that hook
into the game were written against the game's known class names and have not run in the real
game yet. Expect to adjust things on the first test (see [First test checklist](#first-test-checklist)).

## What works in v0.1

- One player hosts, up to 3 others join by IP address.
- Every player sees the others as a coloured capsule with a name label, smoothly interpolated.
- The host's in-game clock is pushed to clients, so day/night and opening hours stay in step.
- Warns if players have loaded different cities.
- Status overlay with join/leave messages.

## What is **not** synced yet

Each player still runs their own copy of the simulation, so these differ between players:

- NPCs (citizens walk different routes on each screen; a citizen you kill is alive for others)
- Doors, locks, items, pickups, money, notes, evidence board
- Cases and objectives (each player has their own)
- Combat between players

The roadmap below is the order these are planned in.

## Requirements

- Shadows of Doubt on PC (Steam)
- [BepInEx 6 bleeding-edge build, **Unity IL2CPP x64**](https://builds.bepinex.dev/projects/bepinex_be). Run the game once after installing so BepInEx generates `BepInEx/interop`.
- Every player needs the **same city**. Have the host share their city file and everyone loads it, or everyone generates it with the same name, seed and size.
  - On Windows the city files live in `%USERPROFILE%\AppData\LocalLow\ColePowered Games\Shadows of Doubt\Cities`.
- The host's port (default **27777, TCP**) reachable from the other players: port-forwarding, or a VPN such as Tailscale, ZeroTier or Radmin VPN.

## Building

Needs the .NET 6+ SDK.

```
dotnet build src/SodCoop.Plugin -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Shadows of Doubt"
```

This builds against the DLLs BepInEx generated in your game folder and copies `SodCoop.dll` and
`SodCoop.Net.dll` into `BepInEx/plugins/SodCoop/`. Every player needs both files.

Run the networking tests with `dotnet test tests/SodCoop.Net.Tests`. They don't need the game.

## Playing

1. Launch the game once with the mod so it creates `BepInEx/config/sodcoop.cfg`, then close it.
2. Edit `sodcoop.cfg`:
   - Host: `Mode = Host`
   - Others: `Mode = Join` and `HostAddress = <host's IP>`
   - Everyone: set `PlayerName`
3. Everyone loads the same city. With `AutoStart = true` the session starts once you're in the game. Otherwise press **F8**.
4. **F8** starts or stops the session. **F9** hides the overlay.

Other settings in the file: `SendRate`, `InterpolationDelayMs`, `SyncTime`, `AvatarHeightOffset`,
and `CityIdOverride` (set the same text on every player if city detection fails).

## How it works

```
src/SodCoop.Net      Game-independent networking (netstandard2.1). Covered by the tests.
  Messages.cs        Wire messages: Hello, Welcome, Reject, PlayerJoined/Left, PlayerState, WorldTime, Chat
  Framing.cs         Length-prefixed binary framing over TCP
  Connection.cs      One socket with its own read/write threads; drops stale positions if a peer stalls
  CoopHost.cs        Host: assigns ids, relays client positions to the other clients, owns the clock
  CoopClient.cs      Client: connects, handshakes, receives events
  SnapshotBuffer.cs  Interpolation of remote players between position updates
src/SodCoop.Plugin   The BepInEx plugin
  Plugin.cs          Entry point
  CoopConfig.cs      sodcoop.cfg settings
  CoopBehaviour.cs   Per-frame loop on the Unity main thread: start/stop, send, apply, overlay
  RemoteAvatar.cs    The capsule drawn for each other player
  GameBridge.cs      The only file that touches the game's own classes (Player, CityData, SessionData)
tests/SodCoop.Net.Tests  Real host + clients over loopback, framing, interpolation
```

The host is authoritative. Network threads never touch Unity: they queue events, and the game thread
drains them each frame. `GameBridge` reaches the game's classes by name through reflection, so if a
game update renames something, the mod still loads. The feature that used it switches off and
the BepInEx log says which class or member was missing.

## First test checklist

Test on a LAN first: one PC hosts and the other joins the host's local IP. If your setup lets the
game run twice on one PC, the second copy can join `127.0.0.1`. Then check, in the BepInEx console or `BepInEx/LogOutput.log`:

| Check | If it fails |
|---|---|
| Log shows `Shadows of Doubt Co-op 0.1.0 loaded` | BepInEx isn't the IL2CPP build, or the DLLs aren't both in `plugins/SodCoop` |
| Overlay text at the top left in game | Session can still work, IMGUI may be unavailable; check the log |
| `Hosting on port 27777` / `Connected to host.` | Firewall, wrong IP, or the `Player` class wasn't found (log warns `Could not find game class`) |
| The other player appears where they really are | If floating or sunk, change `AvatarHeightOffset` |
| Capsules invisible but name labels show | `CreatePrimitive` is stripped from the game build; log says so |
| City mismatch warning while in the same city | Set `CityIdOverride` to the same text on both |
| Clock warnings (`SessionData` / `gameTime`) | Set `SyncTime = false` and report the log line |

To find the right names for anything `GameBridge` can't find, install
[UnityExplorer](https://github.com/yukieiji/UnityExplorer) (IL2CPP build) and look up the class in game.

## Roadmap

1. **v0.1 (this)**: see each other, shared clock.
2. **Shared doors and locks**: the host broadcasts door open/close/lock state; clients apply it.
3. **Shared NPCs**: the host runs citizen AI and clients mirror positions (the big one). Clients' own citizen AI has to be suspended, which needs Harmony patches into the game's AI update.
4. **Shared case**: one murder case and evidence board for everyone.
5. **Item and interaction sync**, then player models in place of capsules.
