using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    public class CheckboxComponent : ComponentBase
    {
        public override string Name => "Checkbox";
        public override string Description => "Checkbox control";

        public string Label { get; set; } = string.Empty;
        public bool Checked { get; set; }
        [BsonIgnore]
        public Action<bool>? OnChanged { get; set; }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}

