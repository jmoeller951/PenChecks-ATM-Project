using System.Globalization;

namespace AtmApp.Web;

/// <summary>
/// Pins currency display to USD regardless of the server's runtime culture — a minimal-globalization
/// Docker image (invariant culture) would otherwise render "C" as "¤100.00" instead of "$100.00".
/// </summary>
public static class CurrencyFormat
{
    public static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    public static string ToUsd(this decimal value) => value.ToString("C", UsCulture);
}
