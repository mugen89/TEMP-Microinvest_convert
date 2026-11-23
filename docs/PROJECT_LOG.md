# Project log — Microinvest Конвертор

What is going on in this project: what the program does, what was decided and by whom, what was built, how it was checked and what is still open. **Read it before changing anything, and add an entry to §5 at the end of every session** (newest first).

**The repository is public.** There are no client names, ЕИК, VAT or invoice numbers, no amounts from client files and no quotes of the owner here. What cannot be here – the owner's requests word for word, the real sample files, what was found in them, and the scripts that check them – is in the folder `private/` on the owner's computer, which git ignores.

| Document | What is in it |
|---|---|
| [`../Deal_Type.md`](../Deal_Type.md) | every rule for column N and VIES, for the accountant to confirm (Bulgarian) |
| [`Output_Format.md`](Output_Format.md) | the file for Делта Pro, column by column (Bulgarian) |
| [`Later.md`](Later.md) | what is put off, with what is known about it: currency, protocols, quantities (Bulgarian) |
| [`Release.md`](Release.md) | how a version is published, and the rehearsal of an update (Bulgarian) |
| [`../CLAUDE.md`](../CLAUDE.md) | the rules for whoever changes the code |
| [`../CHANGELOG.md`](../CHANGELOG.md) | what the user is told about every version (Bulgarian) |

---

## 1. Where things stand (2026-11-23)

| | |
|---|---|
| **Published** | **1.1.0** (tag `v1.1.0`, commit `3799606`) and **1.1.1** (tag `v1.1.1`, commit `b41f29c`). 1.0.0 was only ever a program on the owner's computer |
| **Modules** | Покупки, Продажби, Чуждестранни – exports of AI Accounting → `.xls` for Делта Pro |
| **Tests** | 428, green on the computer and on GitHub |
| **With the accountant** | `Deal_Type.md`: 61 rows with a box to tick, 23 questions. Nothing has come back yet; В5, В17, В20 and В23 carry the owner's decision |
| **Never done yet** | **importing a written file into Делта Pro** |
| **Next** | the owner's test of the in-app update (1.1.0 → 1.1.1), the import test, then §7 |

---

## 2. What the program is

A Windows program – one `.exe`, nothing to install – that turns the Excel exports of the AI Accounting platform into the `.xls` that Microinvest Делта Pro reads with **Операции → Импорт на операции → Импорт от Excel**.

It is a spin-off. The mapping to Делта Pro comes from the owner, who is not an accountant, so it is polished here in rounds with one. When the conversion is approved, the engine (`MicroinvestConvert.Core`: no windows, no Windows) is copied unchanged into the owner's web edition. It is on GitHub from the start because the program updates itself from the releases there; that is also why the repository has to stay public.

| Project | What it is | Size |
|---|---|---|
| `src/MicroinvestConvert.Core` | reading, rules, checks, writing | 26 files, 4 200 lines |
| `src/MicroinvestConvert.App` | the windows and the update | 28 files, 5 100 lines |
| `tools/MicroinvestConvert.Cli` | `miconvert <file> --rows --issues` for comparing two versions | 1 file |
| `tests/MicroinvestConvert.Tests` | 428 tests, all with invented data | 13 files, 4 100 lines |

.NET 9, WinForms, xUnit; ExcelDataReader reads, NPOI writes the old `.xls`.

---

## 3. How it works

### 3.1 The path of a file

1. **In.** Dropped on the start window, on the card of a module or on its window, chosen with the button, or given to the `.exe` (`App/Ui/FileDrop.cs`). Several at once go to their modules; the second file for the same module is reported and not opened. A program started as administrator takes drops the older way (`WM_DROPFILES`), because Windows does not let Explorer use the usual one.
2. **Opened.** `WorkbookLoader` copies the file into memory – it may be open in Excel – and reads the visible sheets.
3. **Recognised.** `AiFile` looks for the row where the platform numbers its columns (`1 | 2 | 2.1 | 3`) and picks the layout. Columns are then read **by position**, because the header texts drift.

   | Export | Columns | Told by |
   |---|---|---|
   | Покупки | 43 | „купувач“ / „доставчик“ in the header |
   | Продажби | 47; 44 until July 2026 | „дневник“ / „клиент“; which layout – by the column of „освободени“ |
   | Чуждестранни | 45 | **both** K „VAT“ and AI „евро“. Without them it is refused: read with the map of Покупки every account would come from a neighbouring column without any error |
   | Протоколи | – | refused, for a later version |

