using System;
using System.IO;
using System.Windows;
using Chillistica_game.App.Services;

namespace Chillistica_game.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Headless verification mode used by the trusted elevated updater
        // (apply-update.ps1): validate a downloaded package's detached signature
        // against the pinned public key, in this admin-only .NET 8 binary, and
        // return the result as the process exit code (0 = valid).
        if (e.Args.Length >= 3 &&
            string.Equals(e.Args[0], "--verify-update", StringComparison.OrdinalIgnoreCase))
        {
            int exitCode = 1;

            try
            {
                byte[] signatureBytes =
                    File.ReadAllBytes(e.Args[2]);

                if (UpdateSignatureVerifier.VerifyFile(e.Args[1], signatureBytes))
                {
                    exitCode = 0;
                }
            }
            catch
            {
                exitCode = 1;
            }

            Shutdown(exitCode);
            return;
        }

        base.OnStartup(e);

        new MainWindow().Show();
    }
}
