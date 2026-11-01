# NORM trial balance import and the FR-016 correction

This walkthrough follows a browser upload through parsing, acceptance or rejection,
and the subsequent trial balance check. It describes the current working-tree code,
including the approved filename simplification. “Original” means the version in Git
at HEAD `8c33b163a4e2e6ff84e6f89d4259dd96086bd53a`, before this uncommitted correction.

**Existing** labels identify behaviour already present. **New/modified** labels identify
the FR-016 correction. Extracts are selected portions of the named methods; they do not
replace the complete source files. Separate extracts may omit intervening code.

## Reading the examples

A **method** is a named piece of work, such as `Import`. A call such as
`NORMImportService.Import(...)` asks that piece of work to run and supplies its inputs.
`if` means “do this only when the condition is true.” `!=` means “does not equal,”
`==` means “equals,” and `||` means “or.” `throw` stops the normal path and sends an
error back to the caller; `catch` handles that error. `return` hands a result back.

`decimal` holds decimal numbers; `0m` is decimal zero. `+=` adds to a running total.
`Rows` is a list of parsed trial balance rows. A database **table** stores records;
`INSERT` adds a record and `UPDATE` changes one. A **transaction** groups database
changes so that they can be saved together (`Commit`) or undone (`Rollback`).

The path to follow is:

```text
Upload button → ImportButton_Click → Import → appropriate parser
    → CommitImport → exact balance and storage checks
        ├─ reject → save audit metadata → show error on upload page
        └─ accept → save source/files/rows/audit together
                    → StatementEngine.Run → Calculate → WriteValidations
                    → return calculation number → show statements page
```

This is the ordinary single-file path. The existing `Import` method rejects FY2025
and directs callers to `ImportFy2025Transition`; the browser handler described here
does not call that two-file method. That unchanged inconsistency is outside this
walkthrough's correction. Use an otherwise supported year, such as FY2026, when
following the ordinary path.

## 1. The browser upload starts the import — existing

**Files/method:** [NORM_Import.aspx](C:/Development/NORM/NORM/NORM_Import.aspx),
upload/button declarations; [NORM_Import.aspx.cs](C:/Development/NORM/NORM/NORM_Import.aspx.cs),
`NORM_Import.ImportButton_Click`.

The page contains a file chooser named `TrialBalanceFile`. Its button declaration
connects a click to the server method:

```aspx
<asp:Button ID="ImportButton" runat="server" Text="Import and calculate" CssClass="norm-button" OnClick="ImportButton_Click" />
```

Here, “server” means the application computer receiving the uploaded file. ASP.NET
WebForms connects the button to `ImportButton_Click`; the browser does not run C# itself.

After checking the selected approved configuration, file presence and extension,
that method makes this call:

```csharp
NORMImportOutcome outcome = NORMImportService.Import(TrialBalanceFile.FileBytes,
    TrialBalanceFile.FileName, sourceType, releaseId, NORMHelper.CurrentUserId());
Response.Redirect("NORM_Statements.aspx?run=" + outcome.CalculationRunId.ToString(), true);
```

`FileBytes` is the uploaded file's contents. The other inputs identify its name,
selected format, approved configuration and user. A configuration release is the
approved setup for the reporting year/entity. It is separate from the uploaded data.

**Next:** `Import` receives these inputs. The redirect happens only after that call
returns successfully with a calculation number. An error instead follows step 8.

## 2. NORM chooses the parser — existing

**File/method:** [NORMImportService.cs](C:/Development/NORM/App_Code/NORMImportService.cs),
`NORMImportService.Import`; supporting methods `LoadApprovedRelease`, `LoadLedgers`
and `ValidateContent`.

