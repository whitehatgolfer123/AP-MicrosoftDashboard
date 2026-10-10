namespace MsAds.Auditor.Core.Models;

/// <summary>
/// An advertising account the authenticated user can reach. Note that an advertising account id
/// and a manager (customer) id are different identifiers and are not interchangeable — both are
/// carried here because reporting calls need the pair.
/// </summary>
public sealed record AdAccountInfo
{
    /// <summary>The advertising account id, as used in reporting calls.</summary>
    public required long AccountId { get; init; }

    /// <summary>The human-facing account number, e.g. "X1234567".</summary>
    public string? AccountNumber { get; init; }

    public required string AccountName { get; init; }

    /// <summary>The manager (customer) id that owns the account.</summary>
    public required long CustomerId { get; init; }

    public string? CustomerName { get; init; }

    /// <summary>ISO currency code the account reports in.</summary>
    public string? CurrencyCode { get; init; }

    /// <summary>The account's reporting time zone, as reported by the API.</summary>
    public string? TimeZone { get; init; }

    public string? Status { get; init; }

    public string DisplayLabel => AccountNumber is null
        ? $"{AccountName} ({AccountId})"
        : $"{AccountName} ({AccountNumber})";
}

public enum AccountAccessFailure
{
    /// <summary>The supplied text was not a valid account id.</summary>
    InvalidFormat,

    /// <summary>No account with that id was found among those the user can reach.</summary>
    NotFound,

    /// <summary>The account exists but the authenticated user has no permission on it.</summary>
    NoPermission,

    NotAuthenticated,

    ServiceError,
}

/// <summary>The outcome of checking that the user may audit a given account.</summary>
public sealed record AccountAccessResult
{
    public required bool IsAccessible { get; init; }

    public AdAccountInfo? Account { get; init; }

    public AccountAccessFailure? Failure { get; init; }

    /// <summary>A message suitable for showing to the user.</summary>
    public string? Message { get; init; }

    public static AccountAccessResult Granted(AdAccountInfo account)
        => new() { IsAccessible = true, Account = account };

    public static AccountAccessResult Denied(AccountAccessFailure failure, string message)
        => new() { IsAccessible = false, Failure = failure, Message = message };
}