4. **Read.** `AiReader` joins the rows of a document (№, type, number, partner). Every amount becomes one `LedgerLine` in one `LedgerBucket` – the ledger column it was found in; a row with amounts in several columns is split. Nothing is decided here.
5. **Decided.** `DocumentProcessor` goes over the document: who and what → accounting date → currency → deal types (`PurchaseRules`, `SaleRules`, `ForeignRules`) → accounts → the amounts of columns A and B → totals → duplicates and exchange rates → the checks that are off or ticked. It keeps no state and runs again after every correction.
6. **Looked at and corrected.** In the windows; every correction goes through `TrySet` and can be taken back to what the file says.
7. **Written.** `OperationRows` makes the texts of columns A–AA, `DeltaWorkbook` writes the `.xls` aside and swaps it in, then reads it back and compares every cell with what had to be written; a difference is reported.

### 3.2 The three modules

| | Покупки | Продажби | Чуждестранни |
|---|---|---|---|
| Entry (A, B) | many debits – one credit | one debit – many credits | many debits – one credit |
| Amounts from | AB, AC/AD, AE/AF, AG | AB–AK | AI, in euro |
| Deal type (N) | AB → 3 · AC/AD: VAT is 20% or 9% → 2, no VAT → 3, fits no rate → 4 with a warning · AE/AF → 4 · AG = ДА → 3 · чл. 163а → held back | by the ledger column: 7, 11, 12, 13, 14, 15, 16, 17; nothing there → 1 with a warning | always 1 |
| VAT | 453/1, after every account | 453/2, after every account | none: the protocol is entered in Делта Pro |
| VIES (O) | empty | 1; with 13 → 2; with 15 → 4 | empty |
| Paid in cash | second row РКО, N = 1 | second row ПКО, N = 1 | second row РКО, N = 1 |
| Protocol (Q–AA) | empty | empty | empty |

Every rule has a number in `Deal_Type.md` (П1–П17, Ч1–Ч8, ПР1–ПР13), with where it comes from.

### 3.3 Accounts, dates, amounts

- **Accounts** are written as the file has them: main, „/“, sub. 4111 stays 4111; 4111 and 1 is 4111/1; 411/1 and 1 is 411/1/1. Only slashes and spaces that cannot mean anything else are cleaned; `411 1` or `411.1` is an error, not a guess. Where Делта Pro has another number, it is set once in „Проверки → Сметки“, for the client or for everybody.
- **Accounting date (C)** is the first day of the tax period, but never before the document; a document from a month after its period is an error. With „Сч. дата = дата на документа“ it is the document date. **P = C.**
- **Cents.** Every amount is rounded on its own. First the VAT amounts of a deal type add up to what they are as one amount, then the bases take the rest, so the document always adds up to its total (`RoundSegments`).
- **Currency of the books:** BGN until 31.12.2025, EUR from 01.01.2026. A document in another currency is held back.

### 3.4 When a document is not written

A missing number, date, type or account; an account that cannot be written; cash without a cash account; a period before the document; another currency (a foreign invoice without its total in euro too); чл. 163а; or the tick taken off. Everything else is written, if need be with a warning.

### 3.5 The windows

| Window | What it is for |
|---|---|
| Start (`MainForm`) | a card per module with a place to drop the file, the last files, Видове сделки, Настройки, Обновяване. The only window on the taskbar |
| Module (`ModuleForm` + `WorkView`) | one per module, all may be open at once. Documents, the rows of the chosen one, how it will be posted, the exact rows of the file, what was read. „← Начало“ hides it with its file and corrections |
| Проверки (`ChecksForm`) | beside the module; what is changed in one shows in the other. Pages: Всички проверки · ДДС · Вид сделка · Сметки · Контрагенти (without ДДС for Чуждестранни) |
| Настройки, Видове сделки, Обновяване, Какво е ново | small windows over the one they were opened from |

A setting changed in one window is in force in all open ones. Corrections live only in memory until the file is written; leaving them behind is asked about.

### 3.6 The checks (46 in 1.1.1)

An **error** holds the document back and cannot be switched off. A **warning** can be ticked off as seen – until what it says changes – or switched off for good. An **info** only tells. П = Покупки, Пр = Продажби, Ч = Чуждестранни. A new check is a code in `IssueCode`, an entry in `CheckCatalog` and the place in the rules that raises it; the window lists it by itself. A new page is a class in `App/Forms/Checks` and a line in `CheckPages`.

