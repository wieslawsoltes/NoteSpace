# Contributing

Use .NET 10 and the pinned Uno SDK. Keep library boundaries intact and avoid adding a dependency from a reusable library to the application. Use permissively licensed dependencies and include their license information when redistributing assets.

Run the portable tests, build the browser and desktop targets, and run the browser smoke suite before opening a pull request. The main workflow does these checks and publishes QA artifacts. Test with a synthetic notebook; never commit personal notebook contents or credentials.

Changes to the data model need validation and round-trip tests. Editing changes need undo/redo and failed-transaction coverage. Rendering changes should include a native rendering test or a reproducible browser screenshot. UI commands must perform a real action or be omitted; do not imply Microsoft feature parity where none has been demonstrated.

Keep PRs focused, describe any migration or compatibility implications, and document remaining platform limitations. Reusable components should expose a clear API and own/dispose their resources. Performance claims need measured workloads and environment details, not only a successful build.
