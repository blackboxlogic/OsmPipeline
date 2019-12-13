using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class TagsTrees
	{
		public readonly static Dictionary<string, TagTreeHierarchy> Keys
			= new Dictionary<string, TagTreeHierarchy>(StringComparer.OrdinalIgnoreCase);
		static TagsTrees()
		{
			var building = new TagTreeHierarchy("building", "yes");
			building.Add("yes", "commercial", "residential", "school", "hospital", "government");
			building.Add("commercial", "retail", "office");
			building.Add("residential", "apartments", "detached", "duplex", "static_caravan", "house");
			Keys.Add("building", building);
		}
	}

	public class TagTreeHierarchy
	{
		public readonly string Key;
		private readonly TreeNode Root;
		private readonly Dictionary<string, TreeNode> Index;

		public TagTreeHierarchy(string key, string root)
		{
			Key = key;
			Root = new TreeNode() { Value = root };
			Index = new Dictionary<string, TreeNode>() { { root, Root } };
		}

		public void Add(string parent, params string[] values)
		{
			var parentNode = Index[parent];
			foreach (var value in values)
			{
				var childNode = new TreeNode() { Value = value, Parent = parentNode };
				parentNode.Children.Add(childNode);
				Index.Add(value, childNode);
			}
		}

		public string FindFirstCommonAncestor(string a, string b)
		{
			var left = Index[a].GetAncestors().Select(n => n.Value).Reverse();
			var right = Index[b].GetAncestors().Select(n => n.Value).Reverse();
			var firstCommonAncestor = left.Zip(right, (l, r) => l == r ? l : null).Last(p => p != null);
			return firstCommonAncestor;
		}

		public bool IsDecendantOf(string a, string b)
		{
			return Index[a].GetAncestors().Any(n => n.Value == b);
		}

		private class TreeNode
		{
			public string Value;
			public TreeNode Parent;
			public List<TreeNode> Children = new List<TreeNode>();

			public IEnumerable<TreeNode> GetAncestors()
			{
				var reference = this;
				while (reference != null)
				{
					yield return reference;
					reference = reference.Parent;
				}
			}
		}
	}
}
