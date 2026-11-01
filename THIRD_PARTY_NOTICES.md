# Third-party software

GDA-ZZZ does not include the former GDA application or its business framework.

- [OneDragon](https://github.com/OneDragon-Anything/ZenlessZoneZero-OneDragon), GPL-3.0,
  is downloaded separately from its official v2.6.0 release and runs as a separate process.
  Its source and license remain in the installed OneDragon directory. Official source:
  https://github.com/OneDragon-Anything/ZenlessZoneZero-OneDragon/tree/v2.6.0
- [Iroh](https://github.com/n0-computer/iroh), MIT/Apache-2.0.
- [QRCoder](https://github.com/codebude/QRCoder), MIT.
- [.NET](https://github.com/dotnet/runtime), MIT; WPF and ASP.NET Core runtime components
  are included in the self-contained distribution with their accompanying notices.
- Other Rust dependencies are recorded in `connection/Cargo.lock`.

The OneDragon runner uses the public module/API boundary. Changes to OneDragon itself
are not bundled. OneDragon game actions and its required assets are not reimplemented.
