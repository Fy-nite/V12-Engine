using System.Collections.Generic;
using System.Xml;

namespace V12.Core.Interfaces
{
    public interface ITemplateProvider
    {
        XmlDocument GetTemplate(string name);
        IEnumerable<string> TemplateNames { get; }
    }
}
