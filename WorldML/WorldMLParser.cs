using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Components;
using V12.Core.Interfaces;

namespace V12.WorldML
{
    public class WorldMLParser
    {
        private static readonly Regex _interpRegex = new(@"\$\{(\w+)\}", RegexOptions.Compiled);

        /// <summary>Optional template provider for <c>template="name"</c> and <c>&lt;Instance&gt;</c> support.</summary>
        public ITemplateProvider TemplateProvider { get; set; }

        /// <summary>
        /// Parse a world description from an XML string.
        /// </summary>
        private IWorldElement _parseTask;
        public IWorldElement Parse(string xml)
        {
        
                if (string.IsNullOrWhiteSpace(xml))
                {
                    throw new ArgumentException("XML content is empty or whitespace. Ensure the resource or file was loaded correctly.");
                }

                var doc = new XmlDocument();
                try
                {
                    doc.LoadXml(xml);
                }
                catch (XmlException)
                {
                    // The input might be a fragment (multiple root elements, no <World> wrapper).
                    // Try wrapping it and parsing again before giving up.
                    try
                    {
                        doc.LoadXml($"<World>{xml}</World>");
                    }
                    catch (XmlException ex2)
                    {
                        var preview = xml.Length > 200 ? xml.Substring(0, 200) + "..." : xml;
                        throw new XmlException($"Failed to parse XML input. Preview: {preview}", ex2);
                    }
                }

                var root = doc.DocumentElement;
                Console.WriteLine("Loaded XML document with root: " + root?.Name);
                //Console.WriteLine("XML Contenets:" + Environment.NewLine + doc.OuterXml);
                if (root == null) throw new Exception("Invalid XML: No root element found.");
                var worldName = root.Attributes?["name"]?.Value ?? "World";
                var world = new Element(worldName);

                // Collect inline <Templates> before parsing elements
                if (TemplateProvider is WorldTemplateProvider wtp)
                {
                    foreach (XmlNode child in root.ChildNodes)
                    {
                        if (child.NodeType != XmlNodeType.Element) continue;
                        if (string.Equals(child.Name, "Templates", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (XmlNode tmpl in child.ChildNodes)
                            {
                                if (tmpl.NodeType != XmlNodeType.Element) continue;
                                if (string.Equals(tmpl.Name, "Template", StringComparison.OrdinalIgnoreCase))
                                    wtp.AddInline(tmpl);
                            }
                        }
                    }
                }

                // Parse root child elements
                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element) continue;
                    if (string.Equals(child.Name, "Templates", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var elem = ParseElement(child, world, null);
                }

                return world;


            }

        /// <summary>
        /// Load and parse XML file from disk.
        /// </summary>
        public IWorldElement ParseFile(string path)
        {
            var xml = System.IO.File.ReadAllText(path);
            return Parse(xml);
        }

        private class DeferredPropertySet
        {
            public object Target { get; set; }
            public PropertyInfo Property { get; set; }
            public string ComponentName { get; set; }
        }

        /// <summary>
        /// Parse a single XML node into an Element and attach components found inside.
        /// Returned element is added to the provided world.Root list.
        /// </summary>
        private IWorldElement ParseElement(XmlNode node, Element world, IWorldElement? parent)
        {
            var name = node.Attributes?["name"]?.Value;
            var description = node.Attributes?["description"]?.Value;

            var element = new Element(name, description, parent);

            // Attach to the real parent so element nesting is preserved. Root-level nodes
            // (parent == null) attach to the world container element. Previously every node
            // was added to the container, which flattened the hierarchy.
            if (parent is Element parentElement)
                parentElement.AddChild(element);
            else
                world.AddChild(element);

            // Collect template overrides (everything except name/description/template)
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (string.Equals(attr.Name, "name", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(attr.Name, "description", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(attr.Name, "template", StringComparison.OrdinalIgnoreCase))
                        continue;
                    overrides[attr.Name] = attr.Value;
                }
            }

            // If this element has a template, expand it first
            bool hasTemplateAttr = false;
            string templateName = node.Attributes?["template"]?.Value;
            if (!string.IsNullOrEmpty(templateName) && TemplateProvider != null)
            {
                hasTemplateAttr = true;
                ExpandTemplate(templateName, overrides, element, world);
            }

            var deferredList = new List<DeferredPropertySet>();

            // Process component child nodes and nested elements
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;

                var childName = child.Name;

                // <Instance template="name" /> — inline template expansion
                if (string.Equals(childName, "Instance", StringComparison.OrdinalIgnoreCase))
                {
                    string instTmpl = child.Attributes?["template"]?.Value;
                    if (!string.IsNullOrEmpty(instTmpl) && TemplateProvider != null)
                    {
                        var instOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (child.Attributes != null)
                        {
                            foreach (XmlAttribute attr in child.Attributes)
                            {
                                if (string.Equals(attr.Name, "template", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                instOverrides[attr.Name] = attr.Value;
                            }
                        }
                        ExpandTemplate(instTmpl, instOverrides, element, world);
                    }
                    continue;
                }

                if (string.Equals(childName, "Component", StringComparison.OrdinalIgnoreCase)
                    || childName.EndsWith("Component", StringComparison.OrdinalIgnoreCase))
                {
                    var comp = CreateComponentFromNode(child, deferredList);
                    if (comp != null)
                    {
                        element.AddComponent(comp);
                    }
                    continue;
                }

                // Otherwise treat as nested element
                ParseElement(child, world, element);
            }

            // Resolve deferred properties for this element
            foreach (var ds in deferredList)
            {
                var refComp = element.Components.FirstOrDefault(c => string.Equals(c.Name, ds.ComponentName, StringComparison.OrdinalIgnoreCase));
                if (refComp != null && ds.Property.PropertyType.IsAssignableFrom(refComp.GetType()))
                {
                    ds.Property.SetValue(ds.Target, refComp);
                }
                else if (refComp == null)
                {
                    Console.WriteLine($"[WorldMLParser] WARNING: Could not find component named '{ds.ComponentName}' on element '{element.Name}' to satisfy property '{ds.Property.Name}'");
                }
            }

            return element;
        }

        private void ExpandTemplate(string templateName, Dictionary<string, string> overrides, Element parent, Element world)
        {
            var tmplDoc = TemplateProvider.GetTemplate(templateName);
            if (tmplDoc == null)
            {
                Console.WriteLine($"[WorldMLParser] WARNING: Template '{templateName}' not found.");
                return;
            }

            var tmplRoot = tmplDoc.DocumentElement;
            if (tmplRoot == null) return;

            foreach (XmlNode tmplChild in tmplRoot.ChildNodes)
            {
                if (tmplChild.NodeType != XmlNodeType.Element) continue;
                var clone = tmplChild.CloneNode(true);
                ApplyInterpolation(clone, overrides);
                ParseElement(clone, world, parent);
            }
        }

        private static void ApplyInterpolation(XmlNode node, Dictionary<string, string> overrides)
        {
            if (node.Attributes != null)
            {
                var attrsToUpdate = new List<(XmlAttribute, string)>();
                foreach (XmlAttribute attr in node.Attributes)
                {
                    var result = InterpolateString(attr.Value, overrides);
                    if (result != attr.Value)
                        attrsToUpdate.Add((attr, result));
                }
                foreach (var (attr, newVal) in attrsToUpdate)
                    attr.Value = newVal;
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                    ApplyInterpolation(child, overrides);
                else if (child.NodeType == XmlNodeType.Text)
                {
                    var result = InterpolateString(child.Value, overrides);
                    if (result != child.Value)
                        child.Value = result;
                }
            }
        }

        private static string InterpolateString(string input, Dictionary<string, string> overrides)
        {
            if (string.IsNullOrEmpty(input) || !input.Contains("${"))
                return input;

            return _interpRegex.Replace(input, match =>
            {
                var name = match.Groups[1].Value;
                return overrides.TryGetValue(name, out var val) ? val : "0";
            });
        }

        /// <summary>
        /// Attempt to construct an IComponent instance from an XML node.
        /// Expected attributes: type (simple or full type name). Child nodes named Property can set properties.
        /// </summary>
        private IComponent? CreateComponentFromNode(XmlNode node, List<DeferredPropertySet> deferredList)
        {
            var typeName = node.Attributes?["type"]?.Value ?? node.Attributes?["typename"]?.Value ?? node.Name;
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            // Try to find a type that implements IComponent
            var compType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t => typeof(IComponent).IsAssignableFrom(t) &&
                    (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)));

            if (compType == null) return null;

            object? instance = null;
            try { instance = Activator.CreateInstance(compType); } catch { return null; }

            // Activator returns object; verify it really implements IComponent before proceeding.
            if (instance is not IComponent component) return null;

            // Set simple properties from child <Property name="X" value="Y"/> or direct attributes on the component node
            // 1) attributes
            if (node.Attributes != null)
            {
                // Explicitly check for 'name' attribute to set component instance name
                var nameAttr = node.Attributes["name"];
                if (nameAttr != null && component is ComponentBase cb)
                {
                    cb.Name = nameAttr.Value;
                }

                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (attr.Name == "type" || attr.Name == "typename" || attr.Name == "name") continue;
                    SetPropertyIfExists(instance, attr.Name, attr.Value, deferredList);
                }
            }

