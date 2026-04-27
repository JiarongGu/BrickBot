namespace BrickBot.Modules.Script;

public static class ScriptEvents
{
    /// <summary>
    /// Fired when the running script's set of registered <c>brickbot.action()</c> names
    /// changes. Payload: <c>{ actions: string[] }</c>.
    /// </summary>
    public const string ACTIONS_CHANGED = "ACTIONS_CHANGED";
}
