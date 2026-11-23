using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Core.Rules.RuleText;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// Turns what the file says (and the user corrected) into what is written: deal types,
/// accounts, amounts in cents, dates — and the list of problems. Run again after every
/// correction; it keeps no state of its own.
/// </summary>
public sealed class DocumentProcessor
{
    public ConverterOptions Options { get; }
    public VatRates Rates { get; }

    public DocumentProcessor(ConverterOptions options)
    {
        Options = options;
        Rates = new VatRates(options.General);
    }

    public void Process(OperationBatch batch)
    {
        foreach (var doc in batch.Documents) ProcessOne(doc);
        MarkDuplicates(batch);
    }

    /// <summary>After a correction of one document of the batch.</summary>
    public void Process(OperationBatch batch, OperationDocument doc)
    {
        ProcessOne(doc);
        MarkDuplicates(batch);
    }

    private void ProcessOne(OperationDocument doc)
    {
        doc.Issues.Clear();
        doc.Issues.AddRange(doc.ReadIssues);

        CheckIdentity(doc);
        ResolveDate(doc);
        CheckCurrency(doc);

        doc.VatGroups = doc.Kind == OperationKind.Purchase
            ? PurchaseRules.Classify(doc, Options, Rates)
            : SaleRules.Classify(doc, Rates);

        doc.SkippedBySetting = Options.General.NoDiary == NoDiaryMode.Skip &&
                               doc.Lines.Count > 0 &&
                               doc.Lines.All(l => l.Bucket == LedgerBucket.PurchaseNoDiary && l.DealTypeOverride == null);

        ResolveAccounts(doc);
        BuildSegments(doc);
        CheckTotals(doc);

        if (doc.Lines.Any(l => l.DealTypeOverride != null && l.DealTypeOverride != l.AutoDealType))
            doc.Issues.Add(new Issue(IssueSeverity.Info, IssueCode.Overridden, "Видът сделка е зададен ръчно."));
    }

    // ----- header -----