            // 2) child Property nodes
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (!string.Equals(child.Name, "Property", StringComparison.OrdinalIgnoreCase)) continue;

                var propName = child.Attributes?["name"]?.Value;
                var propValue = child.Attributes?["value"]?.Value ?? child.InnerText;
                if (string.IsNullOrEmpty(propName)) continue;
                SetPropertyIfExists(instance, propName, propValue, deferredList);
            }

            // 3) If the component has an SvgContent string property, capture
            //    the raw inner XML of this node (e.g. embedded <svg> element).
            if (component is V12.Components.SvgComponent svgComp && node.HasChildNodes)
            {
                var inner = node.InnerXml?.Trim();
                if (!string.IsNullOrEmpty(inner))
                    svgComp.SvgContent = inner;
            }

            // 4) For MeshComponent (Shape=Custom), collect <vert> and <tri> child elements
            if (component is V12.Components.MeshComponent meshComp && node.HasChildNodes)
            {
                var verts = new List<double>();
                var tris = new List<uint>();
                uint vertIndex = 0;
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element) continue;
                    if (string.Equals(child.Name, "vert", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(child.Name, "v", StringComparison.OrdinalIgnoreCase))
                    {
                        var x = TryParseDouble(child.Attributes?["x"]?.Value);
                        var y = TryParseDouble(child.Attributes?["y"]?.Value);
                        var z = TryParseDouble(child.Attributes?["z"]?.Value);
                        if (x.HasValue && y.HasValue && z.HasValue)
                        {
                            verts.Add(x.Value);
                            verts.Add(y.Value);
                            verts.Add(z.Value);
                            vertIndex++;
                        }
                    }
                    else if (string.Equals(child.Name, "tri", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(child.Name, "i", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(child.Name, "index", StringComparison.OrdinalIgnoreCase))
                    {
                        var aAttr = child.Attributes?["a"]?.Value ?? child.Attributes?["v1"]?.Value;
                        var bAttr = child.Attributes?["b"]?.Value ?? child.Attributes?["v2"]?.Value;
                        var cAttr = child.Attributes?["c"]?.Value ?? child.Attributes?["v3"]?.Value;
                        if (uint.TryParse(aAttr, out var a) &&
                            uint.TryParse(bAttr, out var b) &&
                            uint.TryParse(cAttr, out var c))
                        {
                            tris.Add(a); tris.Add(b); tris.Add(c);
                        }
                    }
                }
                if (verts.Count > 0)
                {
                    meshComp.CustomMeshPoints = verts.ToArray();
                    meshComp.Shape = V12.Components.MeshShape.Custom;
                }
                if (tris.Count > 0)
                    meshComp.CustomIndices = tris.ToArray();
            }

            return component;
        }

