# RELEASE_TRACEABILITY_AUDIT (r6, 2026-08-18)

**Finding (Arthur, independent audit of the r5 candidate):** the shipped EXE reported
`ProductVersion = 0.1.0 installer r2 (...)` plus the previous commit SHA. Cause: the EXE
was published BEFORE the metadata edit landed, and the SDK appended the then-current
SourceRevisionId. Package/file hashes were correct — only the embedded identity lied.

**Root fix — one source of truth:** `build/MahodRelease.props` defines engine version,
distribution revision and the Git SHA (supplied at build time via `-p:MahodGitSha=`).
It is imported ONLY by the installer and the two host projects, so the six validated
engineering DLLs keep their approved bytes. A build without the SHA is stamped
`UNSTAMPED-DEV` and cannot be shipped. SDK auto-append is disabled, so no second or
stale SHA can appear.

**Every shipped surface now reports the same truth (audited):**

| Surface | r6 value |
|---|---|
| EXE FileVersion | 0.1.0.6 |
| EXE ProductVersion | 0.1.0-r6 (engine 0.1.0, git 41b9fd0aedf9289b8f0697f001df266f02e98de4) |
| Host DLL 2026/2027 File/ProductVersion | 0.1.0.6 / same string + `host build 2026|2027` |
| AssemblyInformationalVersion | as above (single source) |
| Installer wizard subtitle + success screen | revision + engine + build short SHA |
| Windows uninstall entry | DisplayVersion `0.1.0-r6`, Comments = full release id |
| PackageContents.xml | AppVersion/ComponentEntry `0.1.0.6`, release stamped in comments |
| Support log `HOST_INFO` / SESSION_START | `release=0.1.0-r6 hostYYYY git <sha>` |
| Run manifests (`producer`) | `Mahod.Intergreen.AutoCAD 0.1.0-r6 hostYYYY git <sha>` |
| In-process smoke JSON | `release_id`, `release_revision`, `git_sha` |
| Distribution manifest | r6 rows + r5 superseded |

**Engineering impact:** none. Six engineering DLLs byte-identical in both year payloads
(gate re-verified after every rebuild); suites 210/210 + 65/65.
