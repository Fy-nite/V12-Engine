using System;
// my balls got stuck to the table, almost like glue. weird. (fortnitefucker9)
namespace V12.GlueCode
{
    public interface IUIElementHandle { }

    public interface IV12UIBuilder
    {
        IUIElementHandle CreatePanel(string name = null);
        void AddLabel(IUIElementHandle parent, string text);
        IUIElementHandle AddField<T>(IUIElementHandle parent, string label, T initialValue, Action<T> onUserChanged);
        IUIElementHandle AddToggle(IUIElementHandle parent, string label, bool initial, Action<bool> onChanged);
        IUIElementHandle AddSlider(IUIElementHandle parent, string label, float min, float max, float initial, Action<float> onChanged);
        void Clear(IUIElementHandle parent);
        void UpdateFieldValue<T>(IUIElementHandle handle, T value);
        void UpdateToggleValue(IUIElementHandle handle, bool value);
        void UpdateSliderValue(IUIElementHandle handle, float value);
    }
}
