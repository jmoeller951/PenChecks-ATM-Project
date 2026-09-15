using System.Globalization;

namespace AtmApp.Domain.Exceptions;

/// <summary>
/// Represents an invalid transaction amount (zero or negative). Expected to be wrapped in a
/// Result&lt;T&gt; failure by the Application layer rather than thrown — see AtmService.
/// </summary>
public class InvalidAmountException : Exception
{
    public decimal Amount { get; }

    public InvalidAmountException(decimal amount)
        : base($"Amount must be greater than zero. Received {amount.ToString("C", CultureInfo.GetCultureInfo("en-US"))}.")
    {
        Amount = amount;
    }
}
