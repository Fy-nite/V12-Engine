using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Marks an element as a hot-reload XML source.
    /// Attach this component to any world element and provide a path to a WorldML
    /// XML file. <c>WorldXmlHotReloader</c> will load the file's elements into the
    /// world and watch for saves, replacing them automatically on every change.
    /// </summary>
    public class WorldXmlSourceComponent : ComponentBase
    {
        private string _filePath   = string.Empty;
        private bool   _autoReload = true;

        public override string Name        => "WorldXmlSource";
        public override string Description => "Hot-reload WorldML XML source file";

        /// <summary>
        /// Absolute path, or a path relative to the executable directory,
        /// of the WorldML XML file to load and watch.
        /// </summary>
        public string FilePath
        {
            get => _filePath;
            set { if (_filePath != value) { _filePath = value ?? string.Empty; MarkDirty(); } }
        }

        /// <summary>
        /// When true (default), a <see cref="FileSystemWatcher"/> will be registered
        /// for the file and its contents will be reloaded automatically on change.
        /// Set to false for a one-shot load with no live watching.
        /// </summary>
        public bool AutoReload
        {
            get => _autoReload;
            set { if (_autoReload != value) { _autoReload = value; MarkDirty(); } }
        }

        public WorldXmlSourceComponent() { }

        public WorldXmlSourceComponent(string filePath, bool autoReload = true)
        {
            _filePath   = filePath ?? string.Empty;
            _autoReload = autoReload;
        }
    }
}
