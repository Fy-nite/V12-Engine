using System;
using System.Collections.Generic;

namespace V12.Core.Modules
{
    /// <summary>
    /// Small switch parser shared by the module and mod loaders. Accepts both
    /// <c>--switch value</c> and <c>--switch=value</c>, and stops a bare switch
    /// from swallowing the next flag as its value.
    /// </summary>
    public static class ModuleArgs
    {
        public static bool HasFlag(string[] args, string name)
            => Array.IndexOf(args, name) >= 0;

        /// <summary>Every value given for <paramref name="name"/>, in order.</summary>
        public static List<string> Values(string[] args, string name, out bool present)
        {
            present = false;
            var values = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];

                if (string.Equals(argument, name, StringComparison.Ordinal))
                {
                    present = true;

                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        values.Add(args[++i]);

                    continue;
                }

                if (argument.StartsWith(name + "=", StringComparison.Ordinal))
                {
                    present = true;
                    values.Add(argument[(name.Length + 1)..]);
                }
            }

            return values;
        }

        /// <summary>Resolve a possibly relative path against the host's base directory.</summary>
        public static string Resolve(string root, string path)
            => System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(root, path);
    }
}
