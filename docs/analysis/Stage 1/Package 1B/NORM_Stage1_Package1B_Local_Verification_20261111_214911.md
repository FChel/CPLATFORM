# Package 1B local verification — immutable result record

11 November 2026 · Final run 21:48:35–21:49:11 AEDT

Result: **293 Passed / 0 Failed**. Production import/engine compilation and full ASP.NET compilation Passed. Existing unrelated `eJET/eJET.aspx.cs:125` CS0162 warning only. Results were read from the actual runner/native process outputs; the PowerShell host transcript omits some native stdout.

| Suite | Passed |
|---|---:|
| Package1B | 38 |
| Increment1A | 44 |
| Increment1B (14 service + 1 engine + 6 helper) | 21 |
| Increment1B2 | 63 |
| FR016 (34 service + 9 engine) | 43 |
| Package1AStep3 (62 service/page + 3 engine + 12 JavaScript) | 77 |
| Package1AStep3 ViewState | 7 |

Commands and native build artifact locations are recorded in [the transcript](Local_Verification_20261111_214835.txt). Tests used the unchanged genuine Defence workbook via explicit `-SamplePath`; Package1B reads the six original text fixtures from the repository. Temporary controlled mutations exist only in memory. Fixture SHA-256 and independently verified counts/totals are in [the profile specification](NORM_Stage1_Package1B_Source_Profiles.md). No shared database/SSM/browser/deployment action occurred.

Final isolated artifacts have base directory `C:\Users\Steve\AppData\Local\Packages\openai.codex_2p2nqsd0c76g0\AC\#!sandbox.{56df9aa8-1eec-46e1-8dbf-1352a3fffd8a}\Temp\`:

- `norm-package1b-e6a886029ae44945a167714e960db34f`
- `norm-increment1a-c366b9cf584845b4ae617fa1ecb9a459`
- `norm-increment1b-b364f7b6c644441ca5f262a99021ca40`
- `norm-fr016-d353547e3c5547018b8cd99b3e69f2f3` (full-site compile)
- `norm-step3-970729fb7f524235bce6452fc4d0d1cd`
- `norm-viewstate-1e6b1e6ffc0a4c948b0120ab19bcba09`

Production text-reader SHA-256: `0cea60d53e1e44d844ef6a06c6612445361109433598e542d7b37199ae80b421`.

Production import-service SHA-256: `2c3959a6e1fa5fb59414d82d2907b6de469cbd4689375496e801aed4083a825c`.

These identify the verified working-tree source files; no immutable Git commit was created. Local readiness is established; live SQL locking/readback, Windows-authenticated browser verification and integrated security acceptance are not claimed.
