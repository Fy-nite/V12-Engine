using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    public class ToggleComponent : ComponentBase
    {
        public override string Name => "Toggle";
        public override string Description => "Toggle control";

        public string Label { get; set; } = string.Empty;
        public bool IsOn { get; set; }
        [BsonIgnore]
        public Action<bool>? OnToggled { get; set; }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}

