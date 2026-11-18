# Changelog

All notable changes to the Azure Connectors SDK IntelliSense extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Supports `Azure.Connectors.Sdk` 1.0.0, the generally available package on NuGet.org.
  The extension now prefers the `Azure.Connectors.Sdk` package ID and still recognizes
  `Microsoft.Azure.Connectors.Sdk` preview packages. Development builds are found in
  the SDK repository's `src/Azure.Connectors.Sdk` output.

## [0.1.0] - 2026-04-11

### Added

- LSP server with SDK assembly analysis via reflection and decompilation
- Hover information for connector operations, parameters, and dynamic schemas
- IntelliSense completions for connector names, operation IDs, and parameter values
- CodeLens showing operation metadata inline with code
- Dynamic value resolution against live Azure connector APIs
- Connection management view for Connector Namespace connections
- NuGet package auto-discovery from workspace project references
- GitHub Actions CI workflow for build, test, and lint
- GitHub Actions release workflow for automated VSIX packaging on tag push

[0.1.0]: https://github.com/Azure/Connectors-NET-LSP/releases/tag/v0.1.0
