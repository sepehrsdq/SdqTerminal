# Repository layout

Open `SdqTerminal.sln` from the repository root in Visual Studio.

| Directory | Contents |
| --- | --- |
| `src/SdqTerminal/` | WPF project, application startup and assembly metadata |
| `src/SdqTerminal/Views/` | Windows and their matching XAML code-behind files |
| `src/SdqTerminal/Controls/` | Terminal view, scrollbar resources and menu styling |
| `src/SdqTerminal/Terminal/` | ConPTY sessions, buffer, RTL shaping and wrapping |
| `src/SdqTerminal/Themes/` | Theme definitions, palettes and tab colors |
| `src/SdqTerminal/Services/` | Settings, shell paths, WSL, updates and Windows integration |
| `src/SdqTerminal/Assets/` | Application icon |
| `tests/` | Buffer and WPF regression projects |
| `scripts/` | Version reader and local release build |
| `installer/` | Inno Setup installer definition |
| `docs/` | Website and developer documentation |
| `screenshots/` | Images used by the README and website |
| `artifacts/` | Ignored generated output and archived local builds |

From the repository root:

```powershell
dotnet build SdqTerminal.sln -c Release
dotnet run --project src/SdqTerminal/SdqTerminal.csproj
dotnet run --project tests/SdqTerminal.BufferTests -c Release
dotnet run --project tests/SdqTerminal.RenderTests -c Release
.\scripts\build-release.ps1
```

The release script resolves paths from its own location, so it can also be run
from another working directory. It writes the app to `artifacts/publish/win-x64`
and the installer to `artifacts/release`. Render tests write their output to
`artifacts/render-checks`. Normal SDK `bin` and `obj` folders stay inside each
project and are ignored by Git.

Existing root-level build output was moved intact into `artifacts/legacy/`.
The root `SdqTerminal.png` retains its public URL used by the README and website.
Local `.vs` and `.mimocode` folders retain their original locations because they
belong to development tools rather than application source.
