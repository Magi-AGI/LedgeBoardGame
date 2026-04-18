using Magi.LedgeBoardGame.ServerModule;
using MagiGameServer.Host;

namespace Magi.LedgeBoardGame.Host
{
    public class Program
    {
        private Program() { }

        public static void Main(string[] args)
        {
            var app = MagiGameServer.Host.Program.CreateApp(args, registry =>
            {
                registry.Register(new LedgeGameModule());
            });
            app.Run();
        }
    }
}
