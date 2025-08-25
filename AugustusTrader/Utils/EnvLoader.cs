using System;
using System.IO;

namespace ObserverBot.Utils
{
    public static class EnvLoader
    {
        public static void LoadEnvFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                        Environment.SetEnvironmentVariable(parts[0], parts[1]);
                }
            }
            catch { }
        }
    }
}
