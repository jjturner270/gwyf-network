# GWYF Hook Inventory

Discovered seams for observing the real Golf With Your Friends lobby browser without
touching the server-side model. **Type names here are replaceable implementation
details — they are not architectural concepts.** Re-validate against the installed
build before any production release (see `manual-gwyf-hook-tests.md`).

## Installed build under analysis

| Fact | Value | Source |
| --- | --- | --- |
| Platform | macOS (Steam) | installed game |
| Product bundle | `Golf With Your Friends.app` app id `unity.Blacklight Interactive.Golf With Your Friends` | `Info.plist` |
| Game build | `256 (256.999952)` | `Info.plist` `CFBundleShortVersionString` |
| Unity | `2021.3.28f1 (232e59c3f087)` | `Info.plist` |
| Runtime | **IL2CPP** (`Resources/Data/il2cpp_data`, `Frameworks/GameAssembly.dylib` 57 MB) | game files |
| Metadata | `il2cpp_data/Metadata/global-metadata.dat`, metadata version **29** | parsed header |
| Metadata inventory | 72 images, 12,876 types, 88,627 methods, 57,204 fields | `scripts/tools/il2cpp-metadata-dump.py` |
| Game code image | `Assembly-CSharp.dll` (root namespace) + `Assembly-CSharp-firstpass.dll` | metadata images |

> Older Windows builds observed in the wild are Mono and ship a *different* lobby-browser
> implementation (`PublicLobbyListUI` / `PublicLobbyListEntry`, seen in the third-party
> `confusing-LobbyBrowser` Thunderstore mod). The current Windows Steam build (2026) is
> **IL2CPP** with the same Photon-based browser; the hook compiled clean against its
> BepInEx interop references (`BepInEx/interop/Assembly-CSharp.dll`). This inventory is
> authoritative for IL2CPP builds 256+ (macOS and current Windows). The Windows Mono
> names are listed at the bottom as unverified reference only.

## Lobby discovery flow (build 256, from type graph)