```csharp
List<string> ledgers = LoadLedgers(financialYear, entityCode);
string normalisedType = (sourceType ?? "").Trim().ToUpperInvariant();
ValidateContent(content, "trial balance");

NORMParsedImport parsed;
if (normalisedType == "ERP") { parsed = ErpTrialBalanceImporter.Parse(content, ledgers, financialYear); }
else if (normalisedType == "ROMAN") { parsed = RomanTrialBalanceImporter.Parse(content, ledgers, financialYear); }
else { throw new InvalidDataException("Select ERP or ROMAN as the source format."); }
```

The service has already loaded the approved release and its financial year/entity.
It loads the permitted source ledger codes, tidies the selected format name and checks
that the upload is non-empty and within the configured size limit. The choice of parser
comes from the selected format, not an attempt to guess the format from the file.

A **parser** reads the file's layout and turns it into rows that the rest of NORM can
understand. ERP uses the workbook parser; ROMAN uses the historical text parser.

**Next:** the selected `Parse` method returns `NORMParsedImport`, containing its rows
and reporting year/period information. A parsing error stops this route before acceptance.

## 3. The parsers obtain the amounts — existing

### ERP workbook

**File/method:** [ErpTrialBalanceImporter.cs](C:/Development/NORM/App_Code/ErpTrialBalanceImporter.cs),
`ErpTrialBalanceImporter.Parse`, with `TryDecimal` reading amounts.

After locating the expected headings and checking year/period metadata, `Parse`
examines rows from the first worksheet. It skips rows that do not identify a recognised
company code or do not belong to the configured ledgers. For an included row:

```csharp
decimal ending;
if (!TryDecimal(sheet.Cells[rowNo, 8].Value, out ending))
{
    throw new InvalidDataException("Row " + rowNo.ToString() + " has an invalid ending balance.");
}
```

Column 8 is column H, the ending balance. `TryDecimal` reads a decimal value; if it
cannot, the row causes an error. Columns E, F and G supply opening balance, debit
movement and credit movement. The existing row reconciliation uses:

```csharp
decimal movementDifference = opening + debitMovement + creditMovement - ending;
if (Math.Abs(movementDifference) > 0.01m)
```

The condition starts the existing row-error branch. ERP movements are already signed
in this format, so the code adds both movement columns. Its one-cent row-reconciliation
allowance is unchanged. It is a different check from the new exact-zero whole-TB check.

The selected values are put into a common row record:

```csharp
item.OpeningBalance = opening;
item.DebitMovement = debitMovement;
item.CreditMovement = creditMovement;
item.AccumBalance = ending;
```

### ROMAN text

**File/methods:** [RomanTrialBalanceImporter.cs](C:/Development/NORM/App_Code/RomanTrialBalanceImporter.cs),
`RomanTrialBalanceImporter.Parse` and `ParseAccountingDecimal`.

The parser recognises the historical line layout, filters configured ledgers and
checks the year/period header. It reads the last recognised amount on a matched line:

```csharp
decimal accumulated = ParseAccountingDecimal(amounts[amounts.Count - 1].Value);
```

`ParseAccountingDecimal` applies ROMAN's trailing-minus convention:

```csharp
bool credit = token.EndsWith("-", StringComparison.Ordinal);
decimal value = Decimal.Parse(token.Replace("-", "").Replace(",", ""),
    NumberStyles.Number, CultureInfo.InvariantCulture);
return credit ? -value : value;
```

For example, `100.00-` becomes decimal `-100.00`. The result goes into
`item.AccumBalance`, just as ERP's ending balance does. The ROMAN amount pattern
recognises two-decimal amounts; this correction did not change that parser.

**Next for both:** each row is added to `parsed.Rows`. The shared row definition is
`NORMTrialBalanceRow` in [NORMImportModels.cs](C:/Development/NORM/App_Code/NORMImportModels.cs).
`AccumBalance` is the signed amount used for the aggregate check. It is not a total
of all the other fields. The parsers return to `Import`, which keeps the original
file and calculates its SHA-256 fingerprint—a code identifying the file's contents.
It then calls:

