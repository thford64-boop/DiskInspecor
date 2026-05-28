// ============================================================
// DiskInspector – Advanced Read-Only Disk & Filesystem Analysis
// ============================================================

using System;
using System.Windows.Forms;
using DiskInspector.UI;

namespace DiskInspector
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            Application.Run(new MainForm());
        }
    }
}
