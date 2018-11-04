using System;
using System.IO;

namespace DynamicCompanyMorale
{
    public class Logger
    {
        static string filePath = $"{DynamicCompanyMorale.ModDirectory}/DynamicCompanyMorale.log";
        public static void LogError(Exception ex)
        {
            if (DynamicCompanyMorale.DebugLevel >= 1)
            {
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    var prefix = "[DynamicCompanyMorale @ " + DateTime.Now.ToString() + "]";
                    writer.WriteLine("Message: " + ex.Message + "<br/>" + Environment.NewLine + "StackTrace: " + ex.StackTrace + "" + Environment.NewLine);
                    writer.WriteLine("----------------------------------------------------------------------------------------------------" + Environment.NewLine);
                }
            }
        }

        public static void LogLine(String line)
        {
            if (DynamicCompanyMorale.DebugLevel >= 2) { 
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    var prefix = "[DynamicCompanyMorale @ " + DateTime.Now.ToString() + "]";
                    writer.WriteLine(prefix + line);
                }
            }
        }
    }
}