```csharp
return CommitImport(release, normalisedType, source.FileName, fileHash, parsed, sources, importedBy,
    "The retained source covers reporting periods " + PeriodLabel(parsed) + ".");
```

## 4. Debit, credit and net totals — existing calculation, added safeguards

**File/method:** [NORMImportService.cs](C:/Development/NORM/App_Code/NORMImportService.cs),
`CommitImport`.

First, an existing database lookup checks whether the same file set was already
imported against the release. A match produces the existing duplicate error. The new
balance checks do not replace or move that check.

The current total calculation starts at zero and visits every selected row:

```csharp
decimal debit = 0m;
decimal credit = 0m;
string storageError = null;
bool totalsAvailable = true;
```

Inside the row loop:

```csharp
NORMTrialBalanceRow row = statementInput.Rows[i];
decimal amount = row.AccumBalance;
if (totalsAvailable)
{
    try
    {
        if (amount >= 0m) { debit += amount; }
        else { credit += -amount; }
    }
    catch (OverflowException) { totalsAvailable = false; }
}
```

The sign-based calculation is **existing**: positive amounts add to debit; negative
amounts contribute their positive size to credit. `-amount` changes, for example,
`-99.99` into `99.99`. For +100.00 and -99.99, debit is 100.00 and credit is 99.99.

The overflow protection is **new**. If amounts exceed what C# decimal arithmetic can
hold, the service stops calculating totals and records that the balance check could
not run. It will reject that attempt rather than use a partial total as evidence.

After visiting the rows, the current code calculates:

```csharp
decimal net = totalsAvailable ? debit - credit : 0m;
```

When totals are available, net is debit minus credit: the example gives +0.01.
The `0m` fallback is only a placeholder when arithmetic failed; storage-error handling
forces rejection, and the audit does not report that placeholder as a passing balance.

**Next:** storage checks and the exact-zero decision determine whether the method
can reach the database acceptance code.

## 5. Why the original code accepted an imbalance

**File/method:** original `NORMImportService.CommitImport` in
[NORMImportService.cs](C:/Development/NORM/App_Code/NORMImportService.cs).

The original code did the arithmetic but did not make zero a condition of acceptance:

```csharp
decimal net = debit - credit;
long totalBytes = 0L;
for (int i = 0; i < sources.Count; i++) { totalBytes += sources[i].Content.LongLength; }
```

It then proceeded to the import transaction. That transaction stored the calculated
`net` value, even if it was non-zero. Later it marked the import `Imported` and saved
it. Calculating a difference and storing it did not, by itself, reject anything.

The subsequent statement check was too late to prevent that accepted source; it also
allowed differences up to and including AUD 1.00. Step 9 shows that second correction.

**Next in the original path:** database acceptance in step 7 was reached even for
an aggregate imbalance. The new decision below interrupts precisely that route.

## 6. The new acceptance checks, before and after

**File/methods:** [NORMImportService.cs](C:/Development/NORM/App_Code/NORMImportService.cs),
`CommitImport` and new private helper `FitsStoredAmount`.

The table shows selected exact statements side by side. The right column includes
statements from separate places in the current method, with intervening code omitted.

