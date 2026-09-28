// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Markup;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Files.App.Helpers
{
	[MarkupExtensionReturnType(ReturnType = typeof(string))]
	public sealed partial class ResourceString : MarkupExtension
	{
		private static readonly ResourceMap? resourcesTree = new ResourceManager().MainResourceMap.TryGetSubtree("Resources");

		public string Name { get; set; } = string.Empty;

		protected override object ProvideValue()
			=> resourcesTree?.TryGetValue(Name)?.ValueAsString ?? string.Empty;
	}
}