        private void SetPropertyIfExists(object target, string propName, string? value, List<DeferredPropertySet> deferredList)
        {
            if (value == null) return;
            var pi = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            
            if (pi == null || !pi.CanWrite) 
                return;

            try
            {
                // Defer if property type is a component/interface and value is a string
                if ((pi.PropertyType.IsInterface || typeof(IComponent).IsAssignableFrom(pi.PropertyType)) && pi.PropertyType != typeof(string))
                {
                    deferredList.Add(new DeferredPropertySet { Target = target, Property = pi, ComponentName = value });
                    return;
                }

                var converted = ConvertToType(value, pi.PropertyType);
                pi.SetValue(target, converted);
                //Console.WriteLine($"[WorldMLParser] Set {propName} to {value} on {target.GetType().Name}");
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"[WorldMLParser] Error setting {propName}: {ex.Message}");
            }
        }

        private object? ConvertToType(string value, Type targetType)
        {
            if (targetType == typeof(string)) return value;
            if (targetType.IsEnum)
            {
                try { return Enum.Parse(targetType, value, true); } catch { return null; }
            }
            try
            {
                if (Nullable.GetUnderlyingType(targetType) is Type ut)
                {
                    if (string.IsNullOrEmpty(value)) return null;
                    return Convert.ChangeType(value, ut);
                }
                return Convert.ChangeType(value, targetType);
            }
            catch { return null; }
        }

        private IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
        }

        private static double? TryParseDouble(string? value)
        {
            if (value == null) return null;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result))
                return result;
            return null;
        }
    }
}
