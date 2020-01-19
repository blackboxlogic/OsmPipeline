﻿using Microsoft.Extensions.Logging;
using OsmSharp;
using System;
using System.Linq;
using BAMCIS.GeoJSON;
using OsmSharp.Tags;
using OsmPipeline.Fittings;
using System.Collections.Generic;
using OsmSharp.API;
using System.Threading.Tasks;

namespace OsmPipeline
{
	public static class References
	{
		private static ILogger Log;

		public static Dictionary<string, string> StreetSUFFIX =
			new Dictionary<string, string>(
				FileSerializer.ReadJson<Dictionary<string, string>>(@"StreetSUFFIX.json"),
				StringComparer.OrdinalIgnoreCase);

		public static Dictionary<string, string> MUNICIPALITY =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "TWP", "Township" },
				{ "RES", "Reservation" },
				{ "PLT", "Plantation" }
			};

		public static Dictionary<string, string> PrePostDIRs =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "N" , "North" },
					{ "NE" , "North East" },
					{ "E" , "East" },
					{ "SE" , "South East" },
					{ "S" , "South" },
					{ "SW" , "South West" },
					{ "W" , "West" },
					{ "NW" , "North West" },
					{ "" , "" },
				};

		private static Dictionary<string, Dictionary<string, string>> PLACE_TYPEs =
			new Dictionary<string, Dictionary<string, string>>(
				FileSerializer.ReadJson<Dictionary<string, Dictionary<string, string>>>(@"PLACE_TYPE.json"),
				StringComparer.OrdinalIgnoreCase);

		private static Dictionary<string, string> UNITs =
			new Dictionary<string, string>(FileSerializer.ReadJson<Dictionary<string, string>>(@"UNIT.json"),
				StringComparer.OrdinalIgnoreCase);

		public static async Task<Osm> Fetch(string scopeName)
		{
			Log = Log ?? Static.LogFactory.CreateLogger(typeof(References));
			Log.LogInformation("Fetching Reference material from Maine E911 API");

			// Fetch GIS
			var stateGis = await FileSerializer.ReadJsonCacheOrSource(
				scopeName + "/ReferenceRaw.GeoJson", () => GeoJsonAPISource.FetchMunicipality(scopeName));
			var gisFeatures = stateGis.Features.ToArray();
			Log.LogInformation("Validating Reference material");
			Validate(gisFeatures);

			// Fetch the list of objectIDs with known errors to omit.
			var blacklist = Static.Municipalities[scopeName].BlackList.ToHashSet();
			Log.LogInformation("Translating Reference material");
			// Convert
			var nodes = gisFeatures
				.Where(f => !blacklist.Contains((long)f.Properties["OBJECTID"]))
				.Select(Convert)
				.ToArray();

			var translated = new Osm() { Nodes = nodes, Version = .6 };
			FileSerializer.WriteXml(scopeName + "/ReferenceTranslated.osm", translated);
			nodes = HandleStacks(nodes);
			Validate(nodes);
			var bounds = new Bounds()
			{
				MaxLatitude = (float)nodes.Max(n => n.Latitude) +.001f,
				MinLatitude = (float)nodes.Min(n => n.Latitude) - .001f,
				MaxLongitude = (float)nodes.Max(n => n.Longitude) + .001f,
				MinLongitude = (float)nodes.Min(n => n.Longitude) - .001f,
			};
			var filtered = new Osm() { Nodes = nodes, Version = .6, Bounds = bounds };

			return filtered;
		}

		private static Node Convert(Feature feature)
		{
			var props = feature.Properties;
			var streetName = BuildStreetName((string)props["PREDIR"],
				(string)props["STREETNAME"], (string)props["POSTDIR"], (string)props["SUFFIX"]);
			var level = Level((string)props["FLOOR"], out bool useFloorAsUnit);
			var unit = ReplaceToken(useFloorAsUnit ? (string)props["FLOOR"] : (string)props["UNIT"], UNITs);
			var city = ReplaceToken((string)props["MUNICIPALITY"], MUNICIPALITY);
			var tags = new []
			{
				new Tag("name", Name((string)props["LANDMARK"], (string)props["LOC"], (string)props["BUILDING"])),
				// Missing house numbers will have a "0" here. Mostly: Lewiston.
				new Tag("addr:housenumber", (int)props["ADDRESS_NUMBER"] == 0 ? "" : ((int)props["ADDRESS_NUMBER"]).ToString()),
				new Tag("addr:unit", unit),
				new Tag("addr:street", streetName),
				new Tag("addr:city", city),
				new Tag("addr:state", (string)props["STATE"]),
				new Tag("addr:postcode", (string)props["ZIPCODE"]),
				new Tag("level", level),
				new Tag(Static.maineE911id, ((int)props["OBJECTID"]).ToString())
			};

			var placeTags = PLACE_TYPEs[(string)props["PLACE_TYPE"]].Select(kvp => new Tag(kvp.Key, kvp.Value)).ToArray();
			tags = tags.Concat(placeTags)
				.Select(t => new Tag(t.Key, t.Value?.Trim()))
				.Where(t => t.Value != "" && t.Value != null)
				.ToArray();

			var n = new Node()
			{
				// Geometry lat-lon are higher precision than feature's lat-lon properties
				Latitude = (feature.Geometry as Point).Coordinates.Latitude,
				Longitude = (feature.Geometry as Point).Coordinates.Longitude,
				Tags = new TagsCollection(tags),
				Id = -(int)props["OBJECTID"],
				Version = 1
			};

			return n;
		}
		// Keys which can be removed in order to combine congruent nodes
		private static string[] SacrificialKeys = new[] { "addr:unit", "level", Static.maineE911id };

		// a
		// b
		// b 1
		// b 2
		// c 1
		// c 2
		// d 1
		// d 1
		// e 1
		// =>
		// a
		// b
		// c
		// d 1
		// e 1
		// unit/floor removed if they dissagree
		// remove address-only if there is an address+
		private static Node[] HandleStacks(Node[] nodes)
		{
			List<Node> results = new List<Node>();
			Node[][] addresses = nodes.GroupBy(Tags.GetBaseAddress)
				.Select(g => g.ToArray())
				.ToArray();
			foreach (var address in addresses)
			{
				var nearDuplicateSets = GroupCloseNeighbors(address, int.Parse(Static.Config["MatchDistanceKmMin"]));

				foreach (var nearDuplicateSet in nearDuplicateSets)
				{
					if (nearDuplicateSet.Count == 1)
					{
						results.Add(nearDuplicateSet[0]);
					}
					else
					{
						List<Node> groupResult = new List<Node>();
						var mergables = nearDuplicateSet.GroupBy(n =>
							new TagsCollection(n.Tags.Where(t => !SacrificialKeys.Contains(t.Key))))
							.Select(g => g.ToArray())
							.ToArray();
						foreach (var mergable in mergables)
						{
							groupResult.Add(IntersectTagsAndAverageLocation(mergable));
						}

						results.AddRange(RemoveLessSpecific(groupResult));
					}
				}
			}

			var stacks = results.GroupBy(n => new { n.Latitude, n.Longitude })
				.Select(g => g.ToArray())
				.Where(s => s.Length > 1)
				.ToArray();
			foreach (var stack in stacks)
			{
				Fittings.Geometry.Nudge(stack, 0, .00001);
			}

			Log.LogInformation($"{results.Count} addresses remain from {nodes.Length} (de-duped or combined)");

			return results.ToArray();
		}

		private static List<List<Node>> GroupCloseNeighbors(Node[] address, double closenessMeters)
		{
			var stacks = address.GroupBy(Fittings.Geometry.AsPosition)
				.Select(stack => new
				{
					positions = new List<Position> { stack.Key },
					nodes = stack.ToList()
				})
				.ToList();
			// Combine groups when everything in them is close to everything in another group.
			for (int i = 0; i < stacks.Count - 1; i++)
			{
				for (int j = i + 1; j < stacks.Count; j++)
				{
					var maybeMergeable = stacks[i].positions.SelectMany(l => stacks[j].positions, Fittings.Geometry.DistanceMeters)
						.All(d => d < closenessMeters);
					if (maybeMergeable)
					{
						stacks[i].positions.AddRange(stacks[j].positions);
						stacks[i].nodes.AddRange(stacks[j].nodes);
						stacks.RemoveAt(j);
						j--;
					}
				}
			}

			return stacks.Select(g => g.nodes).ToList();
		}

		private static Node IntersectTagsAndAverageLocation(Node[] stack)
		{
			var ids = string.Join(";", stack.SelectMany(n => n.Tags).Where(t => t.Key == Static.maineE911id).Select(t => t.Value));
			var tags = stack[0].Tags.ToList();
			foreach (var node in stack.Skip(1))
			{
				tags = tags.Intersect(node.Tags).ToList();
			}
			tags.Add(new Tag(Static.maineE911id, ids));
			stack[0].Tags = new TagsCollection(tags);

			var levels = stack.SelectMany(n => n.Tags)
				.Where(t => t.Key == "level")
				.Select(t =>
				{
					int.TryParse(t.Value, out int level);
					return level;
				}).DefaultIfEmpty().Max();

			stack[0].Latitude = stack.Average(n => n.Latitude);
			stack[0].Longitude = stack.Average(n => n.Longitude);

			return stack[0];
		}

		// a
		// a 1
		// a 2
		// b 1
		// =>
		// a 1
		// a 2
		// b 1
		private static List<Node> RemoveLessSpecific(List<Node> stack)
		{
			// Keep when there are no other node's tags that when taken from mine leave me empty.
			for (int i = 0; i < stack.Count; i++)
			{
				var node = stack[i];
				string building = null;
				var bigger = stack.FirstOrDefault(other => node != other
					&& node.Tags.All(t => other.Tags.Contains(t)
						|| SacrificialKeys.Contains(t.Key)
						|| (t.Key == "building" && OtherBuildingIsMoreGeneral(t.Value, other, out building))));
				if (bigger != null)
				{
					if (building != null) bigger.Tags["building"] = building;
					bigger.Tags.AddOrAppend(Static.maineE911id, node.Tags[Static.maineE911id]);
					stack.RemoveAt(i);
					i--;
				}
			}

			return stack;
		}

		private static bool OtherBuildingIsMoreGeneral(string myBuilding, Node other, out string newBuilding)
		{
			if (other.Tags.TryGetValue("building", out string otherBuilding))
			{
				newBuilding = TagTree.Keys["building"].FindFirstCommonAncestor(myBuilding, otherBuilding);
				return true;
			}

			newBuilding = null;
			return false;
		}

		private static void Validate(Feature[] features)
		{
			int[] duplicateObjectIds = features
				.Select(f => (int)f.Properties["OBJECTID"])
				.GroupBy(x => x)
				.Where(x => x.Skip(1).Any())
				.Select(x => x.Key)
				.ToArray();
			if (duplicateObjectIds.Any())
			{
				Log.LogError("ObjectIDs aren't unique! " + string.Join(",", duplicateObjectIds));
			}

			foreach (var f in features)
			{
				if (!StreetSUFFIX.ContainsKey(f.Properties["SUFFIX"]))
				{
					Log.LogError("Bad SUFFIX");
				}
				if (!PrePostDIRs.ContainsKey(f.Properties["PREDIR"]))
				{
					Log.LogError("Bad PREDIR");
				}
				if (string.IsNullOrWhiteSpace((string)f.Properties["STREETNAME"]))
				{
					Log.LogError("Bad STREETNAME");
				}
				if (!PrePostDIRs.ContainsKey(f.Properties["POSTDIR"]))
				{
					Log.LogError("Bad POSTDIR");
				}
				var goodFloor = f.Properties["FLOOR"] == null
					|| ((string)f.Properties["FLOOR"]).Split().All(part =>
						part.Equals("floor", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("flr", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("fl", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("ll", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("bsmt", StringComparison.OrdinalIgnoreCase)
						|| part.All(char.IsNumber));
				if (!goodFloor)
				{
					Log.LogWarning("Bad Floor: " + (string)f.Properties["FLOOR"]);
				}
				if (!((string)f.Properties["BUILDING"]).All(char.IsNumber)
					&& !((string)f.Properties["BUILDING"]).StartsWith("Bldg", StringComparison.OrdinalIgnoreCase))
				{
					Log.LogWarning("Bad bulding: " + (string)f.Properties["BUILDING"]);
				}
				if (!string.IsNullOrEmpty(f.Properties["ROOM"]))
				{
					Log.LogWarning("Ignoring Room: " + (string)f.Properties["ROOM"]);
				}
				if (!string.IsNullOrEmpty(f.Properties["SEAT"]))
				{
					Log.LogWarning("Ognoring Seat: " + (string)f.Properties["SEAT"]);
				}
				if (!int.TryParse((string)f.Properties["ZIPCODE"], out var zip)
					|| zip < 04000 || zip > 04999)
				{
					Log.LogError("Bad Zipcode: " + (string)f.Properties["ZIPCODE"]);
				}
				if ((f.Properties["ADDRESS_NUMBER"] ?? 0) == 0)
				{
					Log.LogError("Bad ADDRESS_NUMBER: " + (string)f.Properties["ADDRESS_NUMBER"].ToString());
				}
			}
		}

		private static void Validate(Node[] nodes)
		{
			if (nodes.Length > 10_000) Log.LogWarning($"{nodes.Length} Nodes may be too big for one changest");

			var duplicates = nodes.GroupBy(n => new { n.Tags })
				.Select(g => g.ToArray())
				.Where(s => s.Length > 1)
				.ToArray();

			if (duplicates.Any())
			{
				Log.LogError(duplicates.Length + " Duplicate Node Tag sets (different lat-lon) have been detected and not corrected.");
			}

			foreach (var node in nodes)
			{
				if (node.Tags["addr:state"] != "ME" ||
					!node.Tags["addr:street"].All(c => char.IsLetter(c) || c == ' ') || //"Interstate 95"
					(!node.Tags.ContainsKey("addr:housenumber") ||
					!node.Tags["addr:housenumber"].All(char.IsNumber)) ||
					node.Latitude == 0 || node.Latitude == null ||
					node.Longitude == 0 || node.Longitude == null)
				{
					Log.LogError("BAD ADDRESS " + node);
				}
			}
		}

		private static string Name(string landmark, string loc, string building)
		{
			if (building?.Length > 0 && building?.Length < 4)
			{
				building = "BLDG " + building;
			}

			var name = ReplaceToken(landmark + ' ' + building + ' ' + loc, UNITs);

			return name;
		}

		// "FLR 1", "Floor 1", "1", "bsmt", "second", "flr 1 & 2"
		// Sometimes the floor contains what should be in UNIT
		private static string Level(string floor, out bool useFloorAsUnit)
		{
			useFloorAsUnit = false;
			if (floor == null || floor == "") return "";
			if (floor.Contains("BSMT", StringComparison.OrdinalIgnoreCase) ||
				floor.Contains("Basement", StringComparison.OrdinalIgnoreCase) ||
				floor.EndsWith(" LL", StringComparison.OrdinalIgnoreCase)) return "-1";
			else if (floor.Equals("GRD", StringComparison.OrdinalIgnoreCase)) return "0";
			else if (floor.Equals("FIRST", StringComparison.OrdinalIgnoreCase)) return "1";
			else if (floor.Equals("SECOND", StringComparison.OrdinalIgnoreCase)) return "2";
			else if (floor.Equals("THIRD", StringComparison.OrdinalIgnoreCase)) return "3";

			var notFloors = new[] { "APT", "LOT", "TRLR", "UNIT", "STE" };
			
			if (notFloors.Any(n => floor.Contains(n, StringComparison.OrdinalIgnoreCase)))
			{
				useFloorAsUnit = true;
				return "";
			}

			return new string(floor
				.Replace('&', ';') // for ranges of floors
				.Replace(',', ';')
				.Replace('-', ';')
				.Replace("and", ";")
				.Where(c => char.IsNumber(c) || c == ';' || c =='.')
				.ToArray());
		}

		private static string BuildStreetName(string predir, string name, string postdir, string suffix)
		{
			var parts = new string[] {
					PrePostDIRs[predir],
					name,
					PrePostDIRs[postdir],
					StreetSUFFIX[suffix]
				}.Where(p => p != "");

			var fullName = string.Join(" ", parts);
			return fullName;
		}

		private static string ReplaceToken(string input, Dictionary<string, string> translation)
		{
			if (input == null) return "";
			var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			parts = parts.Select(part =>
				translation.TryGetValue(part, out string replacement)
					? replacement
					: part).ToArray();

			return string.Join(' ', parts);
		}
	}
}