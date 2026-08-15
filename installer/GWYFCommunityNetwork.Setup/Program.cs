using System;
using System.Linq;
using System.Windows.Forms;

namespace GwyfCommunity.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--selftest"))
            return Selftest.Run();

        if (args.Length == 2 && args[0] == "--selftest-install")
            return Selftest.RunInstallAgainst(args[1]);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
