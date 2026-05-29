namespace V12.Core.UI
{
    using System;
    using System.Collections.Generic;

    public interface IInspector
    {
        // Layer 1 — Primitive Controls
        void Bool(string label, Func<bool> getter, Action<bool> setter);
        void Int(string label, Func<int> getter, Action<int> setter);
        void Float(string label, Func<float> getter, Action<float> setter);
        void String(string label, Func<string> getter, Action<string> setter);
        void Vector2(string label, Func<object> getter, Action<object> setter); // Placeholder
        void Vector3(string label, Func<object> getter, Action<object> setter);
        void Color(string label, Func<object> getter, Action<object> setter);
        void Enum<T>(string label, Func<T> getter, Action<T> setter) where T : struct, Enum;
        void Asset<T>(string label, Func<T> getter, Action<T> setter) where T : class;
        void Entity(string label, Func<object> getter, Action<object> setter);

        // Layer 2 — Layout Helpers
        void Section(string title);
        void Separator();
        void Indent();
        void Unindent();
        void SameLine();
        void Spacing();

        // Layer 3 — Stateful UI Widgets
        void Foldout(string label, Func<bool> getter, Action<bool> setter);
        void TreeNode(string label);
        void TabBar(string label);
        void CollapsingHeader(string label);

        // Layer 4 — Validation & UX Feedback
        void HelpBox(string message);
        void Warning(string message);
        void Error(string message);
        void Tooltip(string text);
        void ReadOnly(string label, object value);
        void Disabled(string label, bool disabled);

        // Layer 5 — Runtime Interaction
        void Button(string label, Action action);
        void Action(string label, Action action);
        void Command(string label, string command);
        void ContextMenu(string label);

        // Layer 6 — Collections
        void List<T>(string label, Func<List<T>> getter, Action<List<T>> setter);
        void Array<T>(string label, Func<T[]> getter, Action<T[]> setter);
        void Dictionary<K, V>(string label, Func<Dictionary<K, V>> getter, Action<Dictionary<K, V>> setter);

        // Layer 7 — Advanced V12 Integration
        void ComponentReference(string label, Func<object> getter, Action<object> setter);
        void WorldReference(string label, Func<object> getter, Action<object> setter);
        void ObjectIRField(string label, Func<object> getter, Action<object> setter);
        void NetworkState(string label, Func<object> getter, Action<object> setter);
        void ReplicationFlags(string label, Func<object> getter, Action<object> setter);
    }
}
