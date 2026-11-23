using Microsoft.Data.Sqlite;
using MicroinvestConvert.App;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Data;
using static MicroinvestConvert.Tests.SomeRates;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mic-db-" + Guid.NewGuid());

    private string PathOf(string name = Database.FileName) => Path.Combine(_folder, name);

    public DatabaseTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    // ----- the file -----

    [Fact]
    public void It_is_made_when_there_is_none_and_nothing_holds_it_afterwards()
    {
        string path = Path.Combine(_folder, "нова папка", Database.FileName);
        new SettingStore(Database.Open(path)).Set("a", "1");
        Assert.True(File.Exists(path));
        File.Move(path, path + ".moved");   // not open anywhere
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void What_is_kept_is_there_the_next_time()
    {
        var settings = new SettingStore(Database.Open(PathOf()));
        Assert.Null(settings.Get("rates.from"));
        settings.Set("rates.from", "2026-01-01");
        settings.Set("rates.from", "2026-02-01");
        settings.Set("друго", "текст с „кавички“ и 'апостроф'");

        var again = new SettingStore(Database.Open(PathOf()));
        Assert.Equal(("2026-02-01", "текст с „кавички“ и 'апостроф'"), (again.Get("rates.from"), again.Get("друго")));
        again.Set("rates.from", null);
        Assert.Null(new SettingStore(Database.Open(PathOf())).Get("rates.from"));
    }

    [Fact]
    public void A_damaged_file_is_put_aside_and_a_new_one_is_started()
    {
        File.WriteAllText(PathOf(), "това не е база данни " + new string('x', 2000));
        Assert.Throws<DatabaseException>(() => Database.Open(PathOf()));

        var database = Database.OpenOrReplace(PathOf(), out string problem);
        Assert.Contains("повредена", problem);
        new SettingStore(database).Set("a", "1");
        Assert.StartsWith("това не е база данни", File.ReadAllText(Assert.Single(Directory.GetFiles(_folder, "*.bad-*"))));

        Database.OpenOrReplace(PathOf(), out problem);
        Assert.Equal("", problem);
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_not_taken()
    {
        Database.Open(PathOf());
        File.SetAttributes(PathOf(), FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<DatabaseException>(() => Database.Open(PathOf()));
        }
        finally
        {
            File.SetAttributes(PathOf(), FileAttributes.Normal);
        }
    }

    [Fact]
    public void A_file_of_a_newer_program_is_used_as_it_is()
    {
        new SettingStore(Database.Open(PathOf())).Set("a", "1");
        Sql($"PRAGMA user_version = {Database.Schema.Count + 5}; ALTER TABLE Clients ADD COLUMN Newer TEXT NOT NULL DEFAULT ''; CREATE TABLE Newer (X);");

        var database = Database.Open(PathOf());
        Assert.Equal("1", new SettingStore(database).Get("a"));
        new ClientStore(database).Save(new ClientSettings { Name = "Фирма" });
        Assert.Equal("Фирма", Assert.Single(new ClientStore(database).Load()).Name);
        Assert.Equal((long)Database.Schema.Count + 5, Sql("PRAGMA user_version"));
        Assert.Empty(Directory.GetFiles(_folder, "*.v*"));
    }

    [Fact]
    public void While_another_program_writes_it_is_said_so()
    {
        var settings = new SettingStore(Database.Open(PathOf()));
        using var other = new SqliteConnection($"Data Source={PathOf()};Pooling=False");
        other.Open();
        using var transaction = other.BeginTransaction(deferred: false);
        Database.Open(PathOf());                                                   // can be opened
        Assert.Contains("заета", Assert.Throws<DatabaseException>(() => settings.Set("a", "1")).Message);
        transaction.Rollback();
        settings.Set("a", "1");
    }

    private object? Sql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={PathOf()};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    // ----- rates -----

    [Fact]
    public void Rates_come_back_as_they_were_kept()
    {
        var store = new RateStore(Database.Open(PathOf()));
        Assert.Null(store.Latest());
        store.Save([KikInfoPage.Parse(RatePages.InEuro()), KikInfoPage.Parse(RatePages.InLeva())],
            new Dictionary<DateTime, DateTime> { [Day("20.11.2026")] = Day("20.11.2026"), [Day("22.11.2026")] = Day("20.11.2026"), [Day("31.12.2025")] = Day("30.12.2025") });

        foreach (var s in new[] { store, new RateStore(Database.Open(PathOf())) })
        {
            Assert.Equal(new ExchangeRate("USD", Day("22.11.2026"), Day("20.11.2026"), 1.1616m), s.Find("USD", Day("22.11.2026")));
            Assert.Equal(1.95583m / 1.7m, s.Find("USD", Day("31.12.2025"))!.PerEuro);
            Assert.Null(s.Find("USD", Day("21.11.2026")));
            Assert.Null(s.Find("XYZ", Day("20.11.2026")));
            Assert.Equal((true, false), (s.Knows(Day("22.11.2026")), s.Knows(Day("21.11.2026"))));
            Assert.Equal((Day("20.11.2026"), 3), (s.Latest(), s.Days().Count));
            Assert.Equal(7, s.RatesOf(Day("22.11.2026")).Count);
            Assert.Contains(("USD", "Щатски долар"), s.Currencies());
            Assert.Equal([Day("22.11.2026"), Day("20.11.2026"), Day("31.12.2025")], s.History("USD").Select(r => r.Day));
            Assert.Empty(s.RatesOf(Day("21.11.2026")));
        }
    }

    [Fact]
    public void A_table_that_is_read_again_replaces_the_old_one()
    {
        var store = new RateStore(Database.Open(PathOf()));
        var days = new Dictionary<DateTime, DateTime> { [Day("20.11.2026")] = Day("20.11.2026") };
        store.Save([KikInfoPage.Parse(RatePages.InEuro())], days);
        Assert.Equal(1.1616m, store.Find("USD", Day("20.11.2026"))!.PerEuro);
        store.Save([KikInfoPage.Parse(RatePages.InEuro().Replace("1.161600", "1.162600"))], days);
        Assert.Equal(1.1626m, store.Find("USD", Day("20.11.2026"))!.PerEuro);
    }

    [Fact]
    public void To_the_rules_a_database_that_is_gone_has_no_rates()
    {
        var store = new RateStore(Database.Open(PathOf()));
        File.Delete(PathOf());
        File.WriteAllText(PathOf(), "нещо друго " + new string('x', 2000));
        Assert.Null(store.Find("USD", Day("20.11.2026")));
        Assert.False(store.Knows(Day("20.11.2026")));
        Assert.Throws<DatabaseException>(() => store.Days());
    }

    // ----- clients -----

    [Fact]
    public void A_client_needs_only_a_name()
    {
        var clients = new ClientBook(new ClientStore(Database.Open(PathOf())));
        clients.Save(new ClientSettings { Name = "Първа Фирма" });
        clients.Save(new ClientSettings { Name = "Втора Фирма" });   // two without an ЕИК
        clients.Save(new ClientSettings
        {
            Name = "Трета Фирма", Eik = SupplierEik, VatNumber = SupplierVat, Note = "бележка", PurchaseVatAccount = "453/7", SaleVatAccount = "453/8",
            CashAccount = "501/1", Accounts = { ["401"] = "401/1" }, AccountCurrencies = { ["401"] = "USD", ["4012"] = "GBP" },
        });

        var again = new ClientBook(new ClientStore(Database.Open(PathOf())));
        Assert.Equal(["Втора Фирма", "Първа Фирма", "Трета Фирма"], again.All.Select(c => c.Name));
        var third = again.Find(SupplierEik, "")!;
        Assert.Equal((SupplierVat, "бележка", "453/7", "453/8", "501/1"), (third.VatNumber, third.Note, third.PurchaseVatAccount, third.SaleVatAccount, third.CashAccount));
        Assert.Equal(("401/1", "USD", "GBP"), (third.Accounts["401"], third.AccountCurrencies["401"], third.AccountCurrencies["4012"]));
        Assert.False(third.Accounts.ContainsKey("4012"));
    }

    [Fact]
    public void Changes_and_deletion_are_kept()
    {
        var clients = new ClientBook(new ClientStore(Database.Open(PathOf())));
        var client = new ClientSettings { Name = "Фирма", Accounts = { ["401"] = "401/1" } };
        clients.Save(client);
        client.Name = "Друго име";
        client.Accounts.Remove("401");
        client.Accounts["411"] = "411/1";
        clients.Save(client);

        var again = new ClientBook(new ClientStore(Database.Open(PathOf())));
        Assert.Equal(["411"], Assert.Single(again.All).Accounts.Keys);
        Assert.Equal("Друго име", again.All[0].Name);
        again.Delete(again.All[0]);
        Assert.Empty(new ClientBook(new ClientStore(Database.Open(PathOf()))).All);
        Assert.Equal(0L, Sql("SELECT COUNT(*) FROM ClientAccounts"));
    }

    // ----- where it is -----

    private AppState App(params string[] databases) => new(PathOf("settings.json"), databases, new FakeSource());

    [Fact]
    public void Where_the_first_place_cannot_be_written_the_next_one_is_taken_without_a_word()
    {
        Directory.CreateDirectory(PathOf("заето.db"));   // a folder with the name of the file
        using var app = App(PathOf("заето.db"), PathOf("друго.db"));
        Assert.Equal((PathOf("друго.db"), ""), (app.DatabasePath, app.DatabaseProblem));
        Assert.Null(app.Save(new ClientSettings { Name = "Фирма" }));
        Assert.True(File.Exists(PathOf("друго.db")));
    }

    [Fact]
    public void A_database_that_is_busy_is_not_replaced_by_one_in_another_place()
    {
        using (var app = App(PathOf())) Assert.Null(app.Save(new ClientSettings { Name = "Фирма" }));

        using (var other = new SqliteConnection($"Data Source={PathOf()};Pooling=False"))
        {
            other.Open();
            using var begin = other.CreateCommand();
            begin.CommandText = "BEGIN EXCLUSIVE";
            begin.ExecuteNonQuery();
            using var app = App(PathOf(), PathOf("друго.db"));
            Assert.Equal("", app.DatabasePath);
            Assert.Contains("заета", app.DatabaseProblem);
            Assert.Equal("Валутни курсове: базата данни не е достъпна", app.Rates.Summary);
            Assert.False(File.Exists(PathOf("друго.db")));
        }

        using var again = App(PathOf(), PathOf("друго.db"));
        Assert.Equal(("", "Фирма"), (again.DatabaseProblem, Assert.Single(again.Clients.All).Name));
    }

    [Fact]
    public void Without_any_database_the_program_works_and_says_so()
    {
        Directory.CreateDirectory(PathOf("заето.db"));
        using var app = App(PathOf("заето.db"));
        Assert.Equal("", app.DatabasePath);
        Assert.Contains("не може да бъде отворена", app.DatabaseProblem);
        Assert.Equal("Валутни курсове: базата данни не е достъпна", app.Rates.Summary);
        Assert.Null(app.Rates.Table.Find("USD", Day("20.11.2026")));

        Assert.Null(app.Save(new ClientSettings { Name = "Фирма", Eik = "1" }));   // until the program is closed
        Assert.Equal("Фирма", app.Clients.Find("1", "")!.Name);
    }

    [Fact]
    public void The_clients_of_the_earlier_versions_are_taken_once()
    {
        File.WriteAllText(PathOf("settings.json"),
            "{\"version\":2,\"clients\":{\"987654326\":{\"name\":\"Фирма ЕООД\",\"purchaseVatAccount\":\"453/9\",\"accounts\":{\"401\":\"401/1\"}},\"111\":{\"saleVatAccount\":\"453/8\"}}}");

        using (var app = App(PathOf()))
        {
            var client = app.Clients.Find("987654326", "")!;
            Assert.Equal(("Фирма ЕООД", "453/9", "401/1"), (client.Name, client.PurchaseVatAccount, client.Accounts["401"]));
            Assert.Equal("111", app.Clients.Find("111", "")!.Name);   // without a name the number is one
            app.Delete(client);
        }

        using (var app = App(PathOf())) Assert.Equal(["111"], app.Clients.All.Select(c => c.Name));
    }
}

public class ClientBookTests
{
    private static ClientBook Book(params ClientSettings[] clients)
    {
        var book = new ClientBook(null);
        foreach (var client in clients) book.Save(client);
        return book;
    }

    [Fact]
    public void A_file_finds_its_client_by_number_and_then_by_name()
    {
        var withNumber = new ClientSettings { Name = "Алфа ЕООД", Eik = "1" };
        var withoutNumber = new ClientSettings { Name = "Бета ООД" };
        var book = Book(withNumber, withoutNumber);

        Assert.Same(withNumber, book.Find("1", "съвсем друго име"));
        Assert.Same(withNumber, book.Find("", "„АЛФА“ ЕООД"));
        Assert.Same(withoutNumber, book.Find("2", "Бета  ООД"));   // its number is not written down yet
        Assert.Null(book.Find("2", "Алфа ЕООД"));                  // the same name with another number
        Assert.Null(book.Find("", ""));
        Assert.Null(book.Find("3", "Гама"));
    }

    [Fact]
    public void A_new_client_takes_the_name_and_number_of_the_file()
    {
        var client = Book().FindOrNew("1", "Алфа ЕООД");
        Assert.Equal(("Алфа ЕООД", "1", 0L), (client.Name, client.Eik, client.Id));
        Assert.Equal("7", Book().FindOrNew("7", "").Name);
    }

    [Fact]
    public void A_number_belongs_to_one_client_and_a_name_is_needed()
    {
        var alpha = new ClientSettings { Name = "Алфа", Eik = "1" };
        var beta = new ClientSettings { Name = "Бета" };
        var book = Book(alpha, beta);
        Assert.Contains("е на „Алфа“", book.Problem(null, "Нова", "1"));
        Assert.Contains("трябва да има име", book.Problem(null, " – ", ""));
        Assert.Contains("Вече има", book.Problem(null, "бета", ""));
        Assert.Null(book.Problem(null, "Бета", "2"));   // two of a name are told apart by their numbers
        Assert.Null(book.Problem(alpha, "Алфа", "1"));  // itself
        Assert.Null(book.Problem(null, "Без номер", ""));
    }
}
