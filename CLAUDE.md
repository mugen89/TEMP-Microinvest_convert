# CLAUDE.md

Guidance for Claude Code when working in this folder (its own git repository).

## What this is

A separate Windows program (C#, .NET 9, WinForms) that converts AI Accounting
exports („Покупки", „Продажби", „Чуждестранни") into an import workbook for
**Microinvest Делта Pro**. It is polished here, in iterations with an accountant; once the
conversion is approved the engine moves into the owner's web edition. Keep
`MicroinvestConvert.Core` free of UI and Windows code so it can be copied there
unchanged.

**Read [`docs/PROJECT_LOG.md`](docs/PROJECT_LOG.md) first:** where things stand, how
the program works, every decision and who made it, how it was checked, what is
open. Add an entry to its §5 at the end of every session – and move §1, §6 and §7
on with it. What may not be public (the owner's words, sample files and what was
found in them, the scripts that check them) goes to `private/`, which git ignores.

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
  for purchases; P = C. All three are the owner's decisions. Foreign invoices
  too: the owner chose „invoice only" (2026-11-23).
- **Columns of the source are matched by position, never by header text** — the
  headers drift in the right part of the sales export. Two sales layouts exist
  (47 columns; 44 until July 2026); `AiFile` recognises them. The foreign export
  is the purchases one with K and AI inserted: it is accepted only when both
  headers are there, never read with the ordinary map.
- **Accounts are written as the file has them:** main account, „/", sub-account
  (4111 and 1 → 4111/1; 411/1 and 1 → 411/1/1). Digits are never split — the
  owner's decision. `AccountText` cleans only slashes and spaces that cannot mean
  anything else; whatever could mean two things is an error, not a guess.
- **Purchases „без ДДС дневник" → deal type 3** (the owner's choice since 1.1.0;
  it was 1). For a supplier from abroad that is a warning (`ForeignNoDiary`): with
  a protocol the document would be in the ledger twice.
- **VAT follows every account** (`VatLines.PerAccount`, the default since 1.1.0);
  the VAT amounts of a deal type must add up to what they are as one amount —
  `DocumentProcessor.RoundSegments`.
- **A check = a code + an entry + a place that raises it:** `IssueCode`,
  `CheckCatalog`, the rule. The severity comes from the catalogue. Settings keep
  the switched-off ones by name, so an older version still reads a newer file.
  The window „Проверки" lists a new check by itself; a new page is a class in
  `App/Forms/Checks` and a line in `CheckPages`.
- **One button on the taskbar:** only `MainForm` has `ShowInTaskbar`. Every other
  window has an owner; `AppForm` hides and shows what it owns when the program is
  minimised (Windows does that for one level only).
- **Settings are shared by the open windows:** after changing one, save and call
  `AppState.RaiseSettingsChanged()`.
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
| `Core/Rules` | `DocumentProcessor` (run again after every edit), `PurchaseRules`, `SaleRules`, `ForeignRules`, `VatRates`, `VatCorrections`, accounts, dates, partners |
| `Core/Output` | `OperationRows` (the texts of A..AA), `DeltaWorkbook` (write + read back and compare) |
| `Core/Settings` | general settings, exceptions per client (by ЕИК), JSON store |
| `Core/Checks` | `CheckCatalog`: every check, its meaning, severity and the fields that answer it |
| `App/Forms` | `MainForm` (start window, `IHub`), `ModuleCard`, `ModuleForm` (one window per module), `WorkView` (3 files), `SettingsForm`, `UpdateForm` |
| `App/Forms/Checks` | `ChecksForm` and its pages; they talk to the opened file through `ICheckHost` |
| `App/Ui` | `Grid<T>`, `FileDrop` (dropping files), `Controls`, `Theme` |
| `App/Updates` | `UpdateService` (GitHub releases), `UpdateInstaller` (swap of the running .exe, kept versions), `Changelog` (CHANGELOG.md inside the program, for „Какво е ново" after an update) |

A new input format only has to produce an `OperationBatch`.

## Build, check, release

```
dotnet test
dotnet run --project src/MicroinvestConvert.App
dotnet run --project tools/MicroinvestConvert.Cli -- <file> --rows --issues [--out x.xls] [--no-diary 1|3|skip] [--vat total|account]
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