| About | Check | Kind | Modules |
|---|---|---|---|
| Документ | Липсва вид на документа | ✖ error | П, Пр, Ч |
|  | Видът и кодът не съвпадат | ⚠ warning | П, Пр, Ч |
|  | Липсва номер на документ | ✖ error | П, Пр, Ч |
|  | Номер, допълнен до 10 знака | info | П, Пр, Ч |
|  | Дублиран документ | ⚠ warning | П, Пр, Ч |
|  | Липсва основание | ⚠ warning | П, Пр, Ч |
|  | Основание от името на сметката | info | П, Пр, Ч |
| Дати | Липсва дата на документа | ✖ error | П, Пр, Ч |
|  | Липсва данъчен период | ⚠ warning | П, Пр, Ч |
|  | Периодът е преди датата на документа | ✖ error | П, Пр, Ч |
| Контрагент | Липсва име на контрагент | ⚠ warning | П, Пр, Ч |
|  | ЕИК с грешна контролна цифра | ⚠ warning | П, Пр |
|  | Номерът не е български ЕИК | ⚠ warning | П, Пр |
|  | Чужд доставчик в дневника | ⚠ warning | П |
|  | Български доставчик | ⚠ warning | Ч |
|  | VIES без ДДС номер от ЕС | ⚠ warning | Пр |
| Сметки | Липсва счетоводна сметка | ✖ error | П, Пр, Ч |
|  | Липсва сметка на контрагента | ✖ error | П, Пр, Ч |
|  | Плащане в брой без каса | ✖ error | П, Пр, Ч |
|  | Неправилна сметка | ✖ error | П, Пр, Ч |
| Суми | Сумите не са във валутата на книгите | ✖ error | П, Пр, Ч |
|  | Сборът се различава от общата сума | ⚠ warning | П, Пр |
|  | Нулев документ | ⚠ warning | П, Пр, Ч |
|  | Отрицателна сума без кредитно известие | ⚠ warning | П, Пр, Ч |
|  | Кредитно известие с положителни суми | info | П, Пр, Ч |
|  | Сумата в евро се различава от фактурата | ⚠ warning | Ч |
|  | Необичаен курс към еврото | ⚠ warning | Ч |
|  | Преизчислено в евро от AI Accounting | info | Ч |
|  | ДДС на чуждия доставчик е в стойността | info | Ч |
| ДДС | ДДС не отговаря на ставка | ⚠ warning | П |
|  | Обявена ставка, но без ДДС | ⚠ warning | П, Пр |
|  | Сумите отговарят на друга ставка | ⚠ warning | П |
|  | ДДС не отговаря на ставката | ⚠ warning | Пр |
|  | ДДС при вид сделка без ДДС | ⚠ warning | Пр |
|  | ДДС с разлика от центове | info | П, Пр |
| Вид сделка | „Без ДДС дневник“ | info | П |
|  | „Без дневник“, а има суми за дневника | ⚠ warning | П |
|  | Чужд доставчик „без дневник“ с вид 3 | ⚠ warning | П |
|  | Не е отбелязан „без ДДС дневник“ | ⚠ warning | Ч |
|  | Продажба извън дневника | ⚠ warning | Пр |
|  | Няма избрано третиране | ⚠ warning | П |
|  | Обратно начисляване по чл. 163а | ✖ error | П |
|  | Ред със суми в няколко колони | info | П, Пр |
|  | Друга клетка в дневника | ⚠ warning | Пр |
|  | Различен VIES в един документ | ⚠ warning | Пр |
|  | Вид сделка, зададен ръчно | info | П, Пр, Ч |

### 3.7 Settings and their defaults

| Setting | Default | Since |
|---|---|---|
| ДДС сметки | 453/1 purchases, 453/2 sales; an exception per client | 1.0.0 |
| Вид сделка (N) for several amounts | one value for each amount (`2;2`) | 1.0.0 |
| ДДС в сумите (A) | **after every account** (`601;453/1;602;453/1`) | **1.1.0** – before: one amount for the deal type |
| Покупки „без ДДС дневник“ | **3** – in the ledger, к. 9 | **1.1.0** – before: 1 |
| VAT rounding and allowed difference | half up; 0.05 a document, 0.01 a row | 1.0.0 |
| Сч. дата = дата на документа | off | 1.0.0 |
| Second row for cash; cash account when the file has none | on; none | 1.0.0 |
| Checks that are off; accounts under another number | none | 1.1.0; 1.0.0 |
| Look for a new version at the start; open the file after writing | on | 1.0.0 |

