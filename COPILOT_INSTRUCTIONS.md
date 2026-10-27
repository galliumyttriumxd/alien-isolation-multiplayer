# Project Context: Alien: Isolation Co-Op Multiplayer Mod

## Overview
We are building a standalone C# multiplayer server and netcode pipeline for Alien: Isolation using a "Hybrid Nitrox" architecture (inspired by Subnautica's Nitrox mod). The mod enables 2-player local/VPN (Hamachi) co-op while running on a single Epic/Steam game instance, bypassing DRM lockouts and synchronizing world states.

## Architecture & Responsibilities
1. **Standalone C# Server (`AlienIsolation.Server`)**:
   - Runs as an asynchronous TCP/UDP socket server on port `7777`.
   - Manages player connections, custom nicknames such as ("Adam", "Oliver"), separate inventory JSON states (`adam_inventory.json`, `oliver_inventory.json`), and global save files.
   - Routes high-frequency transform packets and RPC events between clients.

2. **CathodeLib Integration (`CathodeLib`)**:
   - Used to parse CATHODE engine files (`COMMANDS.PAK`, level layout archives, entity definitions).
   - Maps in-game entities, doors, switches, radios, and emergency save phones to unique `EntityID` bindings.

3. **Delegated AI Authority (Host-Authoritative)**:
   - Host player client calculates native 3D CATHODE navmesh pathfinding for the Xenomorph and Working Joes.
   - Host broadcasts enemy coordinates (20-30 Hz) to the server; Server relays them to Client for linear interpolation (Lerp).

---

## Network Protocol & Packet Specifications
All binary network packets must follow a standardized byte layout using `System.IO.BinaryWriter` and `BinaryReader`:

- **PACKET_CONNECT (0x01)**: `[Byte Opcode] [String Username] [String SelectedMeshID]`
- **PACKET_TRANSFORM (0x02)**: `[Byte Opcode] [Byte PlayerID] [Float PosX, PosY, PosZ] [Float Pitch, Yaw, Roll] [Byte LocomotionState]`
- **PACKET_RPC (0x03)**: `[Byte Opcode] [UInt32 EntityID] [Byte ActionType] [String Payload]`
- **PACKET_SAVE (0x04)**: `[Byte Opcode] [Float HostX, HostY, HostZ] [Float ClientX, ClientY, ClientZ] [String StoryFlags]`

---

## Core Feature Implementations Needed

### 1. Networking Core (`/Networking`)
- `ServerListener.cs`: Asynchronous TCP listener for handshake/login, paired with UDP socket channel for position streaming.
- `PacketHandler.cs`: Opcode reader/writer for encoding and decoding binary payloads.
- `ClientSession.cs`: Tracks active client sockets, player IDs, and assigned character meshes (e.g., Nina Taylor, Axel).

### 2. State & Inventory Management (`/State`)
- `InventoryManager.cs`: Handles discrete player profiles saved to disk (`{username}_inventory.json`).
- Prevents item duplication by treating key story items (Revolver, Flamethrower) as global triggers (`RPC_UNLOCK_WEAPON`), granting items to both radial wheels and destroying the world mesh.

### 3. World Entity & Proximity Logic (`/World`)
- `SaveSystem.cs`: Spatial check requiring both players to be within 5 meters of an Emergency Save Telephone before writing `unified_save.json` and broadcasting a HUD notification toast.
- `ElevatorSync.cs`: Bounding box logic ensuring both players are inside transit cars/elevators before firing synchronized map load triggers.
- `InteractableSync.cs`: Synchronizes state toggles for radios, doors, flares, noisemakers, and flashbangs across `EntityID`s.

### 4. Custom Death & Respawn Loop (`/Player`)
- Intercepts native CATHODE hard-reload functions on player death.
- Triggers local kill animation, applies screen fade, resets health, warps coordinates back to the last active telephone `EntityID`, and resets the Xenomorph's pathfinding node back to its vent state.

---

## Coding Guidelines
- Write clean, modern, asynchronous C# (.NET 8/9 standard).
- Use `System.Numerics.Vector3` and `Quaternion` for coordinate calculations.
- Implement linear interpolation (Lerp) helper functions for smooth client-side entity positioning.
- Ensure thread safety across socket message loops using `ConcurrentDictionary` and lock-free thread-safe collections where applicable.