using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Mosaic.Testing
{
    /// <summary>
    /// Reads the IL of methods and returns the members they use (calls, fields, object creation), including code in
    /// lambdas, closures and iterators. Works without Cecil, so in the Unity Test Runner and in plain dotnet test.
    /// </summary>
    public static class IlScanner
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null))
            .ToDictionary(code => code.Value);

        public static IEnumerable<MethodBase> MethodsOf(Type type)
        {
            foreach (var method in type.GetMethods(All))
                yield return method;
            foreach (var constructor in type.GetConstructors(All))
                yield return constructor;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var method in MethodsOf(nested))
                    yield return method;
        }

        public static IEnumerable<(MethodBase From, MemberInfo Member)> UsagesOf(Type type)
        {
            foreach (var method in MethodsOf(type))
                foreach (var member in MembersUsedBy(method))
                    yield return (method, member);
        }

        public static List<MemberInfo> MembersUsedBy(MethodBase method)
        {
            var result = new List<MemberInfo>();
            var body = method.GetMethodBody();
            if (body == null)
                return result;

            var il = body.GetILAsByteArray();
            var typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

            var i = 0;
            while (i < il.Length)
            {
                short value = il[i++];
                if (value == 0xFE)
                    value = unchecked((short)(0xFE00 | il[i++]));

                if (!Codes.TryGetValue(value, out var code))
                    break;

                switch (code.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    case OperandType.InlineSwitch:
                        i += 4 + 4 * BitConverter.ToInt32(il, i);
                        break;
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineTok:
                        var token = BitConverter.ToInt32(il, i);
                        i += 4;
                        try
                        {
                            var member = method.Module.ResolveMember(token, typeArgs, methodArgs);
                            if (member != null)
                                result.Add(member);
                        }
                        catch (ArgumentException)
                        {
                        }
                        break;
                    default:
                        i += 4;
                        break;
                }
            }

            return result;
        }

        public static string Describe(MethodBase from, MemberInfo member) =>
            $"{OwnerName(from.DeclaringType)}.{from.Name} → {member.DeclaringType?.Name}.{member.Name}";

        public static string OwnerName(Type type)
        {
            while (type.DeclaringType != null)
                type = type.DeclaringType;
            return type.Name;
        }
    }
}
