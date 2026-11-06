using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mosaic.Core;

namespace Mosaic.Testing
{
    /// <summary>
    /// The role rules of Mosaic checked against compiled code. Every rule returns its violations as readable lines;
    /// an empty list means the rule holds. Independent of the test framework:
    /// <c>Assert.That(rules.RulesDoNotPublishEvents(), Is.Empty)</c>.
    /// </summary>
    public sealed class ArchitectureRules
    {
        private static readonly Type[] WorldMutators = { typeof(World), typeof(Entity) };
        private static readonly string[] WorldMutatorNames = { "Add", "Remove", "Destroy", "Spawn", "Flush" };

        private static readonly Type[] Infrastructure =
        {
            typeof(World), typeof(Entity), typeof(IEventBus), typeof(EventBus), typeof(Simulation),
            typeof(CommandBus), typeof(ICommandBus), typeof(GameplayContext),
        };

        private readonly Assembly[] _assemblies;

        public ArchitectureRules(params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
                throw new ArgumentException("Pass the assemblies of the game domain", nameof(assemblies));

            _assemblies = assemblies.Distinct().ToArray();
        }

        /// <summary>Types that may bypass the roles on purpose, for example cheats.</summary>
        public Func<Type, bool> Exclude { get; set; } = _ => false;

        public Func<Type, bool> IsOperations { get; set; } = type => type.Name.EndsWith("Operations");

        public Func<Type, bool> IsReadOnly { get; set; } =
            type => type.Name.EndsWith("Evaluator") || type.Name.EndsWith("Queries");

        private IEnumerable<Type> Types =>
            _assemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => !Exclude(type));

        private IEnumerable<Type> TopLevel => Types.Where(type => !type.IsNested);

        public IReadOnlyList<string> CheckAll() =>
            Named("Commands and events are readonly structs", CommandsAndEventsAreReadonlyStructs())
                .Concat(Named("Every command can be recorded", CommandsCanBeRecorded()))
                .Concat(Named("Handlers, reactions and systems do not publish events", RulesDoNotPublishEvents()))
                .Concat(Named("Evaluators and Queries only read", ReadersDoNotChangeState()))
                .Concat(Named("Models are changed only by the Operations of their feature", ModelsAreChangedOnlyByOperations()))
                .Concat(Named("Models do not reach the world, buses or other entities", ModelsStayIsolated()))
                .Concat(Named("The domain does not reference Unity", DomainDoesNotReferenceUnity()))
                .ToList();

        /// <summary>
        /// Fails when the scanner sees no Operations that publish events and change models. Without this check a broken
        /// scanner would make every other rule pass silently. Use it once the game has its first Operations.
        /// </summary>
        public IReadOnlyList<string> ScannerSeesOperations()
        {
            var usages = TopLevel.Where(IsOperations).SelectMany(IlScanner.UsagesOf).ToList();
            var problems = new List<string>();
            if (!usages.Any(usage => IsPublish(usage.Member)))
                problems.Add("no Operations publishes an event");
            if (!usages.Any(usage => IsModelMutator(usage.Member)))
                problems.Add("no Operations changes a model");
            return problems;
        }

        public IReadOnlyList<string> CommandsAndEventsAreReadonlyStructs() =>
            Sorted(Types
                .Where(type => !type.IsInterface && (typeof(ICommand).IsAssignableFrom(type) || typeof(IEvent).IsAssignableFrom(type)))
                .Where(type => !type.IsValueType || type.GetCustomAttributes(false).All(a => a.GetType().Name != "IsReadOnlyAttribute"))
                .Select(type => type.FullName));

        public IReadOnlyList<string> CommandsCanBeRecorded() =>
            Sorted(Types
                .Where(type => type.IsValueType && typeof(ICommand).IsAssignableFrom(type))
                .Select(type => ReplayFormat.CanWrite(type, out var problem) ? null : $"{type.FullName}: {problem}")
                .Where(line => line != null));

