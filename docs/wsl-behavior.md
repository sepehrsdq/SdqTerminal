# WSL profiles and startup

The WSL launch behavior follows Windows Terminal's generated WSL profiles:

- Discover registered distributions under the current user's `Lxss` registry key,
  with a separate menu entry for each distribution. Rebuild the list when opening
  the profile menu, without launching WSL or parsing localized command output.
- Hide `docker-desktop*` and `rancher-desktop*` utility distributions.
- Launch the system-directory `wsl.exe -d DistributionName` directly, allowing WSL
  to select its configured default user and shell. Do not inject an introduction
  script or force `sh`, `bash`, or an extra login shell.
- Start in `~` using `--cd` on Windows build 19041 or later. An explicit folder
  supplied when opening the app overrides home. On older Windows, use the Windows
  user profile directory as the process working directory without `--cd`.
- Splitting a WSL pane keeps that pane's distribution, regardless of the app's
  current default. Drops/pastes continue converting paths within that distribution.
- Use `STARTF_USESTDHANDLES` with null handles when creating the ConPTY client,
  so the client receives its pseudoconsole's I/O instead of a launcher's redirected
  standard handles.

Distribution names must not be quoted in the WSL option string: WSL can interpret
the quotes as part of the name and return `WSL_E_DISTRO_NOT_FOUND`. They also must
not contain whitespace, control characters or quotes. The executable path and
explicit starting directory remain quoted.

The existing app preference of remembering the last selected terminal profile is
retained, including the selected WSL distribution. Older saved `Wsl` preferences
without a distribution migrate to the registered default when available. If no
distribution can be resolved, the legacy generic profile delegates selection to
`wsl.exe`; path conversion requires choosing a named distribution.

Modern WSL distributions provide Windows Terminal JSON fragments and are skipped
by WT's legacy generator. SdqTerminal lists their registered names directly as
well; it does not import Windows Terminal's user settings or fragment appearance
customizations. It never installs/unregisters distributions or changes WSL defaults.

## References

- [Windows Terminal WSL profile generator](https://github.com/microsoft/terminal/blob/20998f762838f70152a7d1b905e6191f45fa7592/src/cascadia/TerminalSettingsModel/WslDistroGenerator.cpp)
- [Windows Terminal ConPTY connection](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalConnection/ConptyConnection.cpp)
- [WSL quoted distribution-name issue](https://github.com/microsoft/WSL/issues/9792)

## Validation

`dotnet run --project tests/SdqTerminal.RenderTests -c Release` covers registry
fixtures, modern and utility distributions, missing/stale default registrations,
launch arguments, profile-menu entries, split retention and direct CMD/PowerShell
ConPTY sessions with redirected parent I/O.

Optional tests with an installed distribution:

```powershell
dotnet run --project tests/SdqTerminal.RenderTests -c Release -- --wsl-launch-probe Ubuntu-24.04
dotnet run --project tests/SdqTerminal.RenderTests -c Release -- --wsl-path-probe Ubuntu-24.04
```

The launch probe opens real WSL panes, checks home and an explicit directory with
spaces/apostrophes, exercises named/default launches and splits, and closes its
own shells. It does not change saved app preferences or WSL configuration.
