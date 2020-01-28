using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using jo = Newtonsoft.Json.Linq.JObject;
using jp = Newtonsoft.Json.Linq.JProperty;

namespace OsmPipeline.Fittings
{
	public class TagTree
	{
		public readonly static Dictionary<string, TagTree> Keys
			= new Dictionary<string, TagTree>(StringComparer.OrdinalIgnoreCase);

		static TagTree()
		{
			var json = File.ReadAllText("TagTree.json");
			var jObject = (jo)Newtonsoft.Json.JsonConvert.DeserializeObject(json);

			foreach (var key in jObject.Properties())
			{
				var valueRoot = ((jo)key.Value).Properties().Single();
				var tree = new TagTree(valueRoot.Name);
				AppendTree(tree, valueRoot);
				Keys.Add(key.Name, tree);
			}
		}

		private static void AppendTree(TagTree tree, jp node)
		{
			foreach (var child in ((jo)node.Value).Properties())
			{
				tree.Add(node.Name, child.Name);
				AppendTree(tree, child);
			}
		}

		private readonly TreeNode Root;
		private readonly Dictionary<string, TreeNode> Index;

		public TagTree(string root)
		{
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
			return (Index.TryGetValue(a, out var node) || Index.TryGetValue("*", out node))
				&& node.GetAncestors().Any(n => n.Value == b);
		}

		private class TreeNode
		{
			public string Value;
			public TreeNode Parent;
			public List<TreeNode> Children = new List<TreeNode>();

			public IEnumerable<TreeNode> GetAncestors()
			{
				var reference = this.Parent;
				while (reference != null)
				{
					yield return reference;
					reference = reference.Parent;
				}
			}
		}
	}
}

