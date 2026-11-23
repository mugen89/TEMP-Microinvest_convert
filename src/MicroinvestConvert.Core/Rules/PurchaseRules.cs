using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Core.Rules.RuleText;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// Column N of a purchase. The column of the file the amount is in (the treatment the
/// accountant chose in AI Accounting) comes first; for the amounts with tax credit the VAT is
/// calculated and compared:
/// 20% or 9% → 2, no VAT → 3, anything that fits no rate → 4. See Deal_Type.md.
/// </summary>
internal static class PurchaseRules
{
    public static List<VatGroup> Classify(OperationDocument doc, ConverterOptions options, VatRates rates)
    {
        var groups = new List<VatGroup>();

        foreach (var line in doc.Lines)
        {
            switch (line.Bucket)
            {
                case LedgerBucket.PurchaseNoCredit:
                    line.AutoDealType = DealTypes.PurchaseNoCredit;
                    line.Why = "без данъчен кредит (кол. AB)";
                    break;

                case LedgerBucket.PurchaseNoDiary:
                    line.AutoDealType = options.NoDiaryDealType;
                    line.Why = "без ДДС дневник (кол. AG = ДА)";
                    break;

                case LedgerBucket.PurchaseReverseCharge:
                    line.AutoDealType = DealTypes.PurchaseFullCredit;
                    line.Why = "обратно начисляване по чл. 163а";
                    break;
            }
        }

        ClassifyWithVat(doc, LedgerBucket.PurchaseFullCredit, rates, groups);
        ClassifyWithVat(doc, LedgerBucket.PurchaseUnspecified, rates, groups);
        ClassifyPartial(doc, rates, groups);
        AddIssues(doc, groups, options);
        return groups;
    }

    // ----- пълен данъчен кредит (and amounts with nothing in the ledger columns) -----

    private static void ClassifyWithVat(OperationDocument doc, LedgerBucket bucket, VatRates rates, List<VatGroup> groups)
    {
        var lines = doc.Lines.Where(l => l.Bucket == bucket).ToList();
        if (lines.Count == 0) return;

        // 1. The lines of one declared rate, together: the cents even out over them.
        var pending = new List<LedgerLine>();
        foreach (var group in lines.GroupBy(l => l.DeclaredRate))
        {
            var same = group.ToList();
            var match = MatchSum(rates, same, group.Key);
            if (match.IsResolved) Accept(doc, bucket, same, match, groups);
            else pending.AddRange(same);
        }

        if (pending.Count == 0) return;

        // 2. Each remaining line by its own amounts.
        var own = pending.ToLookup(l => MatchLine(rates, l).IsResolved);
        var unresolved = own[false].ToList();

        if (unresolved.Count > 0 && pending.Count > 1)
        {
            // 3. VAT written on a line of its own: all of them together may still be a rate.
            var whole = MatchSum(rates, pending, null);
            if (whole is { IsResolved: true, Rate: > 0m })
            {
                Accept(doc, bucket, pending, whole, groups);
                return;
            }
        }

        foreach (var group in own[true].GroupBy(l => MatchLine(rates, l).Rate))
        {
            var same = group.ToList();
            var match = MatchSum(rates, same, group.Key);

            // Every line fits, so the sum is accepted even if its cents pile up past the limit.
            if (!match.IsResolved)
                match = new VatMatch(VatStatus.WithinTolerance, group.Key, rates.Expected(same.Sum(l => l.Base), group.Key ?? 0m));
            Accept(doc, bucket, same, match, groups);
        }

        if (unresolved.Count > 0)
        {
            var match = MatchSum(rates, unresolved, Common(unresolved));
            if (match.IsResolved)   // each is off, yet their sum fits — do not hide it behind a rate
                match = new VatMatch(VatStatus.Unresolved, null, match.Expected);
            Accept(doc, bucket, unresolved, match, groups);
        }
    }

    private static VatMatch MatchSum(VatRates rates, List<LedgerLine> lines, decimal? declared) =>
        rates.Match(lines.Sum(l => l.Base), lines.Sum(l => l.Vat), declared, rates.DocumentTolerance);

