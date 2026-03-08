using System;
using System.Collections.Generic;
using System.Text;
using ObjectIR.net;
namespace V12.Core.CommandLine
{
    [IRClassBinding("Shell")]
    public interface Shell
    {
        [IRMethodBinding]
        void Run(bool IsAllWorldsClosed);
    }
}
