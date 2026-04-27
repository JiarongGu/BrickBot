using BrickBot.Modules.Capture.Models;
using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Input.Services;
using BrickBot.Modules.Runner.Services;
using BrickBot.Modules.Script.Services;
using BrickBot.Modules.Vision.Services;
using FluentAssertions;
using Moq;

namespace BrickBot.Tests.Modules.Script;

public class JintScriptEngineTests
{
    private readonly Mock<IVisionService> _vision = new();
    private readonly Mock<ITemplateLoader> _templates = new();
    private readonly Mock<IInputService> _input = new();
    private readonly Mock<IRunLog> _log = new();
    private readonly Mock<IScriptHost> _host = new();
    private readonly Mock<IFrameBuffer> _frameBuffer = new();
    private readonly Mock<IScriptDispatcher> _dispatcher = new();

    public JintScriptEngineTests()
    {
        _host.Setup(h => h.Cancellation).Returns(CancellationToken.None);
        _host.Setup(h => h.TemplateRoot).Returns(Path.GetTempPath());
        _host.Setup(h => h.WindowOriginX).Returns(0);
        _host.Setup(h => h.WindowOriginY).Returns(0);
        _host.Setup(h => h.GrabFrame()).Returns(() => null!);
        _frameBuffer.Setup(b => b.Snapshot()).Returns((CaptureFrame?)null);
        _frameBuffer.Setup(b => b.LatestFrameNumber).Returns(0);
        _dispatcher.Setup(d => d.TryDequeueInvocation()).Returns((string?)null);
    }

    private JintScriptEngine BuildEngine() =>
        new(_vision.Object, _templates.Object, _input.Object, _log.Object,
            _frameBuffer.Object, _dispatcher.Object);

    [Fact]
    public void Require_BrickbotModule_ExposesHostGlobals()
    {
        var ctx = new ScriptContext();
        var run = new ScriptRunRequest(
            // CommonJS-style: write a string into ctx so we can assert from C#.
            "var bb = require('brickbot'); __ctx.setJson('hostKind', JSON.stringify(typeof bb.vision));",
            _ => null);

        BuildEngine().Execute(run, _host.Object, ctx);

        ctx.getJson("hostKind").Should().Be("\"object\"");
    }

    [Fact]
    public void Require_Library_ResolvesViaCallbackAndCachesResult()
    {
        var ctx = new ScriptContext();
        var resolveCount = 0;
        var run = new ScriptRunRequest(
            "var u = require('utils'); var v = require('utils'); " +
            "__ctx.setJson('sum', JSON.stringify(u.add(2, 3) + v.add(10, 20)));",
            name =>
            {
                resolveCount++;
                return name == "utils"
                    ? new ScriptFile("utils", "module.exports = { add: function(a, b) { return a + b; } };")
                    : null;
            });

        BuildEngine().Execute(run, _host.Object, ctx);

        ctx.getJson("sum").Should().Be("35");
        resolveCount.Should().Be(1, "the engine caches modules so require() of the same id only resolves once");
    }

    [Fact]
    public void Require_NormalizesPathsToBareLibraryNames()
    {
        var ctx = new ScriptContext();
        var resolved = new List<string>();
        var run = new ScriptRunRequest(
            "require('./lib/foo'); require('./foo'); require('foo');",
            name => { resolved.Add(name); return new ScriptFile(name, "module.exports = {};"); });

        BuildEngine().Execute(run, _host.Object, ctx);

        resolved.Should().AllBeEquivalentTo("foo",
            "all three require() forms should normalize to the bare library name");
    }

    [Fact]
    public void Require_UnknownModule_ThrowsScriptModuleNotFound()
    {
        var ctx = new ScriptContext();
        var run = new ScriptRunRequest(
            "require('does-not-exist');",
            _ => null);

        var act = () => BuildEngine().Execute(run, _host.Object, ctx);

        act.Should().Throw<OperationException>()
            .Where(e => e.Code == "SCRIPT_MODULE_NOT_FOUND");
    }

    [Fact]
    public void Brickbot_RegisterActionPushesToDispatcher()
    {
        var ctx = new ScriptContext();
        var run = new ScriptRunRequest(
            "brickbot.action('cast.fireball', function () {});\n" +
            "brickbot.action('drink.potion', function () {});",
            _ => null);

        BuildEngine().Execute(run, _host.Object, ctx);

        _dispatcher.Verify(d => d.SetRegisteredActions(
            It.Is<string[]>(a => a.Length == 1 && a[0] == "cast.fireball")), Times.Once);
        _dispatcher.Verify(d => d.SetRegisteredActions(
            It.Is<string[]>(a => a.Length == 2 && a.Contains("cast.fireball") && a.Contains("drink.potion"))), Times.Once);
    }

    [Fact]
    public void Brickbot_OnEmit_DispatchesPayload()
    {
        var ctx = new ScriptContext();
        var run = new ScriptRunRequest(
            "var hits = [];\n" +
            "brickbot.on('lowHp', function (p) { hits.push(p.value); });\n" +
            "brickbot.emit('lowHp', { value: 30 });\n" +
            "brickbot.emit('lowHp', { value: 12 });\n" +
            "__ctx.setJson('hits', JSON.stringify(hits));",
            _ => null);

        BuildEngine().Execute(run, _host.Object, ctx);

        ctx.getJson("hits").Should().Be("[30,12]");
    }

    [Fact]
    public void Brickbot_OffRemovesHandler()
    {
        var ctx = new ScriptContext();
        var run = new ScriptRunRequest(
            "var count = 0;\n" +
            "var fn = function () { count++; };\n" +
            "brickbot.on('ping', fn);\n" +
            "brickbot.emit('ping');\n" +
            "brickbot.off('ping', fn);\n" +
            "brickbot.emit('ping');\n" +
            "__ctx.setJson('count', JSON.stringify(count));",
            _ => null);

        BuildEngine().Execute(run, _host.Object, ctx);

        ctx.getJson("count").Should().Be("1");
    }

    [Fact]
    public void Main_AsCommonJsModule_CanUseImportsAndExports()
    {
        var ctx = new ScriptContext();
        // Mimics the TS compiler's CommonJS emit: require + exports usage in main.
        var run = new ScriptRunRequest(
            "Object.defineProperty(exports, '__esModule', { value: true });\n" +
            "var lib = require('greet');\n" +
            "exports.message = lib.hello('BrickBot');\n" +
            "__ctx.setJson('message', JSON.stringify(exports.message));",
            name => name == "greet"
                ? new ScriptFile("greet", "module.exports = { hello: function(n) { return 'hi ' + n; } };")
                : null);

        BuildEngine().Execute(run, _host.Object, ctx);

        ctx.getJson("message").Should().Be("\"hi BrickBot\"");
    }
}