    private static VatMatch MatchLine(VatRates rates, LedgerLine line) =>
        rates.Match(line.Base, line.Vat, line.DeclaredRate, rates.LineTolerance);

    private static void Accept(OperationDocument doc, LedgerBucket bucket, List<LedgerLine> lines, VatMatch match,
        List<VatGroup> groups)
    {
        var (deal, why) = Decide(bucket, lines, match);
        foreach (var line in lines)
        {
            line.AutoDealType = deal;
            line.Why = why;
        }

        groups.Add(NewGroup(doc, bucket, lines, match));
    }

    private static (int Deal, string Why) Decide(LedgerBucket bucket, List<LedgerLine> lines, VatMatch match)
    {
        string source = bucket == LedgerBucket.PurchaseFullCredit ? "пълен ДК" : "по сумите";
        decimal? declared = Common(lines);

        return match.Status switch
        {
            VatStatus.Exact => (DealTypes.PurchaseFullCredit, $"{source}, ДДС е {Percent(match.Rate)} от основата"),
            VatStatus.WithinTolerance => (DealTypes.PurchaseFullCredit,
                $"{source}, ДДС е {Percent(match.Rate)} с разлика {Money(lines.Sum(l => l.Vat) - match.Expected)}"),
            VatStatus.Zero => (DealTypes.PurchaseNoCredit, $"{source}, но без ДДС (0%)"),
            // A zero document: only the declared rate speaks.
            VatStatus.Empty => declared == 0m
                ? (DealTypes.PurchaseNoCredit, $"{source}, нулев документ, 0%")
                : (DealTypes.PurchaseFullCredit, $"{source}, нулев документ"),
            _ => (DealTypes.PurchasePartialCredit, $"{source}, ДДС не отговаря на 20% или 9%"),
        };
    }

    // ----- частичен данъчен кредит -----

    private static void ClassifyPartial(OperationDocument doc, VatRates rates, List<VatGroup> groups)
    {
        var lines = doc.Lines.Where(l => l.Bucket == LedgerBucket.PurchasePartialCredit).ToList();
        if (lines.Count == 0) return;

        foreach (var line in lines)
        {
            line.AutoDealType = DealTypes.PurchasePartialCredit;
            line.Why = "частичен данъчен кредит (кол. AE/AF)";
        }

        groups.Add(NewGroup(doc, LedgerBucket.PurchasePartialCredit, lines, MatchSum(rates, lines, Common(lines))));
    }

    // ----- what the user is told -----

    private static void AddIssues(OperationDocument doc, List<VatGroup> groups, ConverterOptions options)
    {
        if (doc.Lines.Any(l => l.Bucket == LedgerBucket.PurchaseReverseCharge && l.DealTypeOverride == null))
            doc.Issues.Add(new Issue(IssueCode.ReverseCharge,
                "Обратно начисляване по чл. 163а – изисква протокол (колони Q–AA), който ще бъде добавен в следваща версия. " +
                "Документът не се записва, освен ако не зададете вид сделка на редовете му."));

        bool foreign = new PartnerIdentity(doc.Eik, doc.VatNumber).IsForeign;
        AddNoDiaryIssues(doc, options, foreign);

        if (doc.Lines.Any(l => l.Bucket == LedgerBucket.PurchaseUnspecified))
            doc.Issues.Add(new Issue(IssueCode.Unspecified,
                "Има сума без стойност в колоните за дневника (AB–AG) – видът сделка е определен само по сумите."));

        foreach (var group in groups)
        {
            // What the user decided by hand is no longer a question.
            if (group.Lines.All(l => l.DealTypeOverride != null)) continue;
            AddVatIssue(doc, group);
        }

        if (foreign && doc.Lines.Any(l => l.Bucket != LedgerBucket.PurchaseNoDiary && l.DealTypeOverride == null))
            doc.Issues.Add(new Issue(IssueCode.ForeignSupplier,
                $"Доставчикът е чужд ({doc.VatNumber}). Обикновено такава доставка е с протокол – вид сделка 1 и колони Q–AA " +
                "(следваща версия). Проверете вида сделка."));
    }

