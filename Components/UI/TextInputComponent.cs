using System;

namespace V12.Components.UI
{
    /// <summary>
    /// A single-line text input field. Frontends render an editable text box and
    /// call <see cref="InvokeChanged"/> when the user commits a new value.
    /// </summary>
    public class TextInputComponent : ComponentBase
    {
        public override string Name => "TextInput";
        public override string Description => "Editable single-line text field";

        private string _value = string.Empty;

        /// <summary>Placeholder hint shown when the field is empty.</summary>
        public string Placeholder { get; set; } = string.Empty;

        /// <summary>Current committed value.</summary>
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value ?? string.Empty;
                    MarkDirty();
                }
            }
        }

        /// <summary>Raised when the user submits a new value via the frontend.</summary>
        public event Action<string> OnChanged;

        /// <summary>
        /// Called by the frontend when the user has finished editing.
        /// Updates <see cref="Value"/> and fires <see cref="OnChanged"/>.
        /// </summary>
        public void InvokeChanged(string newValue)
        {
            Value = newValue ?? string.Empty;
            try { OnChanged?.Invoke(_value); } catch { }
        }

        public TextInputComponent() { }
        public TextInputComponent(string placeholder) { Placeholder = placeholder ?? string.Empty; }
    }
}
