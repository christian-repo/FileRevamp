using Spectre.Console.Cli;

namespace FileRevamp.Infrastructure;

/// <summary>
/// Minimal Spectre.Console.Cli type registrar for production use.
/// Supports instance registrations (singletons) needed to inject IAnsiConsole.
/// </summary>
internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly Dictionary<Type, List<Type>> _registrations = new();
    private readonly Dictionary<Type, List<object>> _instances = new();

    public void Register(Type service, Type implementation)
    {
        if (!_registrations.TryGetValue(service, out var list))
        {
            list = new List<Type>();
            _registrations[service] = list;
        }
        list.Add(implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        if (!_instances.TryGetValue(service, out var list))
        {
            list = new List<object>();
            _instances[service] = list;
        }
        list.Add(implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        RegisterInstance(service, factory());
    }

    public ITypeResolver Build()
    {
        return new TypeResolver(_registrations, _instances);
    }
}

/// <summary>
/// Resolves types from registrations and singleton instances.
/// </summary>
internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly Dictionary<Type, List<Type>> _registrations;
    private readonly Dictionary<Type, List<object>> _instances;

    public TypeResolver(
        Dictionary<Type, List<Type>> registrations,
        Dictionary<Type, List<object>> instances)
    {
        _registrations = registrations;
        _instances = instances;
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
            return null;

        // Check singleton instances first
        if (_instances.TryGetValue(type, out var instanceList) && instanceList.Count > 0)
            return instanceList[^1];

        // Fall back to registered types — instantiate with parameterless constructor
        if (_registrations.TryGetValue(type, out var typeList) && typeList.Count > 0)
            return Activator.CreateInstance(typeList[^1]);

        // Last resort: try to activate the type directly
        return Activator.CreateInstance(type);
    }

    public void Dispose()
    {
        // Nothing to dispose — instances are owned by the caller
    }
}
