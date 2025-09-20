using System;
using System.Windows.Forms;
using KeyboardToController;

namespace KtC
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MapperForm()); // Start our custom form
        }
    }
}
