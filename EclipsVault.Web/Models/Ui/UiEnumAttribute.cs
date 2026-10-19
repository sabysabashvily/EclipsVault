namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// Marks a string response property that carries a Core enum value by its name. The property stays
/// a string, so no Core type reaches the wire (ADR-0007), while the OpenAPI document lists the
/// enum's names as the property's closed set, which the generated TypeScript turns into a union.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class UiEnumAttribute(Type enumType) : Attribute
{
    public Type EnumType { get; } = enumType;
}
