# Building from source

**Requirements:** Windows + [.NET SDK 9](https://dotnet.microsoft.com/download) (x64).

The app bundles the mpv engine as an embedded resource. Before building, download the Windows x64 **libmpv** library and place it here:

- `src/embed/libmpv-2.dll` — from the mpv dev package, e.g. [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild/releases) or [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake/releases) — grab the `mpv-dev-x86_64-*.7z` archive and copy `libmpv-2.dll` out of it.

> If you plan to redistribute, prefer an **LGPL** build of libmpv so the engine stays dynamically linked under LGPL. HEVC Player only ever calls the public mpv client API (`client.h`) — it never forks or modifies mpv.

Then publish one portable, self-contained binary:

```bat
cd src
dotnet publish HevcPlayer.csproj -c Release -o ..\build
```

Result: a single file in `..\build\` that runs on any Windows 10/11 x64 PC with no .NET installed. The engine is extracted to `%LOCALAPPDATA%\HevcPlayer\bin` on first run.

### Fast UI-only build (no embedded engine)

Handy for working on the interface — skips embedding the ~100 MB engine:

```bat
dotnet build HevcPlayer.csproj -c Release -p:NoEmbed=true
```

### Developer flags

| Flag | What it does |
|------|--------------|
| `--selftest` | offline wiring check (engine extracts, mpv create/init) |
| `--probe <file>` | open a file headless and print its codec, size and duration |
| `--render out.png [demo [frame.png]]` | render the window to a PNG (`demo` = a "now playing" state) |