Settings written by 1.0.0 move to the two new defaults when they still hold the old ones (`SettingsFile.Version` 1 → 2).

### 3.8 Updates

At the start the program asks GitHub for the latest release – anonymously, nothing about the user is sent. „Обнови“ downloads the zip, refuses it without the SHA-256 that GitHub publishes, renames the running `.exe` aside, puts the new one in its place and restarts. The last three versions are kept and can be gone back to. From 1.1.1 the first start of another version says which one it replaced and shows the notes of everything since. `MicroinvestConvert.exe --update` does the same without a window.

A pushed tag `vX.Y.Z` makes the release (`.github/workflows/release.yml`): it has to match `<Version>`, the tests run, one `.exe` is built and zipped, and the notes come from `CHANGELOG.md`. Every installed program sees the latest release, so **the owner allows each one**.

### 3.9 What is kept on a computer

| Where | What |
|---|---|
| `%AppData%\MicroinvestConvert\settings.json` | the settings and the paths of the last files |
| `%LocalAppData%\MicroinvestConvert` | `Updates` – a download in progress, `Versions` – the previous versions, `Logs` – unexpected errors |
| – | no client data: files are written only where the user says |

---

## 4. What was decided

**By the owner** (2026-11-23):

| Decision | Where it shows |
|---|---|
| A separate program, on GitHub from the first day, with an update; later back into the web edition | the whole repository |
| First the two richest files: AI Accounting purchases and sales | 1.0.0 |
| Account = main „/“ sub, exactly as in the file. The first version split 4111 into 411/1; that was rejected the same day | `AccountText`, В17 |
| The `%` shorthand of the sample is not used; VAT goes to 453/1 and 453/2, and these change only in a separate window | Настройки → ДДС сметки |
| Purchases by the calculated VAT: 20% or 9% → 2, none → 3, anything else → 4. 5 and 6 are not made | П2–П11 |
| Sales by the ledger column; VIES only for sales | ПР1–ПР9, §5 of `Deal_Type.md` |
| P = C; R, T, U and everything from V on stay empty (Q and S are part of the same protocol block, so they do too) | `Output_Format.md` |
| Currency conversion only noted for now | `Later.md` |
| VAT after every account | В23, since 1.1.0 |
| Purchases outside the VAT ledger are 3 (1 in 1.0.0, kept then because nobody could confirm it) | П4, since 1.1.0 |
| Foreign invoices: **only the invoice**, N = 1; the protocol is entered in Делта Pro | Ч1, Ч2, В20, since 1.1.0 |
| Every module in its own window, one button on the taskbar, dropping files, a window with the checks | since 1.1.0 |
| 1.1.0 and 1.1.1 may be published | the releases |

**Assumed here, waiting for an accountant** (the number is the question or the rule in `Deal_Type.md`):

| Assumption | Number |
|---|---|
| 7 is к. 11 and 12 of the sales ledger; к. 10 is the total VAT | В1 |
| A VAT that fits no rate is written as 4, with a warning – by law 4 is partial tax credit | В2, В3 |
| 10 = к. 16, 14 = к. 21, 17 = к. 24, 18 = к. 25 with VIES 3, 16 = к. 23 | В7–В9 |
| Several values in N and O are separated with „;“ | В10, В11 |
| T and U are the date and VAT of the protocol; the month in P is what postpones a tax credit; R is к. 8а | В12–В14 |
| РКО and ПКО are numbered `РКО-` + the number of the document | В15 |
| A supplier without a number has 999999999999999 in E and nothing in F, like two different partners in the vendor's sample | В21 |
| VAT of a foreign supplier stays in the expense | В22 |
| Differences up to 0.05 a document and 0.01 a row are cents, not another rate | П8 |

---

## 5. Log

### 2026-11-23 (5) — This log

- **Asked:** a file in the spin-off that says explicitly what is going on in it.
- **Done:** this document; links to it from `README.md` and `CLAUDE.md`; the folder `private/` (ignored by git) for what cannot be public.
- **Checked:** the 134 names and 712 numbers (ЕИК, VAT and document numbers of seven or more characters) of all real samples, against every version of every file in the history of the repository (166). No name is there. Of the numbers, three only coincide with test values and one is the number of an invoice, without its zeros, used as an example. The amounts of the samples against the files as they are now: no two of one row anywhere, two single ones in tests.
- **Replaced** with invented values: that number and the two amounts. They stay in the history – bare numbers, with nothing to tell whose.

