# Project Agent Instructions

1. Do not run build commands (`dotnet build`, `msbuild`, Unity player builds, or similar) unless the user explicitly requests a build.
2. Keep profiler stress-test loops (for example, generation in `Update`) intact unless the user explicitly asks to change test methodology.
3. Write all project documentation in English.
4. Do not manually modify `*.csproj` files and do not manually create `*.meta` files. Unity must be the source of truth for project file/meta generation and synchronization.
5. For SangriaMesh work, use `Packages/com.propellerheadvij.sangria/SangriaMesh/Documentation/Overview.md` as the documentation entry point and reference related files in `Packages/com.propellerheadvij.sangria/SangriaMesh/Documentation/`.

# Terminal Command Rules

1. **Never** write multiple independent commands on separate lines in a single bash call.
2. Always join commands with `;` on a single line, or use subshells `(...)` / grouping `{ ...; }`.
3. For complex multi-line scripts, create a temporary `.sh` file via the `create` tool and then execute it with `bash /path/to/script.sh`.
