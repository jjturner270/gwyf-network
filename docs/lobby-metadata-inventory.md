# Lobby Metadata Inventory

What the real GWYF build 256 exposes per public lobby, classified before anything is
transmitted. Sources: `SessionInfo` / `SessionInfoUIController` / `RoomInfo`
(Assembly-CSharp + Assembly-CSharp-firstpass, metadata-verified; value semantics
pending in-game confirmation).

## Fields on `SessionInfo` (per enumrow result)

| Key | Source member | Example (expected) | Meaning | Public? | Protocol field? | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| UniqueRoomId | `SessionInfo.UniqueRoomId` | joinable Photon room id | external lobby identifier for this build | **yes** | yes (`steamLobbyId`, see below) | never float-converted; string at the boundary |
| HostName | `SessionInfo.HostName` | `im so good` | host's chosen display name — **player-chosen, low sensitivity** | yes | yes (`name`) | treat as public lobby name, not personal data |
| PlayerCount | `SessionInfo.PlayerCount` | `7` | current member count | yes | yes (`members`) | |
| MaxPlayerCount | `SessionInfo.MaxPlayerCount` | `12` | capacity | yes | yes (`capacity`) | |
| CourseName | `SessionInfo.CourseName` | `Forest of Lost Souls` or custom course ref | course / map name | yes | yes (`map`) | custom course refs verified in-game before promoting as map names |
| GameMode | `SessionInfo.GameMode` | `Classic` | mode | yes | yes (`mode`) | |
| Password | `SessionInfo.Password` | `""` | password-protection flag material | **no — do not collect value** | no | use `Isvisible`/`LobbyType` for openness; never transmit the password string |
| CustomCourse | `SessionInfo.CustomCourse` | map-ref | custom course identifier | yes (as string) | no (rawMetadata) | may be a workshop/UGC id — hold until classified |
| CustomMode | `SessionInfo.CustomMode` | mode-ref | custom mode identifier | yes (as string) | no (rawMetadata) | same |
| GameCountdown | `SessionInfo.GameCountdown` | `5` | countdown setting | no | no | gameplay setting, low value |
| IsCustomRandom | `SessionInfo.IsCustomRandom` | `false` | randomised course flag | no | no | gameplay flag |
| LobbyType | `SessionInfo.LobbyType` | `Public` | privacy kind (`SessionKind` enum: InviteOnly/FriendsOnly/Public/Password) | **yes** (privacy classification) | no (rawMetadata) | drives whether the lobby was in the public browser at all |
| Isvisible | `SessionInfo.Isvisible` | `true` | visibility flag | yes | no (rawMetadata) | |
| Identifier | `SessionInfo.Identifier` | stable id (numeric when Steam-backed) | candidate cross-platform stable id | yes | **yes — preferred source for `steamLobbyId` when it looks like a platform lobby id** | must confirm semantics in-game |

## `RoomInfo` (Photon wrapper, raw underneath)

| Key | Public? | Notes |
| --- | --- | --- |
| Name | yes | raw Photon room name; equals session join identity |
| PlayerCount / MaxPlayers | yes | duplicates SessionInfo counts — one source is enough |
| CustomProperties | **unknown** | arbitrary key/value dict from the host; **never forwarded raw** — allowlist only, see below |
| IsOpen / IsVisible | yes | |
| MasterClientId / ExpectedUsers / PlayerTtl / EmptyRoomTtl / AutoCleanUp | **no** | internal server bookkeeping |

## Transmission policy

**Allowlist for the first public build**

Allowed into `LobbyInfo` (supported fields): `steamLobbyId`, `name`, `members`,
`capacity`, `map`, `mode`, plus `gameBuild`/`clientVersion` on the observer.

Allowed into `rawMetadata` (never promoted to protocol fields automatically):

- `LobbyType` (privacy classification)
- `Isvisible`
- `Identifier` (raw backup for the lobby id resolution)
- no other `RoomInfo.CustomProperties` keys until they are classified here

Explicitly **never collected**: `Password` value, `MasterClientId`, `ExpectedUsers`,
player ids, IPs, tokens, connection secrets.

Any new key that surfaces in-game must be classified in this table before it may be
sent. `PUBLIC LOBBY DATA` keys are safe candidates; `UNKNOWN` keys are held back;
`PLAYER-SPECIFIC` keys are not collected by default; `SECRET/TOKEN` is never
collected.

## Protocol field mapping (normalization rules)

- `steamLobbyId` — prefer `Identifier` when it validates as an external lobby id
  (e.g. numeric Steam lobby id), else `UniqueRoomId`. Serialized as string only.
- `name` — `HostName`, nullable when absent (protocol now permits `null`).
- `members` / `capacity` — `PlayerCount` / `MaxPlayerCount` (always present in
  Photon rooms; protocol keeps them required).
- `map` — `CourseName`; custom-course refs and empty values map to `null` until
  verified meaningfully public.
- `mode` — `GameMode`; empty maps to `null`.
- Unknown metadata is kept out of the protocol unless this table promotes it.