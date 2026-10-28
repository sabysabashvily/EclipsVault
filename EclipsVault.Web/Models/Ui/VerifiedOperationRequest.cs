using System.Text.Json.Serialization;

namespace EclipsVault.Web.Models.Ui;

/// <summary>The verified operation a final action commits. The change itself was frozen when it was prepared.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VerifiedOperationRequest(Guid OperationId);
