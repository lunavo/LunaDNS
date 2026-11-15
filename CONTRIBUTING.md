# Contributing to LunaDNS

<sub>**English** · [Tiếng Việt](CONTRIBUTING.vi.md)</sub>

Thanks for helping. Bug reports from users matter as much as code.

## Reporting a problem

1. Check [Troubleshooting](docs/guide/en/08-troubleshooting.md) first.
2. In the app click **Chẩn đoán** (Diagnostics), or run `LunaDNS.exe --report` from an elevated shell. It writes `LunaDNS-report-<time>.txt` to your Desktop with personal info (user name, machine name, private IPs) redacted. Read it before sharing.
3. Open an [issue](https://github.com/lunavo/LunaDNS/issues) with: your OS and version, LunaDNS version, which file you installed (Setup, portable, .dmg), what you did, what you expected, what happened, and the report.

Please report security problems privately to the maintainer instead of opening a public issue.

## Changing the code

1. Read [Architecture](docs/dev/architecture.md) and [Building and testing](docs/dev/building-and-testing.md).
2. Fork, create a branch, make your change with tests.
3. Before opening a pull request:

```powershell
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.App.Tests -f net8.0-windows
dotnet test tests/LunaDns.Platform.Windows.Tests
```

(On macOS use `-f net8.0` and `tests/LunaDns.Platform.MacOS.Tests`.) If you touched NRPT, hosts, proxy or resolver code, also run the admin tests on a machine you can afford to break.

4. Open the PR against `main`. CI must be green on both Windows and macOS.

Rules that keep users' machines safe:

- Every system change goes through `INetworkConfigurator` and the change journal: plan, record, apply. If it cannot be undone from the journal, it does not ship.
- Only touch what LunaDNS created (NRPT Comment `LunaDNS …`, `# LunaDNS` markers). Never modify another program's rules.
- Do not rename journal op or feature names: old entries on users' machines must stay revertible.
- The proxy must keep refusing non-Steam hosts.

## Docs

User guides are in Vietnamese (`docs/guide/`) and English (`docs/guide/en/`). If you change behaviour, update both, or say in the PR which one still needs translating.

## License

By contributing you agree your work is released under the [MIT License](LICENSE).
