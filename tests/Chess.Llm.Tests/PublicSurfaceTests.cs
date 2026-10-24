using System.Reflection;
using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

/// <summary>P11: GitHub.Copilot types stay inside Chess.Llm. Checked by reflection over every exported type.</summary>
public class PublicSurfaceTests
{
    private const string SdkAssemblyName = "GitHub.Copilot.SDK";

    [Fact]
    public void NoExportedMemberOfChessLlmMentionsACopilotSdkType()
    {
        var leaks = new List<string>();

        foreach (var type in typeof(ILlmGateway).Assembly.GetExportedTypes())
        {
            if (type.BaseType is { } baseType && IsCopilot(baseType))
            {
                leaks.Add($"{type.FullName} : {baseType.FullName}");
            }

            leaks.AddRange(type.GetInterfaces().Where(IsCopilot).Select(i => $"{type.FullName} : {i.FullName}"));
            leaks.AddRange(PublicMembersMentioningCopilot(type));
        }

        Assert.Empty(leaks);
    }

    [Fact]
    public void TheDetectorDoesFlagCopilotTypes()
    {
        // Guards the test above against passing vacuously.
        Assert.True(IsCopilot(typeof(GitHub.Copilot.CopilotClient)));
        Assert.True(IsCopilot(typeof(List<GitHub.Copilot.SessionConfig>)));
        Assert.False(IsCopilot(typeof(ILlmGateway)));
    }

    private static IEnumerable<string> PublicMembersMentioningCopilot(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags).Where(f => f.IsPublic || f.IsFamily))
        {
            if (IsCopilot(field.FieldType)) yield return $"{type.FullName}.{field.Name} (field)";
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (IsAccessible(property.GetMethod) && IsCopilot(property.PropertyType)) yield return $"{type.FullName}.{property.Name} (property)";
        }

        foreach (var evt in type.GetEvents(flags))
        {
            if (IsCopilot(evt.EventHandlerType!)) yield return $"{type.FullName}.{evt.Name} (event)";
        }

        foreach (var ctor in type.GetConstructors(flags).Where(c => c.IsPublic || c.IsFamily))
        {
            if (ctor.GetParameters().Any(p => IsCopilot(p.ParameterType))) yield return $"{type.FullName}..ctor";
        }

        foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName && (m.IsPublic || m.IsFamily)))
        {
            if (IsCopilot(method.ReturnType) || method.GetParameters().Any(p => IsCopilot(p.ParameterType)))
            {
                yield return $"{type.FullName}.{method.Name} (method)";
            }
        }
    }

    private static bool IsAccessible(MethodInfo? accessor) => accessor is { IsPublic: true } or { IsFamily: true };

    /// <summary>True when the type is, or a generic argument, element or base type of it is, defined in the Copilot SDK.</summary>
    private static bool IsCopilot(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            return IsCopilot(element);
        }

        if (type.IsGenericType && type.GetGenericArguments().Any(IsCopilot))
        {
            return true;
        }

        return type.Assembly.GetName().Name == SdkAssemblyName;
    }
}
