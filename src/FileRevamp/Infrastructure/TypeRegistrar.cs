using Spectre.Console.Cli;

namespace FileRevamp.Infrastructure;

/// <summary>
/// Minimal Spectre.Console.Cli type registrar for production use.
/// Wraps Spectre's internal registrations and overlays custom singleton registrations.
///
/// Design: Spectre calls Register() for its internal types (IHelpProvider, etc.).
/// We store those registrations. When Resolve() is called, we first check our custom
/// instances, then fall back to Spectre's registered types (created via Activator).
/// Returning null for unknown/unresolvable types lets Spectre use its own fallback.
/// </summary>
internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly Dictionary<Type, Type> _registrations = new();
    private readonly Dictionary<Type, object> _instances = new();

    public void Register(Type service, Type implementation)
    {
        _registrations[service] = implementation;
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _instances[service] = implementation;
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _instances[service] = factory();
    }

    public ITypeResolver Build()
    {
        return new TypeResolver(
            new Dictionary<Type, Type>(_registrations),
            new Dictionary<Type, object>(_instances));
    }
}

/// <summary>
/// Resolves types: custom instances first, then registered concrete types, then null.
/// </summary>
internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly Dictionary<Type, Type> _registrations;
    private readonly Dictionary<Type, object> _instances;

    public TypeResolver(Dictionary<Type, Type> registrations, Dictionary<Type, object> instances)
    {
        _registrations = registrations;
        _instances = instances;
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
            return null;

        // Custom singleton instances take highest priority (e.g., IAnsiConsole → AnsiConsole.Console)
        if (_instances.TryGetValue(type, out var instance))
            return instance;

        // Spectre-registered concrete types (e.g., IEnumerable<IHelpProvider> → List<IHelpProvider>)
        if (_registrations.TryGetValue(type, out var implType))
        {
            try { return Activator.CreateInstance(implType); }
            catch { return null; }
        }

        // Unknown types: return null and let Spectre's fallback handle it.
        // Do NOT call Activator.CreateInstance for unknown types — interfaces and abstract
        // types will throw, causing "Could not resolve type" errors.
        return null;
    }

    public void Dispose() { }
}
