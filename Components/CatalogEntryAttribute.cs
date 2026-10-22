using System;
using System.Runtime.CompilerServices;

namespace V12.Components
{

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class ComponentEntryAttribute : Attribute
	{

		public string Category { get; }

		public string DisplayName { get; }


		public bool SingleInstance { get; set; } = true;

		public int SortOrder { get; set; }

		public ComponentEntryAttribute(string category, string displayName)
		{
			this.Category = category;
			this.DisplayName = displayName;
		}
	}
}