### 2026-11-23 (4) — 1.1.1: after an update the program says what is new

- **Asked:** a second, small version, so that the update can be tested.
- **Built:** `CHANGELOG.md` travels inside the program (`Updates/Changelog.cs`); `FinishPreviousInstall` returns the version it replaced; a green bar on the start window and „Какво е ново“ (`WhatsNewForm`) with the notes of every version since – also when some were skipped. Going back to a version is said too.
- **Verified:** 428 tests; the published 1.1.0 replaced by 1.1.1 and back with two real programs; from GitHub: a copy of the published 1.1.0 starts, offers 1.1.1, becomes byte for byte the published 1.1.1, keeps 1.1.0 and shows the notice.
- **Published:** tag `v1.1.1` (`b41f29c`), with the owner's permission.

### 2026-11-23 (3) — 1.1.0: foreign invoices, a window per module, checks

- **Asked:** the other default for VAT in the amounts and for purchases outside the ledger; the foreign module; a window per module, as in the owner's desktop converter; a separate window with tables of checks where more can be added; one button on the taskbar; dropping files; all this as a new version. Three points were asked back: which of the two other values (→ 3), protocol or invoice only (→ invoice only), whether to publish (→ 1.1.0 and 1.1.1).
- **Built – engine:** `OperationKind.Foreign`, `AiColumns` as one map per layout, `ReadForeignRow`, `ForeignRules`; `CheckCatalog` (the severity of a finding now comes from it), ticking off and switching off; new checks: a number that cannot be Bulgarian, a foreign supplier outside the ledger with 3, an unusual exchange rate, a foreign invoice that does not look like one; the two defaults, with old settings moved on.
- **Built – windows:** start window with cards, `ModuleForm`, `ChecksForm` with five pages (the former VAT and accounts windows became pages), `FileDrop`.
- **Found and fixed on the way:**
  - VAT split over the accounts could drift a cent from the VAT of the document when the file has three decimals → VAT is balanced first.
  - „Проверки“ opened straight on a page was empty – tabs report nothing before they have a window.
  - Minimising the program closed „Проверки“ (Windows hides owned windows, and that looked like leaving) – it closes only with „← Начало“.
  - „Проверки“ stayed on the desktop while the program was minimised: Windows hides one level of owned windows only → `AppForm` passes it on.
  - A setting changed in one module did not reach the others → `AppState.RaiseSettingsChanged`.
  - Before an update the question about unsaved corrections would have come twice.
  - A document taken out of the file still counted as a duplicate.
  - GitHub's first build failed (`6176f00`): its runner is administrator, so files are taken the older way, and the tests knew only the usual one. Both are tested now; a failed test is named in the annotations of the run, which can be read without signing in.
- **Verified:** §6. With the two settings turned back the new engine writes exactly what 1.0.0 wrote.
- **Published:** tag `v1.1.0` (`3799606`), with the owner's permission.

### 2026-11-23 (2) — Accounts as the file has them

- **Asked:** not to split 4111 into 411/1; to treat „/“ in the main account properly; to leave purchases outside the ledger as they were (1), because it could not be confirmed.
- **Changed** (`284eb48`): `AccountText`; the split and its setting are gone; the same account typed in two ways is one amount; what could mean two things is an error. A note that the protocol is not made for a purchase from abroad.
- **Corrected** (`0714c96`): `Deal_Type.md` said the note comes for every such purchase; it follows the country prefix of the VAT number only.
- **Verified:** on all samples only column B changed, and only by the split; every account equals the source.

### 2026-11-23 (1) — 1.0.0: purchases and sales

- **Asked:** the spin-off itself – a separate C# solution, on GitHub with an update, converting the AI Accounting purchases and sales, with `Deal_Type.md` in Bulgarian for an accountant and the rules of columns A–U (§4).
- **Built** (`bfd66a9`, `a905c2f`): the four projects; reading by position with both sales layouts; the rules with the VAT check; writing with read-back; the windows; settings per client; the update with kept versions; the build and release on GitHub; the documents.
- **Learned** from the vendor's sample and [manual](https://microinvest.net/Documents/1/manual_delta_pro.pdf): „Месец за експорт“ (P) decides in which month a document enters the VAT ledgers; VIES is active only for sales; S–AA are the protocol attached to the invoice, which Делта Pro turns into 453/1 – 453/2; foreign invoices there have N = 1; the fifteen nines are accepted as a number.
- **Found and fixed:** one cent of tolerance was too strict for exports with one row per invoice (→ 0.05 a document); windows laid out in code did not scale with the screen; numbers from real files in the first tests were replaced with invented ones before anything was pushed.
- **Verified:** amounts, VAT, deal types and VIES of 495 real documents recomputed by separate scripts with no difference; the layout equals the vendor's sample; the update rehearsed with two real programs.

