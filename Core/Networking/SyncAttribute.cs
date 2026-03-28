namespace V12.Core.Networking
{
    using System;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class SyncAttribute : Attribute
    {
        public string Path { get; }
        public SyncAttribute(string path = null) { Path = path; }
    }
}

