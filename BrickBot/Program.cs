using BrickBot.Infrastructure;

namespace BrickBot;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationBootstrapper.Run();
    }
}