    private static void CheckIdentity(OperationDocument doc)
    {
        if (doc.DocType.Length == 0)
            doc.Issues.Add(new Issue(IssueSeverity.Error, IssueCode.MissingDocumentType,
                doc.SourceDocType.Length > 0
                    ? $"Непознат вид документ „{doc.SourceDocType}“ – изберете тип."
                    : "Липсва вид на документа – изберете тип."));
        if (doc.Number.Length == 0)
            doc.Issues.Add(new Issue(IssueSeverity.Error, IssueCode.MissingNumber, "Липсва номер на документ."));
        if (doc.PartnerName.Length == 0)
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.MissingPartner, "Липсва име на контрагент."));
        if (doc.Reason.Length == 0)
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.MissingReason, "Липсва основание."));
        if (!PartnerIdentity.HasValidEikCheckDigit(doc.Eik))
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.EikCheckDigit,
                $"ЕИК {doc.Eik} не минава контролната проверка – сгрешена цифра или номер на чужда фирма."));
    }

    private void ResolveDate(OperationDocument doc)
    {
        doc.AccountingDate = null;
        if (doc.Date is not { } date)
        {
            doc.Issues.Add(new Issue(IssueSeverity.Error, IssueCode.MissingDate, "Липсва дата на документа."));
            return;
        }

        if (doc.AccountingDateOverride is { } chosen)
        {
            doc.AccountingDate = chosen;
            if (chosen < date)
                doc.Issues.Add(new Issue(IssueSeverity.Error, IssueCode.PeriodBeforeDocument,
                    $"Счетоводната дата {chosen:dd.MM.yyyy} е преди датата на документа {date:dd.MM.yyyy}."));
            return;
        }

        if (Options.General.AccountingDateEqualsDocumentDate)
        {
            doc.AccountingDate = date;
            return;
        }

        if (doc.Period == null)
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.MissingPeriod,
                "Липсва данъчен период (кол. H) – счетоводната дата е датата на документа."));

        var resolved = AccountingDates.Resolve(date, doc.Period);
        doc.AccountingDate = resolved.Date;
        if (resolved.AfterPeriod)
            doc.Issues.Add(new Issue(IssueSeverity.Error, IssueCode.PeriodBeforeDocument,
                $"Данъчният период {doc.Period:MM.yyyy} е преди месеца на документа ({date:dd.MM.yyyy}). " +
                "Поправете периода или датата, или включете „Сч. дата = дата на документа“."));
    }

    private static void CheckCurrency(OperationDocument doc)
    {
        if (doc.Currency.Length == 0 || doc.Date is not { } date) return;

        string book = BookCurrency.On(date);
        if (doc.Currency != book)
            doc.Issues.Add(new Issue(IssueSeverity.Error, IssueCode.ForeignCurrency,
                $"Сумите са в {doc.Currency}, а не в {book}. Превалутирането ще бъде добавено в следваща версия. " +
                $"Ако сте поправили сумите, сменете валутата на {book}."));
    }

    // ----- accounts -----

    private void ResolveAccounts(OperationDocument doc)
    {
        bool purchase = doc.Kind == OperationKind.Purchase;

        doc.CounterAccountOut = AccountCodes.Format(doc.CounterAccount, "", Options);
        CheckAccount(doc, doc.CounterAccountOut, IssueCode.MissingCounterAccount,
            purchase ? "сметката на доставчика (Кт)" : "сметката на клиента (Дт)");

        string cash = doc.CashAccount.Length > 0 ? doc.CashAccount : Options.General.DefaultCashAccount;
        doc.CashAccountOut = AccountCodes.Format(cash, "", Options);
        if (doc.IsCash && Options.General.CashRows)
            CheckAccount(doc, doc.CashAccountOut, IssueCode.MissingCashAccount, "касовата сметка (плащане в брой)");

        foreach (var group in doc.Lines.GroupBy(l => l.AccountKey))
        {
            string account = AccountCodes.Format(group.First().Account, group.First().Analytic, Options);
            string what = purchase ? "счетоводната сметка (Дт)" : "приходната сметка (Кт)";
            string rows = string.Join(", ", group.Select(l => l.SourceRow).Distinct());
            CheckAccount(doc, account, IssueCode.MissingAccount, $"{what} на ред {rows}");
        }
    }

    private static void CheckAccount(OperationDocument doc, string account, IssueCode missing, string what)
    {
        string? problem = AccountCodes.Problem(account);
        if (problem == null) return;

        doc.Issues.Add(account.Length == 0
            ? new Issue(IssueSeverity.Error, missing, $"Липсва {what}.")
            : new Issue(IssueSeverity.Error, IssueCode.BadAccount, $"Неправилна сметка „{account}“ – {problem} ({what})."));
    }

    // ----- columns A, B, N, O -----

    /// <summary>
    /// The amounts of a line for its deal type. Without tax credit (3) and outside the ledger
    /// (1) the VAT of a purchase is part of the expense.
    /// </summary>
    private static (decimal Base, decimal Vat) AmountsOf(OperationDocument doc, LedgerLine line)
    {
        if (doc.Kind == OperationKind.Sale) return (line.Base, line.Vat);

        switch (line.DealType)
        {
            case DealTypes.Internal:
            case DealTypes.PurchaseNoCredit:
                return (line.Gross, 0m);

            case DealTypes.PurchaseFullCredit:
            case DealTypes.PurchasePartialCredit:
                // „Без кредит" changed by hand to „с кредит": the gross gives its VAT back.
                bool gross = line.Bucket is LedgerBucket.PurchaseNoCredit or LedgerBucket.PurchaseNoDiary;
                if (gross && line.Vat == 0m && line.SellerVat != 0m && line.Base == line.SellerTotal)
                    return (line.Base - line.SellerVat, line.SellerVat);
                break;
        }

        return (line.Base, line.Vat);
    }

    private void BuildSegments(OperationDocument doc)
    {
        // Credit notes are written with a minus; the platform usually sends them so.
        decimal gross = doc.Lines.Sum(l => l.Gross);
        bool creditNote = DocumentTypes.IsCreditNote(doc.DocType);
        decimal sign = creditNote && gross > 0m ? -1m : 1m;

        if (sign < 0m)
            doc.Issues.Add(new Issue(IssueSeverity.Info, IssueCode.SignFlipped,
                "Кредитно известие с положителни суми – записват се с минус."));
        else if (!creditNote && gross < 0m)
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.NegativeDocument,
                $"Общата сума е отрицателна ({Money(gross)}), а документът не е кредитно известие."));

        string vatAccount = doc.Kind == OperationKind.Purchase ? Options.PurchaseVatAccount : Options.SaleVatAccount;
        var segments = new List<EntrySegment>();

        foreach (var deal in Ordered(doc.Lines, l => l.DealType))
        {
            var vatLines = new List<LedgerLine>();
            foreach (var account in Ordered(deal, l => AccountCodes.Format(l.Account, l.Analytic, Options)))
            {
                var lines = account.ToList();
                segments.Add(NewSegment(account.Key, NameOf(lines), false, deal.Key, lines,
                    sign * lines.Sum(l => AmountsOf(doc, l).Base)));

                if (Options.General.VatLines == VatLines.PerAccount)
                    AddVat(doc, segments, vatAccount, deal.Key, lines, sign);
                vatLines.AddRange(lines);
            }

            if (Options.General.VatLines == VatLines.PerDealType)
                AddVat(doc, segments, vatAccount, deal.Key, vatLines, sign);
        }

        RoundSegments(segments);
        doc.Segments = segments;

        doc.TotalVat = segments.Where(s => s.IsVat).Sum(s => s.Amount);
        doc.TotalBase = segments.Where(s => !s.IsVat).Sum(s => s.Amount);
        doc.TotalGross = doc.TotalBase + doc.TotalVat;

        if (doc.Kind == OperationKind.Sale)
        {
            if (doc.ViesOverride == null && segments.Select(s => s.Vies).Distinct().Count() > 1)
                doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.ViesMixed,
                    "Сумите имат различен VIES – в кол. O се записва по една стойност за всяка сума. Проверете как го приема Делта Pro."));
        }
    }

    // Largest first; equal ones keep the order of the file.
    private static List<IGrouping<TKey, LedgerLine>> Ordered<TKey>(IEnumerable<LedgerLine> lines, Func<LedgerLine, TKey> key) =>
        lines.GroupBy(key).OrderByDescending(g => Math.Abs(g.Sum(l => l.Gross))).ToList();

    private void AddVat(OperationDocument doc, List<EntrySegment> segments, string account, int deal, List<LedgerLine> lines, decimal sign)
    {
        decimal vat = lines.Sum(l => AmountsOf(doc, l).Vat);
        bool zeroDocument = vat == 0m && lines.All(l => l.Gross == 0m) && deal is
            DealTypes.PurchaseFullCredit or DealTypes.PurchasePartialCredit or DealTypes.Sale20 or DealTypes.Sale9;

        if (vat != 0m || zeroDocument)
            segments.Add(NewSegment(account, "ДДС", true, deal, lines, sign * vat));
    }

    private static EntrySegment NewSegment(string account, string name, bool isVat, int deal, List<LedgerLine> lines, decimal raw) => new()
    {
        Account = account,
        AccountName = name,
        IsVat = isVat,
        DealType = deal,
        Vies = DealTypes.Get(deal).Vies,
        Lines = lines,
        Raw = raw,
        Amount = Cents(raw),
    };

    private static string NameOf(List<LedgerLine> lines) =>
        lines.Select(l => l.AnalyticName.Length > 0 ? l.AnalyticName : l.AccountName).FirstOrDefault(t => t.Length > 0) ?? "";

    internal static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Every amount is rounded on its own, so with more than two decimals in the file the sum
    // can drift off the total of the document. The rounded-off amounts — and only they — give
    // or take a cent until it fits: first the ones rounded the other way, bases before VAT.
    private static void RoundSegments(List<EntrySegment> segments)
    {
        decimal residual = Cents(segments.Sum(s => s.Raw)) - segments.Sum(s => s.Amount);
        if (residual == 0m) return;

        decimal step = residual > 0m ? 0.01m : -0.01m;
        var candidates = segments
            .Where(s => s.Raw != s.Amount)
            .OrderBy(s => s.IsVat)
            .ThenBy(s => Math.Sign(s.Raw - s.Amount) == Math.Sign(residual) ? 0 : 1)
            .ThenByDescending(s => Math.Abs(s.Raw - s.Amount))
            .ToList();

        foreach (var segment in candidates)
        {
            if (residual == 0m) break;
            segment.Amount += step;
            residual -= step;
        }
    }

    // ----- control -----

    private static void CheckTotals(OperationDocument doc)
    {
        decimal inLedger = doc.Lines.Sum(l => l.Gross);
        decimal inFile = doc.Lines.Sum(l => l.SellerTotal);

        if (Math.Abs(inLedger - inFile) > 0.01m)
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.AmountsDoNotAdd,
                $"Сборът на сумите ({Money(inLedger)}) се различава от общата сума на документа във файла ({Money(inFile)})."));

        if (inLedger == 0m && doc.Lines.All(l => l.Base == 0m && l.Vat == 0m))
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.ZeroDocument,
                "Нулев документ – проверете дали Делта Pro го приема."));

        if (doc.Kind == OperationKind.Sale &&
            doc.Segments.Any(s => s.IsVat && s.Amount != 0m && s.DealType is not (DealTypes.Sale20 or DealTypes.Sale9
                or DealTypes.SaleIntraCommunityAcquisition or DealTypes.SaleArticle82 or DealTypes.SaleOtherTax)))
            doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.VatMismatch,
                "Има ДДС при вид сделка, който е без ДДС."));
    }

    private static void MarkDuplicates(OperationBatch batch)
    {
        foreach (var doc in batch.Documents) doc.Issues.RemoveAll(i => i.Code == IssueCode.Duplicate);

        var same = batch.Documents
            .Where(d => d.Number.Length > 0)
            .GroupBy(d => (d.DocType, d.Number, Partner: d.Eik.Length > 0 ? d.Eik : d.VatNumber.Length > 0 ? d.VatNumber : d.PartnerName))
            .Where(g => g.Count() > 1);

        foreach (var group in same)
            foreach (var doc in group)
            {
                string others = string.Join(", ", group.Where(d => d != doc).Select(d => "№ " + d.Index));
                doc.Issues.Add(new Issue(IssueSeverity.Warning, IssueCode.Duplicate,
                    $"Дублиран документ – същият тип, номер и контрагент като {others}."));
            }
    }
}
