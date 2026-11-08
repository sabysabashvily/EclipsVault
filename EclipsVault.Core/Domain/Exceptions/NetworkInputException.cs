namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a range or label an administrator entered breaks the trusted-network input rules.
/// <see cref="Field"/> names the request property that holds the value. Nothing was changed.
/// </summary>
public sealed class NetworkInputException : VaultAdminException
{
    public NetworkInputException(string field, string message) : base(message)
    {
        Field = field;
    }

    public string Field { get; }
}