        public IReadOnlyList<string> RulesDoNotPublishEvents() =>
            Sorted(TopLevel
                .Where(IsRule)
                .SelectMany(IlScanner.UsagesOf)
                .Where(usage => IsPublish(usage.Member))
                .Select(usage => IlScanner.Describe(usage.From, usage.Member)));

        public IReadOnlyList<string> ReadersDoNotChangeState() =>
            Sorted(TopLevel
                .Where(IsReadOnly)
                .SelectMany(IlScanner.UsagesOf)
                .Where(usage => IsPublish(usage.Member) || IsSend(usage.Member) || IsWorldMutation(usage.Member) ||
                                IsModelMutator(usage.Member) ||
                                usage.Member.DeclaringType != null && IsOperations(usage.Member.DeclaringType))
                .Select(usage => IlScanner.Describe(usage.From, usage.Member)));

        public IReadOnlyList<string> ModelsAreChangedOnlyByOperations() =>
            Sorted(TopLevel
                .Where(type => !IsModel(type))
                .SelectMany(type => IlScanner.UsagesOf(type).Select(usage => (Type: type, usage.From, usage.Member)))
                .Where(usage => IsModelMutator(usage.Member) &&
                                !(IsOperations(usage.Type) && usage.Type.Namespace == usage.Member.DeclaringType.Namespace))
                .Select(usage => IlScanner.Describe(usage.From, usage.Member)));

        public IReadOnlyList<string> ModelsStayIsolated()
        {
            var fields = TopLevel
                .Where(IsModel)
                .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(field => Infrastructure.Contains(field.FieldType))
                .Select(field => $"{field.DeclaringType.Name}.{field.Name}: {field.FieldType.Name}");

            var calls = TopLevel
                .Where(IsModel)
                .SelectMany(IlScanner.UsagesOf)
                .Where(usage => Infrastructure.Contains(usage.Member.DeclaringType))
                .Select(usage => IlScanner.Describe(usage.From, usage.Member));

            return Sorted(fields.Concat(calls));
        }

        public IReadOnlyList<string> DomainDoesNotReferenceUnity() =>
            Sorted(_assemblies
                .SelectMany(assembly => assembly.GetReferencedAssemblies()
                    .Where(name => name.Name.StartsWith("UnityEngine") || name.Name.StartsWith("UnityEditor"))
                    .Select(name => $"{assembly.GetName().Name} → {name.Name}")));

        private static bool IsModel(Type type) => type != null && typeof(IModel).IsAssignableFrom(type) && !type.IsInterface;

        private static bool IsRule(Type type) =>
            typeof(CommandHandler).IsAssignableFrom(type) || typeof(Reaction).IsAssignableFrom(type) ||
            typeof(ISystem).IsAssignableFrom(type) && !type.IsInterface;

        private static bool IsPublish(MemberInfo member) =>
            member.Name == nameof(IEventBus.Publish) &&
            (member.DeclaringType == typeof(IEventBus) || member.DeclaringType == typeof(EventBus));

        private static bool IsSend(MemberInfo member) =>
            member.Name == nameof(ICommandBus.Send) &&
            (member.DeclaringType == typeof(ICommandBus) || member.DeclaringType == typeof(CommandBus));

        private static bool IsWorldMutation(MemberInfo member) =>
            WorldMutators.Contains(member.DeclaringType) && WorldMutatorNames.Contains(member.Name);

        private static bool IsModelMutator(MemberInfo member) =>
            member is MethodInfo method && IsModel(method.DeclaringType) &&
            !method.IsPublic && !method.Name.StartsWith("get_");

        private static IReadOnlyList<string> Sorted(IEnumerable<string> lines) =>
            lines.Distinct().OrderBy(line => line, StringComparer.Ordinal).ToList();

        private static IEnumerable<string> Named(string rule, IEnumerable<string> violations) =>
            violations.Select(violation => $"{rule}: {violation}");
    }
}
