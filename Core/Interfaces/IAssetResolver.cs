using System.IO;

namespace V12.Core.Interfaces
{
    public interface IAssetResolver
    {
        string Resolve(string uri);
        Stream Open(string uri);
        void Mount(string mountPoint, string physicalPath);
        void Unmount(string mountPoint);
    }
}
