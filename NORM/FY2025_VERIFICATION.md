# FY2025 independent replay baseline

The proof-engine calculation was independently reproduced from:

- `NORM/SRC_FILES/Balances_2025_period_10plus_ERP.xlsx`
- `NORM/NORM_wiring/NORM_wiring/sql/NORM_FY2025_mapping.sql`
- the audited FY2025 figures seeded by that mapping and completed by
  `src/sql/NORM_02_FY2025_Promote.sql`

Run from the repository root:

```powershell
python src/tools/NORM_FY2025_ReplayCheck.py
```

The verifier uses `openpyxl` and does not form part of the IIS runtime.

## Verified baseline

- 901 FY2025 mapping rows loaded.
- 961 company-code 1000 trial-balance rows accepted.
- Trial balance net: $0.00.
- 99.770775% mapped by absolute value.
- 126 source rows remain without a complete statement-line/account-type mapping;
  they are individually visible in the NORM unmapped pool.
- 39 audited face lines and calculated totals compared.
- 6 tie to the nearest presented $'000.
- 26 are within 1%.
- 7 exceed 1% and require DFG accounting verification.

The seven strict variances are:

1. Expenses in relation to special accounts.
2. Revenue in relation to special accounts.
3. Write-down of non-financial assets.
4. Cash and cash equivalents.
5. Net assets.
6. Total equity (the mapping label is `Statement of Changes in Equity`).
7. Suppliers payables.

The closing-balance SoFP equation differs by $940,176.063 thousand after the
current mappings. NORM records this as a blocking failure rather than obscuring
it. Cash and equity/movement treatment are the main known contributors.

Published values are comparison evidence only. They are never inserted into a
calculated result.