The public lobby browser on this build is **Photon-based**, not Steam-lobby based.
Steam lobbies still exist for friends/invites/social wiring; the *public browser
search* enumerates Photon rooms through a SQL lobby (`OpGetGameList`, "LobbyType must
be SqlLobby") filtered by `SessionMatchmaking.GenerateQuery`.

```
user opens lobby browser (SessionBrowserUIController)
  └─ EnumerateSessionInfosGameState.RequestSessionInfos()
       └─ ISessionEnumeratorCoordinator.Start()
            └─ Team17.RealtimeMultiplayer.Photon.SessionEnumeratorCoordinator
                 └─ Photon room-list response
                      └─ OnPhotonRoomListReceivedHandler()          [raw RoomInfo list]
                           └─ EnumerateSessionInfosGameState.OnSessionInfoEnumerationComplete()
                                └─ .UpdateSessionInfos()          [builds List<SessionInfo>]
                                     └─ SessionBrowserUIController.UpdateSessionInfos()
                                          └─ SessionInfoUIController.Setup(m_sessionInfo) per row
```

The narrowest seam where the game has already assembled **normalized lobby objects**
is the `SessionInfo` result set, not raw Photon `RoomInfo`.

## Candidate seams (metadata-verified names)

### SessionInfo — primary data carrier

```text
TYPE     SessionInfo                       (Assembly-CSharp.dll, root namespace)
MEMBER   RoomInfo (property, RoomInfo)     ← raw Photon room wrapper
MEMBER   UniqueRoomId (property, string)   ← joinable room identifier
MEMBER   PlayerCount / MaxPlayerCount (properties, int)
MEMBER   HostName (property, string)       ← host display name
MEMBER   CourseName (property, string)     ← course name / custom course ref
MEMBER   GameMode (property, string)
MEMBER   Password (property, string)
MEMBER   CustomCourse / CustomMode (property, string)
MEMBER   GameCountdown / IsCustomRandom (property)
MEMBER   LobbyType / Isvisible (property)
MEMBER   Identifier (property, string)     ← candidate stable cross-platform id
OBSERVED N/A (metadata-derived; every accessor exists as get_*/set_*)
USE      The normalized lobby result object. One per lobby per enumeration.
STRATEGY Read-only property access. Never mutate.
CONFIDENCE High (member list verified; value semantics to confirm in-game)
```

### SessionBrowserUIController — primary invocation seam

```text
TYPE     SessionBrowserUIController        (Assembly-CSharp.dll, root namespace)
MEMBER   UpdateSessionInfos (method)       ← called with the fresh result set
OBSERVED Called after each enumeration completes to render lobby rows.
USE      Primary passive observation seam: postfix patch, walk m_controllers
         (List<SessionInfoUIController>), read m_sessionInfo per row.
STRATEGY Harmony postfix; unaffected rows are never touched.
CONFIDENCE High (member names verified; exact param list to confirm at runtime)
```

### EnumerateSessionInfosGameState — lifecycle seam

```text
TYPE     EnumerateSessionInfosGameState    (Assembly-CSharp.dll, root namespace)
MEMBER   RequestSessionInfos (method)      ← fired when the browser starts a search
MEMBER   OnSessionInfoEnumerationComplete (method) ← fired when results arrive
MEMBER   UpdateSessionInfos (method)       ← converts results into UI rows
OBSERVED Drives the whole browser state machine from open → search → results.
USE      Prefix on RequestSessionInfos → LobbySearchStarted.
         Postfix on UpdateSessionInfos → read the applied SessionInfo list.
STRATEGY Harmony; results read from the same list the game just applied.
CONFIDENCE Medium-High (state machine verified; callback order to confirm in-game)
```

### SessionEnumeratorCoordinator (Photon) — low-level fallback seam

```text
TYPE     Team17.RealtimeMultiplayer.Photon.SessionEnumeratorCoordinator
MEMBER   OnPhotonRoomListReceivedHandler (method) ← raw Photon room-list callback
OBSERVED Fired when Photon delivers the room list for the SQL lobby query.
USE      Fallback seam if SessionInfo-based seams change; requires re-deriving
         lobby fields from RoomInfo.CustomProperties with literal property keys.
STRATEGY Harmony postfix, defensive conversion.
CONFIDENCE Medium (type + member verified; property-key constants not extracted)
```

### SessionInfoUIController — row-level seam (diagnostics only)

```text
TYPE     SessionInfoUIController           (Assembly-CSharp.dll, root namespace)
MEMBER   Setup (method, takes SessionInfo) ← one UI row per lobby
MEMBER   m_sessionInfo (field)             ← the row's lobby object
USE      Secondary path for cross-checking the primary seam output.
CONFIDENCE Medium
```

### SessionMatchmaking / SessionMatchmakingProperties — query & property keys

```text
TYPE     SessionMatchmaking                (Assembly-CSharp.dll, root namespace)
MEMBER   GenerateQuery (method, static)    ← builds the SQL lobby query
MEMBER   s_queryBuilder (field, static)

TYPE     SessionMatchmakingProperties
MEMBER   fields: Any, HostName, CourseName, GameMode, Password, CustomCourse,
         CustomMode, Identifier, GameCountdown, LobbyType, IsCustomRandom
         (const string keys, presumed room-property keys)
OBSERVED Metadata-verified only.
CONFIDENCE Low for hooking; informational.
```

## Hook strategy summary

| Layer | Mechanism | Game impact |
| --- | --- | --- |
| Search lifecycle | Harmony **prefix** on `EnumerateSessionInfosGameState.RequestSessionInfos` | none (read-only) |
| Result set | Harmony **postfix** on `SessionBrowserUIController.UpdateSessionInfos` walking `m_controllers[n].m_sessionInfo` | none (read-only) |
| Raw fallback | Harmony postfix on `SessionEnumeratorCoordinator.OnPhotonRoomListReceivedHandler` (future, only if the above breaks) | none |

Every boundary is wrapped in `try/catch` recording into `DiagnosticsPanel`; no
exception ever propagates into vanilla flow. No polling, no UI scraping, no mutation.

## Runtime mod host requirements (macOS build)

- IL2CPP requires **BepInEx 6 (Bleeding Edge)** for macOS —
  https://builds.bepinex.dev/projects/bepinex_be
- Game types are referenced through Il2CppInterop-generated assemblies in
  `BepInEx/interop/` (`Il2CppAssembly-CSharp.dll` etc.). Point `GWYF_REFS` at that
  folder to compile the game-bound patch (`mod/Hooks/Game/`).
- Without game refs the project compiles in "instrument-only" mode: hooks log
  `HOOK UNAVAILABLE` and the game-facing pipeline simply has no source attached.

## Build matrix

| Build | Runtime | Public browser backend | Hook seam | Status |
| --- | --- | --- | --- | --- |
| macOS 256 (installed) | IL2CPP | Photon SQL lobby | `SessionInfo` / `SessionBrowserUIController` | verified in metadata, pending in-game proof |
| Windows current (Steam, 2026) | **IL2CPP** (BepInEx.Unity.IL2CPP) | Photon SQL lobby | `SessionInfo` / `SessionBrowserUIController` | **compiles clean against interop refs** (BepInEx 6 BE + Il2CppInterop); runtime proof pending |
| Windows (older) | Mono | Steam lobbies (`PublicLobbyListUI` / `PublicLobbyListEntry`) | third-party evidence only | **unverified — do not code against without a running build** |

## Repro: regenerate this inventory

```bash
python3 scripts/tools/il2cpp-metadata-dump.py \
  "/path/to/Golf With Your Friends.app/Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat" \
  > /tmp/gwyf-metadata.json
```

Runs against the real installed metadata; no game launch required. Always re-run
after a game update and diff the candidate seams above before shipping a new
`gwyf-lobby-hook` adapter version.