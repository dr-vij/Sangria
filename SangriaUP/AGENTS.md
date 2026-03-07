# Project Agent Instructions

1. Do not run build commands (`dotnet build`, `msbuild`, Unity player builds, or similar) unless the user explicitly requests a build.
2. Keep profiler stress-test loops (for example, generation in `Update`) intact unless the user explicitly asks to change test methodology.
3. Write all project documentation in English.
