# Alien Isolation Multiplayer

Experimental multiplayer networking and gameplay integration for Alien: Isolation.

The current implementation contains:

- A .NET 8 TCP/UDP relay server.
- TCP connection handshaking and UDP endpoint association.
- Validated player transform state and two-client transform relay.
- Automated networking tests.
- Read-only CathodeLib/OpenCAGE integration work.

This project is experimental. It does not include Alien: Isolation game files, and
you must own the game and comply with the applicable licenses and terms before
using any game integration code.

## Requirements

- Windows
- .NET 8 SDK
- Alien: Isolation installed locally for future game-side integration
- Git

## Build and test

From the repository root:

```powershell
dotnet build .\AlienIsolation.Server\AlienIsolation.Server.csproj
dotnet test .\AlienIsolation.Server.Tests\AlienIsolation.Server.Tests.csproj
```

More server and relay instructions are in
[AlienIsolation.Server/README.md](./AlienIsolation.Server/README.md).

## Run the server

```powershell
dotnet run --project .\AlienIsolation.Server\AlienIsolation.Server.csproj
```

The server listens on TCP and UDP port `7777` by default.

## Clone this project

For a normal clone:

```powershell
git clone --recurse-submodules https://github.com/YOUR-USERNAME/alien-isolation-multiplayer.git
cd alien-isolation-multiplayer
dotnet test .\AlienIsolation.Server.Tests\AlienIsolation.Server.Tests.csproj
```

Replace `YOUR-USERNAME` with the GitHub account or organization that owns the
repository.

## Publishing the project on GitHub

Do not upload `bin`, `obj`, build output, credentials, or Alien: Isolation game
archives. The root `.gitignore` excludes the generated and local files.

From this project directory, create an empty repository on GitHub first. Do not
add a second README, `.gitignore`, or license in the GitHub form. Then run:

```powershell
git init
git branch -M main
git add .gitignore README.md AlienIsolation.Server AlienIsolation.Server.Tests COPILOT_INSTRUCTIONS.md .vscode
git commit -m "Initial multiplayer networking implementation"
git remote add origin https://github.com/YOUR-USERNAME/alien-isolation-multiplayer.git
git push -u origin main
```

`CathodeLib` is maintained as a separate Git repository and contains its own
submodules. Before publishing, choose one of these approaches:

1. **Recommended:** publish the main project with `CathodeLib` as a Git
   submodule. This keeps the upstream library history separate and lets a clone
   use `--recurse-submodules`.
2. **Alternative:** publish a fork of `CathodeLib` separately and point the
   project to that fork.

Do not commit the nested `CathodeLib/.git` directory as ordinary project files.
If you want the main repository to own a particular CathodeLib revision, add it
as a submodule before the first commit:

```powershell
git submodule add https://github.com/OpenCAGE/CathodeLib.git CathodeLib
git submodule update --init --recursive
git add .gitmodules CathodeLib
git commit -m "Add CathodeLib dependency"
git push
```

If Git reports that `CathodeLib` already exists when adding the submodule,
move that local folder outside the project temporarily, run the command above,
and then copy only uncommitted project-specific changes back after checking
that they are not proprietary or generated files.
