using MicroinvestConvert.Core.Settings;

namespace MicroinvestConvert.Data;

/// <summary>The clients in the database, each with its accounts.</summary>
public sealed class ClientStore(Database database) : IClientStore
{
    public IReadOnlyList<ClientSettings> Load() => database.Use(c =>
    {
        var clients = c.Read("SELECT Id, Name, Eik, VatNumber, Note, PurchaseVatAccount, SaleVatAccount, CashAccount FROM Clients ORDER BY Id",
            r => new ClientSettings
            {
                Id = r.GetInt64(0), Name = r.GetString(1), Eik = r.GetString(2), VatNumber = r.GetString(3), Note = r.GetString(4),
                PurchaseVatAccount = r.GetString(5), SaleVatAccount = r.GetString(6), CashAccount = r.GetString(7),
            });

        var byId = clients.ToDictionary(x => x.Id);
        foreach (var (id, account, target, currency) in c.Read("SELECT ClientId, Account, Target, Currency FROM ClientAccounts",
                     r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3))))
        {
            if (!byId.TryGetValue(id, out var client)) continue;
            if (target.Length > 0) client.Accounts[account] = target;
            if (currency.Length > 0) client.AccountCurrencies[account] = currency;
        }

        return clients;
    });

    public void Save(ClientSettings client) => database.Change(c =>
    {
        (string, object)[] values =
        [
            ("$name", client.Name), ("$eik", client.Eik), ("$vat", client.VatNumber), ("$note", client.Note),
            ("$purchase", client.PurchaseVatAccount), ("$sale", client.SaleVatAccount), ("$cash", client.CashAccount),
        ];

        if (client.Id == 0)
            client.Id = c.Read(
                "INSERT INTO Clients (Name, Eik, VatNumber, Note, PurchaseVatAccount, SaleVatAccount, CashAccount, Created) " +
                "VALUES ($name, $eik, $vat, $note, $purchase, $sale, $cash, $now) RETURNING Id",
                r => r.GetInt64(0), [.. values, ("$now", Stored.Now())])[0];
        else
            c.Execute(
                "UPDATE Clients SET Name = $name, Eik = $eik, VatNumber = $vat, Note = $note, PurchaseVatAccount = $purchase, " +
                "SaleVatAccount = $sale, CashAccount = $cash WHERE Id = $id",
                [.. values, ("$id", client.Id)]);

        c.Execute("DELETE FROM ClientAccounts WHERE ClientId = $id", ("$id", client.Id));
        foreach (string account in client.Accounts.Keys.Union(client.AccountCurrencies.Keys, StringComparer.OrdinalIgnoreCase))
            c.Execute("INSERT INTO ClientAccounts (ClientId, Account, Target, Currency) VALUES ($id, $account, $target, $currency)",
                ("$id", client.Id), ("$account", account),
                ("$target", client.Accounts.GetValueOrDefault(account, "")), ("$currency", client.AccountCurrencies.GetValueOrDefault(account, "")));
    });

    public void Delete(ClientSettings client) => database.Change(c => c.Execute("DELETE FROM Clients WHERE Id = $id", ("$id", client.Id)));
}
