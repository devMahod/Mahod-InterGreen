# PATH_TEST_MATRIX (PathResolverTests, 17 — all PASS)
| Case | Expected | Test |
|---|---|---|
| Lin exact quoted path + 3 variants | same normalized file | Lin_regression (PERMANENT) |
| inner quote in filename | rejected, never sanitized | Quote_in_middle_of_filename |
| spaces/double/hyphen/underscore/()/[]/&/' | Ok + exact path | Valid_special_names_resolve |
| Hebrew folder/file, mixed HE+EN+digits | Ok | Valid_special_names_resolve |
| long nested path (>200 chars) | Ok | Long_nested_path_resolves |
| UNC to nonexistent host | NotFound family + guidance, no crash | Nonexistent_unc_path |
| unmapped drive letter | NotFound family | Nonexistent_mapped_drive |
| missing file / missing folder | NotFound / FolderNotFound | dedicated |
| .xls/.csv/.txt | UnsupportedExtension | Unsupported_extensions_rejected |
| directory as path | PathIsDirectory | Directory_instead_of_file |
| zero-byte .xlsx | EmptyFile | Zero_byte_xlsx |
| txt renamed .xlsx | MalformedWorkbook | Text_file_renamed_to_xlsx |
| empty/null/quoted-empty | Empty | Empty_input_rejected |
| exclusively locked file | Locked + guidance | Locked_file_reports_locked |
| message quality | actionable, no exception text | No_raw_paths_only_messages |