    private static void AddNoDiaryIssues(OperationDocument doc, ConverterOptions options, bool foreign)
    {
        var lines = doc.Lines.Where(l => l.Bucket == LedgerBucket.PurchaseNoDiary).ToList();
        if (lines.Count == 0) return;

        doc.Issues.Add(new Issue(IssueCode.NoDiary, options.General.NoDiary switch
        {
            NoDiaryMode.Skip => "„Без ДДС дневник“ – не се записва (настройка).",
            NoDiaryMode.NoCredit => "„Без ДДС дневник“ – вид сделка 3, влиза в дневника, к. 9 (настройка).",
            _ => "„Без ДДС дневник“ – вид сделка 1, не влиза в дневника (настройка)." +
                 (foreign ? $" Доставчикът е чужд ({doc.VatNumber}): протоколът за самоначисляване на ДДС (кол. Q–AA) не се създава." : ""),
        }));

        // With 3 the invoice is in the ledger. A purchase from abroad is there with its protocol.
        if (!lines.Any(l => l.DealTypeOverride == null && l.AutoDealType == DealTypes.PurchaseNoCredit)) return;

        bool impossible = !PartnerIdentity.HasPossibleLength(doc.Eik) || !PartnerIdentity.HasValidEikCheckDigit(doc.Eik);
        if (foreign)
            doc.Issues.Add(new Issue(IssueCode.ForeignNoDiary,
                $"Доставчикът е чужд ({doc.VatNumber}), а с вид сделка 3 документът влиза в дневника. " +
                "Ако за него има протокол, изберете вид 1 – иначе ще е в дневника два пъти."));
        else if (impossible)
            doc.Issues.Add(new Issue(IssueCode.ForeignNoDiary,
                $"Номерът {doc.Eik} не е български ЕИК, а с вид сделка 3 документът влиза в дневника. " +
                "Ако доставчикът е чужд и има протокол, изберете вид 1 – иначе ще е в дневника два пъти."));
    }

    private static void AddVatIssue(OperationDocument doc, VatGroup group)
    {
        switch (group.Status)
        {
            case VatStatus.Unresolved when group.Bucket != LedgerBucket.PurchasePartialCredit:
                doc.Issues.Add(new Issue(IssueCode.VatUnresolved,
                    $"ДДС {Money(group.Vat)} при основа {Money(group.Base)} не отговаря на 20% или 9%" +
                    (group.Percent is { } p ? $" (излиза {Money(p)}%)" : "") + " – вид сделка 4. Проверете."));
                break;

            case VatStatus.Zero when group.DeclaredRate > 0m:
                doc.Issues.Add(new Issue(IssueCode.VatZeroButDeclared,
                    $"Обявена е ставка {Percent(group.DeclaredRate)}, а ДДС е 0 – вид сделка 3. Проверете."));
                break;

            case VatStatus.WithinTolerance:
                doc.Issues.Add(new Issue(IssueCode.VatWithinTolerance,
                    $"ДДС {Money(group.Vat)} се различава с {Money(group.Difference)} от {Percent(group.Rate)} върху " +
                    $"{Money(group.Base)} – в допустимата разлика."));
                break;
        }

        if (group.Status is VatStatus.Exact or VatStatus.WithinTolerance && group.DeclaredRate is { } declared &&
            group.Rate != declared)
            doc.Issues.Add(new Issue(IssueCode.VatDifferentRate,
                $"Обявена е ставка „{(group.Lines[0].RateText.Length > 0 ? group.Lines[0].RateText : Percent(declared))}“, " +
                $"а сумите отговарят на {Percent(group.Rate)}."));
    }

    // ----- helpers -----

    private static VatGroup NewGroup(OperationDocument doc, LedgerBucket bucket, List<LedgerLine> lines, VatMatch match) => new()
    {
        Document = doc,
        Bucket = bucket,
        Lines = lines,
        Status = match.Status,
        Base = lines.Sum(l => l.Base),
        Vat = lines.Sum(l => l.Vat),
        DeclaredRate = Common(lines),
        Rate = match.Rate,
        Expected = match.Expected,
    };

    /// <summary>The rate all lines declare; null when they differ.</summary>
    private static decimal? Common(List<LedgerLine> lines) =>
        lines.Select(l => l.DeclaredRate).Distinct().Count() == 1 ? lines[0].DeclaredRate : null;
}
