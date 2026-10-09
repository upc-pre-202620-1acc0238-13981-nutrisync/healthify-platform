using System.Reflection;
using System.Reflection.Emit;

namespace Healthify.Platform.Tests.Ai;

/// <summary>
///     IA-0 architecture rule: Shared, and the AI module inside it, references no bounded context (nor the ReadModels
///     composition layer). Checked by reflection on every type under <c>Healthify.Platform.Shared</c>, compiler-made
///     ones included (async state machines, lambdas): base types, interfaces, fields, properties, signatures,
///     attributes and every type, method or field the IL of each method body touches.
/// </summary>
public class SharedKernelIndependenceTests
{
    private const string SharedNamespace = "Healthify.Platform.Shared";

    private static readonly string[] ForbiddenNamespaces =
    [
        "Healthify.Platform.Iam", "Healthify.Platform.CareRelationship", "Healthify.Platform.NutritionalCare",
        "Healthify.Platform.IntakeBodyResponse", "Healthify.Platform.MonitoringAdherence",
        "Healthify.Platform.FoodCatalog", "Healthify.Platform.ReadModels"
    ];

    private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                            BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    [Fact]
    public void Shared_references_no_namespace_of_the_six_contexts()
    {
        var sharedTypes = SharedTypes().ToList();
        Assert.Contains(sharedTypes, t => t.Name == "AiGenerationPipeline"); // the scan really sees the AI module

        var violations = sharedTypes
            .SelectMany(type => ReferencedTypes(type).Select(referenced => (type, referenced)))
            .Where(p => IsForbidden(p.referenced))
            .Select(p => $"{p.type.FullName} -> {p.referenced.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void The_scan_detects_a_reference_hidden_in_a_method_body()
    {
        // A context type that appears only inside the IL of a method, never in a signature.
        var referenced = ReferencedTypes(typeof(BodyOnlyReference)).ToList();

        Assert.Contains(referenced, IsForbidden);
    }

    private static IEnumerable<Type> SharedTypes()
    {
        return typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace is not null &&
                        (t.Namespace == SharedNamespace || t.Namespace.StartsWith(SharedNamespace + ".")));
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        var found = new List<Type>();
        if (type.BaseType is not null) found.Add(type.BaseType);
        found.AddRange(type.GetInterfaces());
        found.AddRange(type.GetCustomAttributesData().Select(a => a.AttributeType));
        found.AddRange(type.GetFields(Everything).Select(f => f.FieldType));
        found.AddRange(type.GetProperties(Everything).Select(p => p.PropertyType));

        foreach (var method in type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything)))
        {
            if (method is MethodInfo info) found.Add(info.ReturnType);
            found.AddRange(method.GetParameters().Select(p => p.ParameterType));
            found.AddRange(method.GetCustomAttributesData().Select(a => a.AttributeType));
            found.AddRange(BodyReferences(method));
        }

        return found.SelectMany(Expand);
    }

    /// <summary>Types touched by the IL of a method: every inline type, method, field or token operand.</summary>
    private static IEnumerable<Type> BodyReferences(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null) yield break;
        foreach (var local in body.LocalVariables) yield return local.LocalType;

        var il = body.GetILAsByteArray() ?? [];
        var module = method.Module;
        var typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (var i = 0; i < il.Length;)
        {
            OpCode code;
            if (il[i] == 0xFE)
            {
                code = OpCodesByValue[(short)(0xFE00 | il[i + 1])];
                i += 2;
            }
            else
            {
                code = OpCodesByValue[il[i]];
                i += 1;
            }

            switch (code.OperandType)
            {
                case OperandType.InlineType:
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineTok:
                    var token = BitConverter.ToInt32(il, i);
                    MemberInfo? member = null;
                    try
                    {
                        member = module.ResolveMember(token, typeArgs, methodArgs);
                    }
                    catch (ArgumentException)
                    {
                    }

                    switch (member)
                    {
                        case Type t:
                            yield return t;
                            break;
                        case MethodBase m:
                            if (m.DeclaringType is not null) yield return m.DeclaringType;
                            if (m is MethodInfo mi) yield return mi.ReturnType;
                            foreach (var p in m.GetParameters()) yield return p.ParameterType;
                            if (m.IsGenericMethod)
                                foreach (var g in m.GetGenericArguments()) yield return g;
                            break;
                        case FieldInfo f:
                            if (f.DeclaringType is not null) yield return f.DeclaringType;
                            yield return f.FieldType;
                            break;
                    }

                    i += 4;
                    break;
                case OperandType.InlineSwitch:
                    i += 4 + 4 * BitConverter.ToInt32(il, i);
                    break;
                default:
                    i += OperandSize(code.OperandType);
                    break;
            }
        }
    }

    private static int OperandSize(OperandType operand)
    {
        return operand switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            _ => 4
        };
    }

    /// <summary>A type, its element type and its generic arguments, recursively.</summary>
    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
            foreach (var t in Expand(element))
                yield return t;
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            foreach (var t in type.GetGenericArguments().SelectMany(Expand))
                yield return t;
    }

    private static bool IsForbidden(Type type)
    {
        var ns = type.Namespace;
        return ns is not null && ForbiddenNamespaces.Any(f => ns == f || ns.StartsWith(f + "."));
    }

    private static class BodyOnlyReference
    {
        // ReSharper disable once UnusedMember.Local
        public static string Touch()
        {
            return typeof(Healthify.Platform.CareRelationship.Domain.Model.Errors.CareRelationshipError).Name;
        }
    }
}
