using System.Reflection;
using RepoRag.Core.Chunking;

namespace RepoRag.Core.Tests;

public class ArchitectureTests
{
    [Fact] // ADR 0003: no Markdig type appears in Core's public API
    public void No_Markdig_types_in_public_api()
    {
        var leaks =
            from type in typeof(IChunker).Assembly.GetExportedTypes()
            from member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            from used in UsedTypes(member)
            where used.Namespace?.StartsWith("Markdig") == true
            select $"{type.Name}.{member.Name} uses {used.FullName}";

        Assert.Empty(leaks);
    }

    private static IEnumerable<Type> UsedTypes(MemberInfo m) => m switch
    {
        MethodInfo mi => mi.GetParameters().Select(p => p.ParameterType).Append(mi.ReturnType),
        PropertyInfo pi => [pi.PropertyType],
        FieldInfo fi => [fi.FieldType],
        ConstructorInfo ci => ci.GetParameters().Select(p => p.ParameterType),
        _ => []
    };
}
