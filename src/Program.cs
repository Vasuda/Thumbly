using System;
using System.Windows.Forms;

namespace Thumbly
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // "--folder <path>": launched from Explorer's folder right-click menu; opens with that folder armed.
            int fi = Array.IndexOf(args, "--folder");
            if (fi >= 0)
                return RunWindow(fi + 1 < args.Length ? args[fi + 1] : "");

            // Headless mode: files dropped onto Thumbly.exe (or passed via "Send to") are converted without a window.
            if (args.Length > 0)
                return RunHeadless(args);

            return RunWindow(null);
        }

        private static int RunWindow(string armedFolder)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { ShellIntegration.RepairIfMoved(Application.ExecutablePath); } catch { } // keep the menu entry pointing at this exe
            Application.Run(new MainForm(armedFolder));
            return 0;
        }

        private static int RunHeadless(string[] paths)
        {
            var settings = Settings.Load();
            int failures = 0;
            foreach (var p in paths)
            {
                try
                {
                    if (!Converter.IsSupported(p)) throw new NotSupportedException("Unsupported file type: " + p);
                    Converter.ConvertFile(p, settings);
                }
                catch (Exception ex)
                {
                    failures++;
                    MessageBox.Show(ex.Message, "Thumbly", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            return failures == 0 ? 0 : 1;
        }
    }
}
