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
    private readonly Dictionary<Type, Lazy<object>> _lazyInstances = new();

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
        // WR-01: Defer factory evaluation to first Resolve() call so construction happens
        // at first-use, not at registration time. Lazy<T> guarantees at-most-once evaluation.
        _lazyInstances[service] = new Lazy<object>(factory);
    }

    public ITypeResolver Build()
    {
        return new TypeResolver(
            new Dictionary<Type, Type>(_registrations),
            new Dictionary<Type, object>(_instances),
            new Dictionary<Type, Lazy<object>>(_lazyInstances));
    }
}

/// <summary>
/// Resolves types: custom instances first, then registered concrete types, then null.
/// </summary>
internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly Dictionary<Type, Type> _registrations;
    private readonly Dictionary<Type, object> _instances;
    private readonly Dictionary<Type, Lazy<object>> _lazyInstances;

    public TypeResolver(
        Dictionary<Type, Type> registrations,
        Dictionary<Type, object> instances,
        Dictionary<Type, Lazy<object>> lazyInstances)
    {
        _registrations = registrations;
        _instances = instances;
        _lazyInstances = lazyInstances;
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
            return null;

        // Custom singleton instances take highest priority (e.g., IAnsiConsole → AnsiConsole.Console)
        if (_instances.TryGetValue(type, out var instance))
            return instance;

        // Lazy instances evaluated at first Resolve() call (WR-01: deferred construction)
        if (_lazyInstances.TryGetValue(type, out var lazy))
            return lazy.Value;

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
