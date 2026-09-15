using System.Globalization;

namespace AtmApp.Domain.Exceptions;

/// <summary>
/// Represents an insufficient-funds business rule violation. Expected to be wrapped in a
/// Result&lt;T&gt; failure by the Application layer rather than thrown — see AtmService.
/// </summary>
public class InsufficientFundsException : Exception
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    public Guid AccountId { get; }
    public decimal RequestedAmount { get; }
    public decimal AvailableBalance { get; }

    public InsufficientFundsException(Guid accountId, string accountName, decimal requestedAmount, decimal availableBalance)
        : base($"{accountName} has insufficient funds: requested {requestedAmount.ToString("C", UsCulture)}, available {availableBalance.ToString("C", UsCulture)}.")
    {
        AccountId = accountId;
        RequestedAmount = requestedAmount;
        AvailableBalance = availableBalance;
    }
}
