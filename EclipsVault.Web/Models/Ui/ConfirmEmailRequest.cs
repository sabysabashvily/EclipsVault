using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EclipsVault.Web.Models.Ui;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmEmailRequest(Guid OperationId, [Required, MaxLength(64)] string Code);
