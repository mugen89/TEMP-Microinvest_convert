# CLAUDE.md

Guidance for Claude Code when working in this folder (its own git repository).

## What this is

A separate Windows program (C#, .NET 9, WinForms) that converts AI Accounting
exports („Покупки", „Продажби") into an import workbook for **Microinvest
Делта Pro**. It is polished here, in iterations with an accountant; once the
conversion is approved the engine moves into the owner's web edition. Keep
`MicroinvestConvert.Core` free of UI and Windows code so it can be copied there
unchanged.

## Rules that bite

- **The repository is PUBLIC.** Never commit client workbooks, names, ЕИК, VAT
  or invoice numbers, nor amounts copied from a client file. `.gitignore`
  excludes every workbook (also `Model.xls`, the vendor's sample). Tests build
  their sheets in memory with invented data (`tests/.../TestSheet.cs`).
- **`Deal_Type.md` is the contract with the accountant.** A change of a deal
  type rule changes that file in the same commit (a test compares its table
  with `DealTypes`). It is in Bulgarian; every rule has a number and a source.
- **The output is BIFF8 `.xls`, every cell of „Операции" is text.** Делта Pro
  does not read `.xlsx`. Amounts have a decimal dot; dates are `yyyy-MM-dd`.
- **Columns Q–AA stay empty** until the protocols are done; VIES (O) is empty
  for purchases; P = C. All three are the owner's decisions.
- **Columns of the source are matched by position, never by header text** — the
  headers drift in the right part of the sales export. Two sales layouts exist
  (47 columns; 44 until July 2026); `AiFile` recognises them.
- **Accounts are written as the file has them:** main account, „/", sub-account
  (4111 and 1 → 4111/1; 411/1 and 1 → 411/1/1). Digits are never split — the
  owner's decision. `AccountText` cleans only slashes and spaces that cannot mean
  anything else; whatever could mean two things is an error, not a guess.
- **Purchases „без ДДС дневник" → deal type 1 is unconfirmed** (П4, В5, В20). The
  owner keeps it for now; most of them are purchases from abroad whose VAT comes
  with a protocol, which this version does not write.
- One debit – many credits (sales) or many debits – one credit (purchases);
  never many to many. The `%` shorthand of `Model.xls` is not used.
- **Deal type 4 is legally „частичен данъчен кредит".** The owner's rule also
  uses it for a VAT that fits no rate, so such documents always carry a warning.
- No currency conversion yet: a document in another currency is held back. What
  is known about it, and about protocols, is in `docs/Later.md`.
- UI text is Bulgarian. Message boxes go through `Dialogs` (Bulgarian buttons).
- Windows are laid out in code with table/flow panels at 96 DPI; every
  constructor ends with `EndLayout()` or nothing scales. `DataGridView` does not
  scale itself — use `Grid<T>`.

## Map

| Where | What |
|---|---|
| `Core/Source` | Excel reading (`WorkbookLoader`), recognition (`AiFile`), rows → documents (`AiReader`), column map (`AiColumns`) |
| `Core/Model` | `OperationBatch` → `OperationDocument` → `LedgerLine` (one amount in one `LedgerBucket`); edits go through `TrySet` |
| `Core/Rules` | `DocumentProcessor` (run again after every edit), `PurchaseRules`, `SaleRules`, `VatRates`, `VatCorrections`, accounts, dates, partners |
| `Core/Output` | `OperationRows` (the texts of A..AA), `DeltaWorkbook` (write + read back and compare) |
| `Core/Settings` | general settings, exceptions per client (by ЕИК), JSON store |
| `App/Forms` | `MainForm`, `StartView`, `WorkView` (3 files), `VatCheckForm`, `AccountsForm`, `SettingsForm`, `UpdateForm` |
| `App/Updates` | `UpdateService` (GitHub releases), `UpdateInstaller` (swap of the running .exe, kept versions) |

A new input format only has to produce an `OperationBatch`.

## Build, check, release

```
dotnet test
dotnet run --project src/MicroinvestConvert.App
dotnet run --project tools/MicroinvestConvert.Cli -- <file> --rows --issues [--out x.xls]
dotnet publish src/MicroinvestConvert.App -p:PublishProfile=win-x64
```

Pictures of every window (no client data leaves the machine):
set `MICONVERT_SHOTS=<folder>` and `MICONVERT_FILES=<file>|<file>`, then
`dotnet test --filter VisualTests`. A rehearsal of the update with two real
programs: `docs/Release.md`.

Real samples are outside this repository (the parent folder's CLAUDE.md says
where). Before and after an engine change run the CLI with `--rows` over them
and compare.

Releases: `docs/Release.md` (tag `v<Version>` → GitHub Actions). A release is
public and reaches every installed program, so the owner decides when: prepare
everything, then let them push the tag.
