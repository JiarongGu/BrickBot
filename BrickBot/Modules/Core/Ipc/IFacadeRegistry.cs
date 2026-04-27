namespace BrickBot.Modules.Core.Ipc;

public interface IFacadeRegistry
{
    BaseFacade Get(string moduleName);
    void Register(string moduleName, BaseFacade facade);
}

public sealed class FacadeRegistry : IFacadeRegistry
{
    private readonly Dictionary<string, BaseFacade> _facades = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string moduleName, BaseFacade facade)
    {
        _facades[moduleName] = facade;
    }

    public BaseFacade Get(string moduleName)
    {
        if (!_facades.TryGetValue(moduleName, out var facade))
        {
            throw new InvalidOperationException($"No facade registered for module '{moduleName}'");
        }
        return facade;
    }
}
