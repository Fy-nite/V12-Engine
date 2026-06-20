using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.WorldML
{
    public class WorldMLParser
    {
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
                Console.WriteLine("XML Contenets:" + Environment.NewLine + doc.OuterXml);
                if (root == null) throw new Exception("Invalid XML: No root element found.");
                var worldName = root.Attributes?["name"]?.Value ?? "World";
                var world = new Element(worldName);

                // If the root node itself contains element nodes, parse them as world elements.
                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element) continue;
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
            // Add to world registry so it can be found later by systems that iterate Root
            world.AddChild(element);

            var deferredList = new List<DeferredPropertySet>();

            // Process component child nodes and nested elements
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;

                var childName = child.Name;

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
                    Console.WriteLine($"[WorldMLParser] Resolved deferred property {ds.Property.Name} to component {ds.ComponentName} on {element.Name}");
                }
                else if (refComp == null)
                {
                    Console.WriteLine($"[WorldMLParser] WARNING: Could not find component named '{ds.ComponentName}' on element '{element.Name}' to satisfy property '{ds.Property.Name}'");
                }
            }

            return element;
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
                //if (nameAttr != null)
                //{
                //    component.Name = nameAttr.Value;
                //}

                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (string.Equals(attr.Name, "type", StringComparison.OrdinalIgnoreCase) || 
                        string.Equals(attr.Name, "name", StringComparison.OrdinalIgnoreCase)) continue;
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

            return component;
        }

        private void SetPropertyIfExists(object target, string propName, string? value, List<DeferredPropertySet> deferredList)
        {
            if (value == null) return;
            var pi = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            
            // ADDED DEBUG LOGGING
            Console.WriteLine($"[WorldMLParser] DEBUG: Attempting to set '{propName}' on '{target.GetType().Name}'. Found PI: {pi != null}");
            
            if (pi == null || !pi.CanWrite) 
            {
                Console.WriteLine($"[WorldMLParser] DEBUG: Property '{propName}' NOT FOUND or NOT WRITABLE on {target.GetType().Name}. Available: {string.Join(", ", target.GetType().GetProperties().Select(p => p.Name))}");
                return;
            }

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
                Console.WriteLine($"[WorldMLParser] Set {propName} to {value} on {target.GetType().Name}");
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
    }
}