---

## 6. How it was checked

| What | How | Result |
|---|---|---|
| Amounts, VAT, cash rows, cell formats, dates | a separate script reads the source and the written `.xls` and computes everything again | 11 exports with 500 documents: 495 written with 0 problems, 5 held back (currency, one also with its period) |
| Deal types and VIES | recomputed the same way | 495, 0 different |
| Accounts | every account of column B against the source | 495 documents and 35 cash rows, 0 wrong |
| Foreign export | all 27 cells of every row recomputed | 16 of 16 |
| Nothing else changed in 1.1.0 | the new engine with the two old settings against the output of 1.0.0, cell by cell | 882 rows, 0 different |
| Layout and lists | against the vendor's `Model.xls` | 27 columns, 39 document types, 18 deal types, 4 VIES values the same |
| `Deal_Type.md` says what the program does | its tables and examples are read by tests | green |
| Windows | 46 tests operate them; the real program with three files: 4 windows, 1 button on the taskbar, minimised and back whole, questions appear, exit code 0 | fine |
| Update | two real programs, forward and back; from GitHub 1.0.0 → 1.1.0 and 1.1.0 → 1.1.1 | identical to the published programs |
| The public repository | names and numbers of all samples against its whole history, amounts against its files | §5 (5) |

**Not checked:**

- **importing a written file into Делта Pro** – nobody has done it yet;
- dragging with a real mouse (what happens after the drop, and that every part of the windows takes drops, is tested);
- the real layout of a screen at 125–200% (checked with simulated scaling);
- more than one sales export in the current layout (9 documents) and one foreign export (16).

To check again after a change of the engine: `dotnet test`, then the scripts in `private/checks` over the samples (on the owner's computer).

---

## 7. Open

**For the accountant** – `Deal_Type.md`, above all:

1. В2 – may a VAT that fits no rate go to к. 12 and 13 as 4?
2. В10, В11, В23 – does Делта Pro take `2;2`, `2;4` and VAT after every account?
3. В12 – is a field for another „Месец за експорт“ needed, to postpone a tax credit?
4. В20 – which values make the protocol (Q–AA), if it is to be written?
5. В21 – are several partners with 999999999999999 kept apart?
6. В5 – is 3 right for a Bulgarian supplier outside the VAT ledger?

**For the owner:** the import into Делта Pro; the test of the update; what else „Проверки“ should check.

**Planned by the owner:** other incoming Excel files; a mode for movements of money (N = 1); then the engine into the web edition.

**Possible next:** the protocol from the AI Accounting export „Протоколи“, joined by the number of the invoice (`Later.md`); currency conversion; quantities of materials.

**Known limits:** the program is not signed, so Windows asks at the first start; updates stop if the repository becomes private; corrections are lost when a file is closed unsaved; one file per module at a time; a foreign supplier in „Покупки“ is known only by the prefix of its VAT number or by a number that cannot be Bulgarian.

---

## 8. Traps for whoever works here

- **A release reaches every installed program.** Ask the owner before every tag; prepare everything first.
- **Sample data:** nothing of it into tests, comments, documents or commit messages – numbers too. Nothing disappears from GitHub with a force-push.
- A `TabControl` reports no page until it has a window; a hidden owner makes `Visible` false; Windows hides owned windows one level deep.
- A control that is added to a shown window is not scaled – build all columns and controls at the start and show or hide them (see `AllChecksPage`).
- `Directory.Build.props`, `Deal_Type.md` (its version line) and `CHANGELOG.md` carry the version; tests compare all three.
- Settings may be read by an older version after going back: new values as text, never as new enum members.
- The runner of GitHub is administrator; a test must not depend on that.
- Windows PowerShell 5.1 writes a byte order mark with `-Encoding UTF8`; long scripts go to a file, not into a command line.
- `dotnet test` on the solution does not rebuild the Release build of the command line tool – build it before comparing outputs.
