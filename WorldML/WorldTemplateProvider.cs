using System.Collections.Generic;
using System.IO;
using System.Xml;
using V12.Core.Interfaces;

namespace V12.WorldML
{
    public class WorldTemplateProvider : ITemplateProvider
    {
        private readonly Dictionary<string, XmlDocument> _templates = new();
        private readonly string _templatesDir;

        public WorldTemplateProvider(string templatesDir = null)
        {
            _templatesDir = templatesDir;
        }

        public IEnumerable<string> TemplateNames => _templates.Keys;

        public void AddInline(XmlNode templateNode)
        {
            var name = templateNode.Attributes?["name"]?.Value;
            if (string.IsNullOrEmpty(name)) return;

            var doc = new XmlDocument();
            var import = doc.ImportNode(templateNode, true);
            doc.AppendChild(import);
            _templates[name] = doc;
        }

        public void LoadFromDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.xml"))
            {
                var doc = new XmlDocument();
                doc.Load(file);
                var root = doc.DocumentElement;
                var name = root?.Attributes?["name"]?.Value ?? Path.GetFileNameWithoutExtension(file);
                _templates[name] = doc;
            }
        }

        public XmlDocument GetTemplate(string name)
        {
            _templates.TryGetValue(name, out var doc);
            return doc;
        }
    }
}
