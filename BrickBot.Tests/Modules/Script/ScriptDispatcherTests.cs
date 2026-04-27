using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Script;
using BrickBot.Modules.Script.Services;
using FluentAssertions;
using Moq;

namespace BrickBot.Tests.Modules.Script;

public class ScriptDispatcherTests
{
    private readonly Mock<IProfileEventBus> _eventBus = new();

    private ScriptDispatcher Build() => new(_eventBus.Object);

    [Fact]
    public void SetRegisteredActions_EmitsActionsChanged()
    {
        var d = Build();

        d.SetRegisteredActions(new[] { "cast.fireball", "drink.potion" });

        d.GetRegisteredActions().Should().BeEquivalentTo("cast.fireball", "drink.potion");
        _eventBus.Verify(b => b.EmitAsync(
            ModuleNames.SCRIPT,
            ScriptEvents.ACTIONS_CHANGED,
            It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public void EnqueueInvocation_UnknownAction_Throws()
    {
        var d = Build();
        d.SetRegisteredActions(new[] { "known" });

        var act = () => d.EnqueueInvocation("unknown");

        act.Should().Throw<OperationException>()
            .Where(e => e.Code == "RUNNER_ACTION_NOT_FOUND");
    }

    [Fact]
    public void EnqueueInvocation_NoActiveRun_Throws()
    {
        var d = Build();
        // No SetRegisteredActions ever called → registered list is empty.

        var act = () => d.EnqueueInvocation("anything");

        act.Should().Throw<OperationException>()
            .Where(e => e.Code == "RUNNER_ACTION_NOT_FOUND");
    }

    [Fact]
    public void TryDequeueInvocation_ReturnsFifoOrder()
    {
        var d = Build();
        d.SetRegisteredActions(new[] { "a", "b" });
        d.EnqueueInvocation("a");
        d.EnqueueInvocation("b");
        d.EnqueueInvocation("a");

        d.TryDequeueInvocation().Should().Be("a");
        d.TryDequeueInvocation().Should().Be("b");
        d.TryDequeueInvocation().Should().Be("a");
        d.TryDequeueInvocation().Should().BeNull();
    }

    [Fact]
    public void Reset_ClearsActionsAndQueue_AndEmitsEmptyList()
    {
        var d = Build();
        d.SetRegisteredActions(new[] { "a" });
        d.EnqueueInvocation("a");

        d.Reset();

        d.GetRegisteredActions().Should().BeEmpty();
        d.TryDequeueInvocation().Should().BeNull();
        _eventBus.Verify(b => b.EmitAsync(
            ModuleNames.SCRIPT,
            ScriptEvents.ACTIONS_CHANGED,
            It.Is<object>(p => p != null)), Times.Exactly(2)); // initial set + reset
    }

    [Fact]
    public void Reset_WhenAlreadyEmpty_DoesNotEmit()
    {
        var d = Build();

        d.Reset();

        _eventBus.Verify(b => b.EmitAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<object>()), Times.Never);
    }
}
