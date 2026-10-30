namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// Marks a string request or response property, or a list of strings, that carries Core enum values
/// by name. The property stays a string, so no Core type reaches the wire (ADR-0007), while the
/// OpenAPI document lists the enum's names as the closed set, which the generated TypeScript turns
/// into a union.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class UiEnumAttribute(Type enumType) : Attribute
{
    public Type EnumType { get; } = enumType;
}
