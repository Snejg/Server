using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Server
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            
            try
            {
                int portNumber = int.Parse(args[0]);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Server(portNumber));
            }
            catch(IndexOutOfRangeException e)
            {
                Console.WriteLine("Please set port to listen");
                //return;
            }
        }
    }
}
