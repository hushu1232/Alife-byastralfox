# D Drive Storage Policy

This project should keep large project-owned files on `D:` whenever possible.

## Project-Owned Paths

- Source root: `D:\Alife`
- Build outputs: `D:\Alife\Outputs`
- Runtime metadata: `D:\Alife\Runtime`
- Persistent storage: `D:\Alife\Storage`
- Project temp files: `D:\Alife\.tmp\Alife.Client`

`Directory.Build.props` routes MSBuild outputs into `D:\Alife\Outputs\...`.
`AlifePath` routes runtime, storage, and temp directories under the project root by default.

## Startup Script

`tools\start-alife-napcat-live.ps1` no longer hardcodes the user-profile dotnet path.

Resolution order:

1. Explicit `-DotNetPath`
2. `ALIFE_DOTNET_PATH`
3. `D:\dotnet\dotnet.exe`
4. `dotnet` from `PATH`

If a D-drive dotnet runtime is installed later, set:

```powershell
setx ALIFE_DOTNET_PATH D:\dotnet\dotnet.exe
```

## External Tool Caches

Some large caches are controlled by .NET, NuGet, Playwright, browser tooling, or the OS rather than this repository. To move new cache writes to `D:`, configure user environment variables:

```powershell
setx NUGET_PACKAGES D:\.nuget\packages
setx DOTNET_CLI_HOME D:\.dotnet
setx TMP D:\tmp
setx TEMP D:\tmp
setx PLAYWRIGHT_BROWSERS_PATH D:\ms-playwright
```

These settings affect future shells and may affect other projects. Existing cache folders under `C:\Users\<user>` are not moved or deleted automatically.

## Cleanup Boundary

Do not delete or move existing C-drive caches blindly. First confirm what owns the data, then migrate by:

1. Closing running Alife, dotnet, browser, and NapCat processes.
2. Setting the environment variables above.
3. Rebuilding and running tests once.
4. Only then removing old caches that are confirmed unused.
