// Itexture deez nuts
namespace V12.Core.Core.Interfaces
{
    /// <summary>
    /// Engine-agnostic texture reference.
    /// </summary>
    public interface ITexture
    {
        string Name { get; }
        // Unique identifier or file path that the engine uses to locate the asset
        string Source { get; }
    }
}
