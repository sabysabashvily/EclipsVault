using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

public sealed record StepUpRequest([MaxLength(64)] string? Code);
