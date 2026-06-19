using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace V12.Core.Interfaces.Renderer
{
    public interface ISpatial
    {
        Matrix4x4 WorldTransform { get; }
    }
}
