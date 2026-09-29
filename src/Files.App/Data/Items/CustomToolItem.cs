// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Items
{
	[Serializable]
	public sealed class CustomToolItem
	{
		[JsonPropertyName("Name")]
		public string Name { get; set; } = string.Empty;

		[JsonPropertyName("ExecutablePath")]
		public string ExecutablePath { get; set; } = string.Empty;

		[JsonPropertyName("Arguments")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Arguments { get; set; }
	}
}
