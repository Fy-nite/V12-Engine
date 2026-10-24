using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
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
using V12.Core.Interfaces.Renderer;

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
        IWorldElement LoadElementFromXml(string xml, IWorldElement parent)
        {
            var el = Parse(xml);   // single-root Element
            parent.AddChild(el);
            return el;
        }
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
            var worldName = GetAttr(root, "name") ?? GetAttr(root, "WorldName") ?? "World";
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
            var name = GetAttr(node, "name");
            var description = GetAttr(node, "description");

            var element = new Element(name, description, parent);

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
            string templateName = GetAttr(node, "template");
            if (!string.IsNullOrEmpty(templateName) && TemplateProvider != null)
            {
                ExpandTemplate(templateName, overrides, element, world);
            }

            var deferredList = new List<DeferredPropertySet>();
            // The <LocalTransform> block is recorded here and applied AFTER every
            // component is attached, so it survives whichever dialect wrote the
            // file: it wins over a synthesized TransformComponent, and a
            // TransformComponent written as attributes wins over it (see ApplyLocalTransform).
            var explicitTransform = ReadLocalTransform(node);

            bool transformSeen = false;

            // Process component child nodes and nested elements
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;

                var childName = child.Name;

                // <LocalTransform> is a value block, never an element.
                if (string.Equals(childName, "LocalTransform", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(childName, "Transform", StringComparison.OrdinalIgnoreCase))
                    continue;

                // <Instance template="name" /> — inline template expansion
                if (string.Equals(childName, "Instance", StringComparison.OrdinalIgnoreCase))
                {
                    string instTmpl = GetAttr(child, "template");
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

                if (IsComponentNode(childName))
                {
                    var comp = CreateComponentFromNode(child, deferredList);
                    if (comp != null)
                    {
                        element.AddComponent(comp);
                        if (string.Equals(comp.Name, "Transform", StringComparison.OrdinalIgnoreCase))
                            transformSeen = true;
                    }
                    continue;
                }

                // Otherwise treat as nested element
                ParseElement(child, world, element);
            }

            ApplyLocalTransform(element, explicitTransform, transformSeen);

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

        /// <summary>True when the node is a component to attach rather than a nested
        /// element: the canonical <c>&lt;Component type="…" /&gt;</c>, the terse
        /// <c>&lt;TransformComponent … /&gt;</c> shorthand, or the old dialect's
        /// <c>&lt;Component&gt;&lt;Type&gt;MeshComponent&lt;/Type&gt;…</c> value
        /// block, whose component is named in a child element.</summary>
        private static bool IsComponentNode(string tag)
        {
            return tag.Equals("Component", StringComparison.OrdinalIgnoreCase)
                || tag.EndsWith("Component", StringComparison.OrdinalIgnoreCase);
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
            var typeName = GetAttr(node, "type") ?? GetAttr(node, "typename");
            if (string.IsNullOrWhiteSpace(typeName))
            {
                // Old dialect: the component is named in a <Type> child block.
                typeName = ChildElementValue(node, "Type");
            }
            if (string.IsNullOrWhiteSpace(typeName))
                typeName = node.LocalName ?? node.Name;
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            // Try to find a type that implements IComponent
            var compType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t => typeof(IComponent).IsAssignableFrom(t) &&
                    (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)));

            if (compType == null)
            {
                // Silence here used to hide a whole dialect mismatch: every element
                // came back as a component-less shell with nothing on the console.
                Console.WriteLine($"[WorldMLParser] WARNING: Unknown component type '{typeName}' (element <{node.ParentNode?.Name}>) - component skipped. Is its assembly loaded?");
                return null;
            }

            object? instance = null;
            try { instance = Activator.CreateInstance(compType); } catch { return null; }

            // Activator returns object; verify it really implements IComponent before proceeding.
            if (instance is not IComponent component) return null;

            // Set simple properties from child <Property name="X" value="Y"/> or direct attributes on the component node
            // 1) attributes
            if (node.Attributes != null)
            {
                // Explicitly check for 'name' attribute to set component instance name
                var nameAttr = GetAttr(node, "name");
                if (nameAttr != null && component is ComponentBase cb)
                {
                    cb.Name = nameAttr;
                }

                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (string.Equals(attr.Name, "type", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(attr.Name, "typename", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(attr.Name, "name", StringComparison.OrdinalIgnoreCase)) continue;
                    SetPropertyIfExists(instance, attr.Name, attr.Value, deferredList);
                }
            }

            // 2) child Property nodes, and the old dialect's direct child-element
            //    values (<Energy>1.2</Energy>, <Shape>Box</Shape>) alongside them.
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;

                if (string.Equals(child.LocalName, "Property", StringComparison.OrdinalIgnoreCase))
                {
                    var propName = GetAttr(child, "name");
                    var propValue = GetAttr(child, "value") ?? child.InnerText;
                    if (string.IsNullOrEmpty(propName)) continue;
                    SetPropertyIfExists(instance, propName, propValue, deferredList);
                    continue;
                }

                // <Type> already named the component; skip it here.
                if (string.Equals(child.LocalName, "Type", StringComparison.OrdinalIgnoreCase)) continue;

                // Only capture leaves; a nested block (e.g. <Position>) is not a value.
                var text = child.InnerText;
                if (string.IsNullOrWhiteSpace(text)) continue;
                bool hasElementChild = false;
                foreach (XmlNode gc in child.ChildNodes)
                    if (gc.NodeType == XmlNodeType.Element) { hasElementChild = true; break; }
                if (hasElementChild) continue;

                SetPropertyIfExists(instance, child.LocalName, text.Trim(), deferredList);
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
            if (pi != null && pi.CanWrite)
            {
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
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WorldMLParser] Error setting {propName}: {ex.Message}");
                    return;
                }
            }

            var fi = target.GetType().GetField(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (fi == null) return;
            try
            {
                if ((fi.FieldType.IsInterface || typeof(IComponent).IsAssignableFrom(fi.FieldType)) && fi.FieldType != typeof(string))
                {
                    var param = new DeferredPropertySet { Target = target };
                    // Property required by DeferredPropertySet - build a helper isn't necessary
                    // just skip; fields to component references are rare
                    return;
                }
                var converted = ConvertToType(value, fi.FieldType);
                fi.SetValue(target, converted);
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

        /// <summary>Case-insensitive attribute lookup. .NET's XmlDocument indexer
        /// is case-sensitive, so <c>Attributes["name"]</c> silently returns null for
        /// an SDK-style <c>Name="…"</c> — which used to drop every element name and
        /// every component type in that dialect without a warning.</summary>
        private static string? GetAttr(XmlNode node, string name)
        {
            if (node.Attributes == null) return null;
            foreach (XmlAttribute attr in node.Attributes)
                if (string.Equals(attr.Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(attr.LocalName, name, StringComparison.OrdinalIgnoreCase))
                    return attr.Value;
            return null;
        }

        /// <summary>Value of a direct child element named <paramref name="name"/>.</summary>
        private static string? ChildElementValue(XmlNode node, string name)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (string.Equals(child.LocalName ?? child.Name, name, StringComparison.OrdinalIgnoreCase))
                    return child.InnerText?.Trim();
            }
            return null;
        }

        /// <summary>Read the SDK dialect's <c>&lt;LocalTransform&gt;</c> value block
        /// (<c>&lt;Position&gt;0,2.6,7&lt;/Position&gt;</c> etc.).</summary>
        private static TRS? ReadLocalTransform(XmlNode node)
        {
            XmlNode? block = null;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                var n = child.LocalName ?? child.Name;
                if (string.Equals(n, "LocalTransform", StringComparison.OrdinalIgnoreCase))
                {
                    block = child;
                    break;
                }
            }
            if (block == null) return null;

            var trs = new TRS { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };
            foreach (XmlNode field in block.ChildNodes)
            {
                if (field.NodeType != XmlNodeType.Element) continue;
                var fname = field.LocalName ?? field.Name;
                var raw = field.InnerText?.Trim();
                if (string.IsNullOrEmpty(raw)) continue;

                if (string.Equals(fname, "Position", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseVector3(raw, out var p)) trs.Position = p;
                }
                else if (string.Equals(fname, "Scale", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseVector3(raw, out var s)) trs.Scale = s;
                }
                else if (string.Equals(fname, "Rotation", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseQuaternion(raw, out var q)) trs.Rotation = q;
                }
            }
            return trs;
        }

        /// <summary>Apply a parsed transform to the element. A TransformComponent
        /// written as attributes always wins (it is the canonical sheet); otherwise
        /// the value block is pushed through <see cref="Element.LocalTransform"/>,
        /// which mirrors into the TransformComponent when one exists.</summary>
        private static void ApplyLocalTransform(Element element, TRS? explicitTransform, bool transformSeen)
        {
            if (explicitTransform == null) return;
            if (transformSeen)
            {
                // Canonical files carry both; the attributes already applied.
                // Only adopt the block when it actually says something different.
                var lt = element.LocalTransform;
                if (!TRSApproximatelyEqual(lt, explicitTransform.Value))
                {
                    Console.WriteLine($"[WorldMLParser] NOTE: '{element.Name}' has both a TransformComponent and a <LocalTransform>; using the TransformComponent.");
                }
                return;
            }
            element.LocalTransform = explicitTransform.Value;
        }

        private static bool TRSApproximatelyEqual(TRS a, TRS b)
        {
            const float eps = 1e-3f;
            return MathF.Abs(a.Position.X - b.Position.X) < eps
                && MathF.Abs(a.Position.Y - b.Position.Y) < eps
                && MathF.Abs(a.Position.Z - b.Position.Z) < eps
                && MathF.Abs(a.Scale.X - b.Scale.X) < eps
                && MathF.Abs(a.Scale.Y - b.Scale.Y) < eps
                && MathF.Abs(a.Scale.Z - b.Scale.Z) < eps
                && QuaternionApproximatelyEqual(a.Rotation, b.Rotation);
        }

        private static bool QuaternionApproximatelyEqual(Quaternion a, Quaternion b)
        {
            const float eps = 1e-3f;
            return (MathF.Abs(a.X - b.X) < eps && MathF.Abs(a.Y - b.Y) < eps
                 && MathF.Abs(a.Z - b.Z) < eps && MathF.Abs(a.W - b.W) < eps)
                || (MathF.Abs(a.X + b.X) < eps && MathF.Abs(a.Y + b.Y) < eps
                 && MathF.Abs(a.Z + b.Z) < eps && MathF.Abs(a.W + b.W) < eps);
        }

        private static bool TryParseVector3(string raw, out Vector3 value)
        {
            value = Vector3.Zero;
            var parts = SplitComponents(raw);
            if (parts.Length != 3) return false;
            value = new Vector3(parts[0], parts[1], parts[2]);
            return true;
        }

        /// <summary>Accepts either <c>x,y,z</c> or <c>(x, y, z)</c>.</summary>
        private static float[] SplitComponents(string raw)
        {
            var cleaned = raw.Trim().Trim('(', ')', '[', ']');
            var parts = cleaned.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                    return Array.Empty<float>();
            }
            return result;
        }

        /// <summary>Quaternions are read as <c>x,y,z,w</c>. The degenerate
        /// <c>0,0,0,0</c> block (how the old template wrote "no rotation") is read
        /// as identity rather than as a zero quaternion.</summary>
        private static bool TryParseQuaternion(string raw, out Quaternion value)
        {
            value = Quaternion.Identity;
            var parts = SplitComponents(raw);
            if (parts.Length != 4) return false;
            if (parts[0] == 0 && parts[1] == 0 && parts[2] == 0 && parts[3] == 0) return true;
            value = Quaternion.Normalize(new Quaternion(parts[0], parts[1], parts[2], parts[3]));
            return true;
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
