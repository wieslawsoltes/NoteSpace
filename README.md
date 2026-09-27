# NoteSpace

An independent, modular notebook workspace built with **Uno Platform 6.7.30**, **.NET 10**, and **SkiaSharp**. Development is in progress. See the build workflow for verification results; full Microsoft OneNote parity is not claimed.

## Development

```sh
dotnet workload install wasm-tools
dotnet run --project tests/NoteSpace.Tests -c Release
dotnet run --project src/NoteSpace.App -f net10.0-browserwasm
```

## Architecture

Core → Editor / Storage / Rendering.Skia → Controls → App. Each library is independently packable; no library references the application.

## License

MIT. Uno Platform and SkiaSharp are external, permissively licensed dependencies. OneNote is a Microsoft trademark. NoteSpace is not affiliated with or endorsed by Microsoft.