| Before FR-016 | Current FR-016 correction |
|---|---|
| <code>decimal net = debit - credit;</code> | <code>decimal net = totalsAvailable ? debit - credit : 0m;</code> |
| No aggregate-zero or lossless-storage rejection branch. | <code>if (net != 0m &#124;&#124; storageError != null)</code> starts the rejection branch. |
| Continued directly toward file sizing and import transaction. | The rejection branch saves audit metadata and then throws an error. Only a passing attempt continues toward the import transaction. |

The actual current decision is:

```csharp
if (net != 0m || storageError != null)
```

This means “reject if the net is not zero **or** there is a storage problem.”
There is no one-dollar, one-cent or presentation-rounding allowance. +0.01, -0.01,
+0.99, +1.00 and +0.001 all enter the rejection branch.

The new helper checks whether a value can be saved exactly in the existing database:

```csharp
private static bool FitsStoredAmount(decimal amount)
{
    return amount >= -99999999999999999.99m && amount <= 99999999999999999.99m &&
        Decimal.Round(amount, 2) == amount;
}
```

The existing database amount fields allow 17 digits before the decimal point and
two after it (`DECIMAL(19,2)`). The helper checks that range and tests whether rounding
to two decimal places would leave the number unchanged. It does **not** replace the
number with a rounded value. Thus 1.2300 fits; 1.001 does not.

`CommitImport` applies it to every selected row's accumulated balance and each supplied
opening/debit/credit amount, then to the stored totals. For example:

```csharp
if (!FitsStoredAmount(row.AccumBalance)) { storageError = StorageError(row.SourceRowNo, "AccumBalance", row.AccumBalance); }
```

`StorageError` identifies the first offending row, field and exact amount for audit.
`ExactAmount` formats numbers for text without hiding sub-cent differences. Balanced
+1.001/-1.001 therefore passes the arithmetic check but fails storage validation.
Accepted reporting amounts are the same decimal values that passed these checks.

**Next:** passing attempts proceed to step 7. Failing attempts take step 8 and cannot
reach the accepted-import writes or the calculation call.

## 7. Accepted data is saved — existing transaction, new check evidence

**File/method:** [NORMImportService.cs](C:/Development/NORM/App_Code/NORMImportService.cs),
`CommitImport`. Database work uses existing methods in
[NORMHelper.cs](C:/Development/NORM/App_Code/NORMHelper.cs): `OpenConnection`,
`InsertId`, `Exec` and `P`.

The acceptance transaction is **existing**:

```csharp
using (OleDbConnection connection = NORMHelper.OpenConnection())
using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
```

These lines open the application's configured database connection and start one
group of writes. The helper calls use that same connection and transaction.
`InsertId` adds a record and returns its database-assigned number; `Exec` executes
a database instruction; `P` supplies a separate named value such as `@balance`.

The method writes these **existing** records, in order:

| Table | What is saved |
|---|---|
| `tblNORM_Import` | Import number, release/year/entity, source identity, row count and totals; initially `Importing`. |
| `tblNORM_ImportFile` | Original file contents, name, fingerprint, periods and whether it supplies statement rows. |
| `tblNORM_TrialBalanceRow` | Each selected reporting row, linked to the import number. |
| `tblNORM_AuditEvent` | Existing `IMPORT_CREATED` event and new successful balance/storage evidence. |

For the reporting amount, the existing insert includes:

```csharp
NORMHelper.P("@balance", row.AccumBalance),
```

That passes the checked amount directly to storage. After writing all rows, the
existing status instruction is:

```csharp
NORMHelper.Exec(connection, transaction,
    "UPDATE dbo.tblNORM_Import SET StatusCode = 'Imported' WHERE ImportId = @import AND StatusCode = 'Importing'",
    NORMHelper.P("@import", importId));
```

The **new** call inside this same transaction is:

```csharp
WriteBalanceAudit(connection, transaction, "Import", importId.ToString(CultureInfo.InvariantCulture),
    debit, credit, net, null, importedBy);
```

It records balance and storage Pass evidence for the accepted import. Its surrounding
error handler supplies a safe message if that new evidence cannot be written.
The existing outer transaction handler saves everything or undoes the group:

```csharp
transaction.Commit();
```

```csharp
catch
{
    transaction.Rollback();
    throw;
}
```

The status update alone does not finish acceptance; the commit saves all these changes
together. An audit-write failure before commit prevents the import from being accepted.

**Next:** after the accepted-source transaction has finished, the existing code calls:

```csharp
int runId = NORMStatementEngine.Run(importId, importedBy);
```

This creates or reuses a calculation and eventually returns its number to the upload
handler. Calculation is a later operation, not part of the source-acceptance transaction.

## 8. A rejected attempt saves audit metadata and returns an error — new branch

**File/methods:** [NORMImportService.cs](C:/Development/NORM/App_Code/NORMImportService.cs),
`CommitImport`, new `WriteBalanceAudit` and existing `WriteAudit`;
[NORM_Import.aspx.cs](C:/Development/NORM/NORM/NORM_Import.aspx.cs),
existing `ImportButton_Click` error handler.

The rejected attempt gets its own reference, rather than an accepted import number:

```csharp
string attemptId = Guid.NewGuid().ToString("N");
```

Inside a separate audit-only transaction, this new call records the check results:

```csharp
WriteBalanceAudit(connection, transaction, "ImportAttempt", attemptId, debit, credit, net, storageError, importedBy, totalsAvailable);
```

The new helper reuses `WriteAudit`. Its storage check evidence includes:

```csharp
WriteAudit(connection, transaction, "IMPORT_STORAGE_VALIDATION", entityType, entityId,
    "Check=LOSSLESS_DECIMAL_19_2;Result=" + (storageError == null ? "Pass" : "Fail") +
    (storageError == null ? "" : ";" + storageError), user);
```

The existing `WriteAudit` method writes to the existing audit table:

```csharp
"INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy) " +
"VALUES (@event,@type,@id,@detail,@user)"
```

The rejection branch saves attempt context, exact balance/storage results and source
metadata together. Each source event includes a bounded filename. The **new, simplified**
representation keeps Unicode and spaces and escapes only field separators, percent
signs and line breaks:

```csharp
string escapedName = fileName.Replace("%", "%25").Replace(";", "%3B").Replace("=", "%3D")
    .Replace("\r", "%0D").Replace("\n", "%0A");
```

For example, `試算 表;a=1%.xlsx` is stored as `試算 表%3Ba%3D1%25.xlsx` in the source
event's filename field. There are no chunks or offsets. Empty or over-260-character
names explicitly fail this audit transaction; values are not silently shortened.

No rejected import header, original-file blob, reporting rows or calculation are
created. Rejection retains metadata, not the rejected file itself. Once the audit
transaction succeeds, this new statement stops the normal path:

```csharp
throw new InvalidDataException(failure + " No reporting source or calculation was created. Rejection evidence saved. Attempt reference: " + attemptId + ".");
```

If the audit cannot be saved, the branch instead throws:

```csharp
throw new InvalidDataException(failure + " No reporting source or calculation was created. Rejection audit evidence could not be saved; contact support.");
```

The invalid data is still rejected. The message does not claim saved evidence when
persistence failed. Arithmetic overflow records balance NotRun and storage Fail.

Control returns through `Import` to the existing page error handler:

```csharp
catch (Exception error)
{
    ErrorPanel.Visible = true;
    ErrorMessage.Text = "<span>" + HttpUtility.HtmlEncode(error.Message) + "</span>";
}
```

The page shows the message as text, using `HtmlEncode` so message content is not
interpreted as page instructions. It does not redirect to statements on this path.
Earlier format errors and accepted-file duplicates retain their existing error paths;
this new audit branch is for the aggregate/storage decision, not every possible error.

**Next:** the user can correct and upload the source again. Rejected attempts have
no accepted import record, so they do not prevent another attempt through duplicate
handling. There is no StatementEngine call from this rejection branch.

## 9. StatementEngine checks the stored amounts — existing calculation, modified TB check

**File/methods:** [StatementEngine.cs](C:/Development/NORM/App_Code/StatementEngine.cs),
`NORMStatementEngine.Run`, `Calculate`, `WriteValidations` and `AddValidation`.

`Run` loads the accepted import's context and identifies the configuration used.
If an active completed calculation already matches the input/configuration fingerprint,
it returns that calculation number. Otherwise it creates a run marked `Running` and
calls the existing calculation method:

```csharp
Calculate(runId, context, startedBy);
return runId;
```

`Calculate` reads active reporting rows from `tblNORM_TrialBalanceRow` for this import.
As it processes them, it independently adds their stored signed amounts:

```csharp
decimal sourceAmount = NORMHelper.Dec(row, "AccumBalance");
decimal sourceMovement = NORMHelper.Dec(row, "DebitMovement") + NORMHelper.Dec(row, "CreditMovement");
totalNet += sourceAmount;
totalAbs += Math.Abs(sourceAmount);
```

The net used for the TB check is the sum of `sourceAmount`, not the movement total.
After producing statement results, `Calculate` passes `totalNet` to `WriteValidations`.
Only the `DEBITS_EQUAL_CREDITS` rule changed:

| Before FR-016 | Current FR-016 correction |
|---|---|
| <code>Math.Abs(tbDifference) &lt;= 0.001m ? "Pass" : "Fail"</code> | <code>totalNet == 0m ? "Pass" : "Fail"</code> |
| Tolerance supplied to `AddValidation`: <code>0.001m</code>. | Tolerance supplied: <code>0m</code>. |
| Detail said a difference above AUD 1 blocks assurance. | Detail includes the exact AUD difference and says any non-zero difference fails. |

The current code is:

```csharp
decimal tbDifference = totalNet / 1000m;
AddValidation(connection, transaction, runId, "DEBITS_EQUAL_CREDITS", "Debits equal credits", "Blocking",
    totalNet == 0m ? "Pass" : "Fail", tbDifference, 0m, 0m,
    "Net of all imported trial-balance rows in $'000. Exact difference in AUD: " +
    totalNet.ToString("0.00##########################", CultureInfo.InvariantCulture) +
    ". Any non-zero difference fails this check.");
```

`totalNet` is in dollars. Dividing by 1,000 preserves the existing validation display
units. Originally 0.001 in those units meant AUD 1.00; the old comparison passed a
difference of exactly that amount. The new decision uses unscaled `totalNet`, so even
AUD 0.01 fails. The amount columns for validation results retain their existing limited
precision: a sub-dollar difference can appear numerically as zero there, but the result
is Fail and the detail text retains the exact dollar difference.

The existing `AddValidation` method saves the result to `tblNORM_ValidationResult`,
linked to the calculation number. It is separate from the import audit evidence.
`Calculate` then saves its results and marks the run `Complete`. **Complete means the
calculation finished; it does not mean every validation passed.** An execution error
instead causes `Run` to mark the calculation `Failed`.

**Next:** `CommitImport` returns its outcome, including `CalculationRunId`, to the
upload handler, which redirects to the statements page as shown in step 1. A later
calculation error does not undo the already saved valid source; that existing behaviour
is distinct from a rejected upload, which creates neither source nor calculation.
Historical saved validations are not rewritten, and reuse of an existing completed
calculation does not rerun its checks.

## Following one small example

Assume valid file metadata, supported precision and no accepted duplicate:

| Parsed amounts | Import decision | What is saved | Subsequent TB check |
|---|---|---|---|
| +100.00, -100.00 | Net 0.00; storage fits; accept. | Original source, rows and audit evidence together; then calculation. | Pass when a new calculation checks those unchanged rows. |
| +100.00, -99.99 | Net +0.01; reject. | Attempt and failure metadata only, if audit persistence succeeds. | No calculation is started for this attempt. |
| +1.001, -1.001 in ERP | Net zero but storage would lose precision; reject. | Balance Pass plus storage Fail audit evidence, if persistence succeeds. | No calculation is started for this attempt. |

The [implementation and verification report](C:/Development/NORM/docs/FR016_Implementation_and_Verification_20261019.md)
contains the separate test evidence and manual UAT procedure. This walkthrough explains
the code only; no application execution, database access or additional tests were needed
to prepare it.
