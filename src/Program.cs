using System;
using System.Windows.Forms;

namespace Thumbly
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Headless mode: files dropped onto Thumbly.exe (or passed via "Send to") are converted without a window.
            if (args.Length > 0)
                return RunHeadless(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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
