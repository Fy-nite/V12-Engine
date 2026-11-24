using System;
using System.Numerics;

namespace V12.Core
{
    /// <summary>
    /// A double-precision world-space offset. V12 keeps the live simulation and
    /// renderer in a small float bubble (<see cref="World.Origin"/> plus local
    /// transforms); this type carries the true global position outside that
    /// bubble without the precision loss of a float <see cref="Vector3"/>.
    /// </summary>
    public readonly struct WorldOffset : IEquatable<WorldOffset>
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public WorldOffset(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly WorldOffset Zero = new(0d, 0d, 0d);

        public static WorldOffset FromVector3(Vector3 value) => new(value.X, value.Y, value.Z);

        /// <summary>Convert to a float vector. Only safe for small local
        /// offsets; use the double fields for global positions.</summary>
        public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static WorldOffset operator +(WorldOffset a, WorldOffset b)
            => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static WorldOffset operator -(WorldOffset a, WorldOffset b)
            => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static WorldOffset operator +(WorldOffset a, Vector3 b)
            => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static WorldOffset operator -(WorldOffset a, Vector3 b)
            => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static WorldOffset operator -(WorldOffset a)
            => new(-a.X, -a.Y, -a.Z);

        public bool Equals(WorldOffset other)
            => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object? obj) => obj is WorldOffset other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
