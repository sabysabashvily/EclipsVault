namespace EclipsVault.Core.Application.Passkeys;

public enum PasskeyProofOutcome
{
    Verified = 1,

    /// <summary>The response could not be read at all, so it proved nothing and tested nothing.</summary>
    Malformed,

    /// <summary>A readable response that did not prove the account. It counts as a wrong proof.</summary>
    Rejected
}
