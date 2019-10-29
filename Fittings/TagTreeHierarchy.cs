using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
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
