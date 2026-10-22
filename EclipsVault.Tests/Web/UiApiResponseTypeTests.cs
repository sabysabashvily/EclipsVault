using System.Reflection;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Web.Controllers.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>
/// The UI API answers with response types the web layer defines, never with a Core type as it is
/// (ADR-0007). A field added to a Core type must not silently appear on the wire, because in a vault
/// that is how something sensitive leaks. Every UI API action therefore declares its response type,
/// which is also what the OpenAPI document and the Angular UI's generated types are built from.
/// </summary>
public class UiApiResponseTypeTests
{
    private static readonly Assembly Web = typeof(MeController).Assembly;
    private static readonly Assembly Core = typeof(User).Assembly;

    /// <summary>
    /// Actions that answer with a file, declared as a <see cref="Stream"/>, instead of a response type.
    /// The personal data export keeps its own versioned format instead. Its fields are pinned by
    /// <c>PersonalDataExportServiceTests</c>, every nested item included, and the served file by <c>DataApiTests</c>.
    /// </summary>
    private static readonly HashSet<string> FileDownloads = ["DataController.Export"];

    [Fact]
    public void EveryUiApiAction_DeclaresAResponseTypeTheWebLayerDefines()
    {
        var actions = UiApiActions().ToList();
        Assert.NotEmpty(actions);

        var undeclared = actions
            .Where(action => ResponseTypes(action).Count == 0)
            .Select(Name)
            .ToList();
        Assert.True(undeclared.Count == 0,
            "These UI API actions declare no response type; return ActionResult<T> or add [ProducesResponseType]:\n  " +
            string.Join("\n  ", undeclared));

        var notWeb = actions
            .SelectMany(action => ResponseTypes(action)
                .Where(type => type.Assembly != Web && !(type == typeof(Stream) && FileDownloads.Contains(Name(action))))
                .Select(type => $"{Name(action)}: {type}"))
            .ToList();
        Assert.True(notWeb.Count == 0,
            "These UI API actions answer with a type the web layer does not define:\n  " + string.Join("\n  ", notWeb));
    }

    [Fact]
    public void NoUiApiResponseType_CarriesACoreType()
    {
        var leaks = UiApiActions()
            .SelectMany(ResponseTypes)
            .Distinct()
            .SelectMany(type => CoreTypesReachableFrom(type, []).Select(core => $"{type.Name} carries {core}"))
            .ToList();

        Assert.True(leaks.Count == 0,
            "These UI API response types serialize a Core type as it is; map it to a web-layer type:\n  " +
            string.Join("\n  ", leaks));
    }

    private static IEnumerable<MethodInfo> UiApiActions()
        => Web.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => RouteTemplates(type).Any(template => template.TrimStart('/').StartsWith("api/ui/", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(NonActionAttribute), inherit: true));

    private static IEnumerable<string> RouteTemplates(Type controller)
        => controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(route => route.Template)
            .Concat(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes(inherit: true).OfType<IRouteTemplateProvider>())
                .Select(route => route.Template))
            .OfType<string>();

    /// <summary>The <c>T</c> of an <c>ActionResult&lt;T&gt;</c> return, awaited or not, plus every <c>[ProducesResponseType]</c> type.</summary>
    private static List<Type> ResponseTypes(MethodInfo action)
    {
        var returned = action.ReturnType;
        if (returned.IsGenericType && (returned.GetGenericTypeDefinition() == typeof(Task<>) || returned.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            returned = returned.GetGenericArguments()[0];
        }

        var declared = action.GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true)
            .Select(attribute => attribute.Type)
            .Where(type => type != typeof(void));

        return returned.IsGenericType && returned.GetGenericTypeDefinition() == typeof(ActionResult<>)
            ? declared.Prepend(returned.GetGenericArguments()[0]).ToList()
            : declared.ToList();
    }

    /// <summary>Core types among the public properties of <paramref name="type"/>, its element and generic argument types, recursively.</summary>
    private static IEnumerable<Type> CoreTypesReachableFrom(Type type, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            return [];
        }

        if (type.Assembly == Core)
        {
            return [type];
        }

        var nested = type.IsArray ? [type.GetElementType()!] : type.GetGenericArguments();
        if (type.Assembly == Web)
        {
            nested = [.. nested, .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.PropertyType)];
        }

        return nested.SelectMany(inner => CoreTypesReachableFrom(inner, seen)).ToList();
    }

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";
}
