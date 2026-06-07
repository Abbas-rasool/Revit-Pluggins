# Revit Structural Detailing Add-ins

Autodesk Revit add-ins written in C# against the Revit API, built to automate
repetitive structural detailing tasks. The solution contains two tools, each
driven by its own WPF interface.

## Tools

### Perimeter Rebars
Automates top-bar reinforcement around a post tension slab perimeter. The user picks a floor
(or an existing filled region), and the tool:

- extracts the outer floor boundary,
- detects edge columns within a configurable distance and builds column grid lines,
- lets the user review and edit the generated grid before committing,
- resolves rebar positions (including inclusion / exclusion zones and MB1 edge
  zones), then places the rebar families and their tags.

### Turn-Down Slab
- Places slab-edge / turn-down profiles along selected floor edges, with support
for multiple profile types (e.g. simple and brick edges).

- User can add the dynanic extension types with a click, avoiding the tedious native revit approach. Useful in timber foundation modeling.

## Technical highlights

- **Single codebase, multiple Revit versions** — one project targets Revit 2024,
  2025, and 2026, switching between .NET Framework 4.8 and .NET 8 per version via
  build configurations.
- **Separation of concerns** — selection, geometry, family loading, and placement
  are split into focused classes (finders, builders, resolvers, placers, providers).
- **Robust Revit API usage** — scoped transactions, silent family-load and failure
  handling to avoid blocking dialogs, and placement validation against view and
  family-placement types.
- **Geometry** — uses NetTopologySuite for polygon / boundary operations.

## Tech stack

C# · Autodesk Revit API · WPF · .NET Framework 4.8 / .NET 8 · NetTopologySuite ·
Nice3point Revit Toolkit

## Building

Open `RevitPlugins.slnx` in Visual Studio 2022+. The project defines one build
configuration per supported Revit version — there is no plain `Debug`, so build
with one of:

- `Debug R24` / `Release R24` → Revit 2024 (.NET Framework 4.8)
- `Debug R25` / `Release R25` → Revit 2025 (.NET 8)
- `Debug R26` / `Release R26` → Revit 2026 (.NET 8)

## Status

Portfolio / example project. The Revit family files (`.rfa`) and project-standard
names referenced at runtime — filled-region types, tag types, and the family
catalog JSON — are specific to the original environment and are **not** included
in this repository, so the tools are intended to demonstrate code and structure
rather than to run as-is against an arbitrary model.
