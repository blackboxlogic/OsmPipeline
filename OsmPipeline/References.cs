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
				{ "PLT", "Plantation" },
				{ "CO", "County" },
				{ "CNTY", "County" },
				{ "CNT", "County" },
				{ "S", "South" },
				{ "St", "Saint" },
				{ "NE", "Northeast" },
				{ "04853", "North Haven" },
				{ "P*", "Point" }
			};

		public static Dictionary<string, string> PrePostDIRs =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "N" , "North" },
					{ "NE" , "Northeast" },
					{ "E" , "East" },
					{ "SE" , "Southeast" },
					{ "S" , "South" },
					{ "SW" , "Southwest" },
					{ "W" , "West" },
					{ "NW" , "Northwest" },
					{ "" , "" },
				};

		private static Dictionary<string, Dictionary<string, string>> PLACE_TYPEs =
			new Dictionary<string, Dictionary<string, string>>(
				FileSerializer.ReadJson<Dictionary<string, Dictionary<string, string>>>(@"PLACE_TYPE.json"),
				StringComparer.OrdinalIgnoreCase);

		private static Dictionary<string, string> UNITs =
			new Dictionary<string, string>(FileSerializer.ReadJson<Dictionary<string, string>>(@"UNIT.json"),
				StringComparer.OrdinalIgnoreCase);

		public static async Task<Osm> Fetch(string scopeName, Func<Feature, bool> filter = null)
		{
			TagTree.Reload();

			Log = Log ?? Static.LogFactory.CreateLogger(typeof(References));
			Log.LogInformation("Fetching Reference material from Maine E911 API");

			// Fetch GIS
			var stateGis = await FileSerializer.ReadJsonCacheOrSource(
				scopeName + "/ReferenceRaw.GeoJson", () => GeoJsonAPISource.FetchMunicipality(scopeName));
			var gisFeatures = stateGis.Features.Where(f => filter == null || filter(f)).ToArray();
			if (IsFeatureSchemaOld(gisFeatures[0]))
			{
				gisFeatures = gisFeatures.Select(UpdateFeatureSchema).ToArray();
			}
			Log.LogInformation("Validating Reference material");
			Validate(gisFeatures);

			// Fetch the list of objectIDs with known errors to omit.
			var blacklist = Static.Municipalities[scopeName].BlackList.ToHashSet();
			Log.LogInformation("Translating Reference material");
			// Convert
			var nodes = gisFeatures
				.Where(f => !blacklist.Contains((long)f.Properties[KEYS.OBJECTID]))
				.Select(Convert)
				.Where(n => n.Tags.ContainsKey("addr:housenumber"))
				.ToArray();

			if (!nodes.Any()) return null;
			nodes = HandleStacks(nodes);
			HandleBlackTags(nodes, Static.Municipalities[scopeName].BlackTags);

			foreach (var node in nodes.Where(n => n.Tags.ContainsKey("addr:unit")))
			{
				var newUnit = RemoveTokens(ReplaceTokens(node.Tags["addr:unit"], UNITs), Tags.UnitOmmisionable);
				if (newUnit == "") node.Tags.RemoveKey("addr:unit");
				else node.Tags["addr:unit"] = newUnit;
			}

			var filtered = new Osm() { Nodes = nodes, Version = .6, Bounds = nodes.AsBounds() };

			return filtered;
		}

		public static bool IsFeatureSchemaOld(Feature feature)
		{
			return feature.Properties.ContainsKey("MUNICIPALITY");
		}

		public static Feature UpdateFeatureSchema(Feature feature)
		{
			var upgrades = new Dictionary<string, string>() {
				{ "MUNICIPALITY", KEYS.TOWN },
				{ "POSTAL_COMMUNITY", KEYS.POSTCOMM }
			};
			foreach (var upgrade in upgrades)
			{
				feature.Properties[upgrade.Value] = feature.Properties[upgrade.Key];
				feature.Properties.Remove(upgrade.Key);
			}
			return feature;
		}

		public static void Report(OsmGeo[] nodes)
		{
			Console.WriteLine("Report");
			var keys = nodes.SelectMany(n => n.Tags)
				.Where(t => !t.Key.StartsWith(Static.maineE911id)
					&& t.Key != "addr:housenumber" && t.Key != "addr:street" && t.Key != "addr:place")
				.GroupBy(t => t)
				.Select(t => "\t" + t.First() + "\t\tx" + t.Count())
				.OrderBy(t => t);

			foreach (var keyValue in keys)
			{
				Console.WriteLine(keyValue);
			}

			Console.WriteLine();

			var streetSuffixes = nodes.SelectMany(n => n.Tags)
				.Where(t => t.Key == "addr:street" || t.Key == "addr:place")
				.Select(t => t.Key + "=*" + t.Value.Split(' ').Last())
				.GroupBy(t => t)
				.Select(t => "\t" + t.First() + "\t\tx" + t.Count())
				.OrderBy(t => t);

			foreach (var streetSuffix in streetSuffixes)
			{
				Console.WriteLine(streetSuffix);
			}
		}

		private static Node Convert(Feature feature)
		{
			var props = feature.Properties;
			var streetName = BuildStreetName((string)props[KEYS.PREDIR],
				(string)props[KEYS.STREETNAME], (string)props[KEYS.POSTDIR], (string)props[KEYS.SUFFIX]);
			var level = Level((string)props[KEYS.FLOOR], out bool useFloorAsUnit);
			var unit = useFloorAsUnit ? (string)props[KEYS.FLOOR] : (string)props[KEYS.UNIT] + (string)props[KEYS.ADDNUMSUF];
			var city = City((string)props[KEYS.POSTCOMM], (string)props[KEYS.TOWN]);
			var houseNumber = HouseNumber(props[KEYS.ADDRESS_NUMBER]);
			var tags = new []
			{
				new Tag("name", Name((string)props[KEYS.LANDMARK], (string)props[KEYS.LOC], (string)props[KEYS.BUILDING])),
				// Missing house numbers will have a "0" here. Mostly: Lewiston.
				new Tag("addr:housenumber", houseNumber),
				new Tag("addr:unit", unit),
				new Tag("addr:street", streetName),
				new Tag("addr:city", city),
				new Tag("addr:state", (string)props[KEYS.STATE]),
				new Tag("addr:postcode", Zip((string)props[KEYS.ZIPCODE])),
				new Tag("level", level),
				new Tag(Static.maineE911id, ((int)props[KEYS.OBJECTID]).ToString())
			};

			var placeTags = PLACE_TYPEs[((string)props["PLACE_TYPE"]).Trim()].Select(kvp => new Tag(kvp.Key, kvp.Value)).ToArray();
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
				Id = -(int)props[KEYS.OBJECTID],
				Version = 1
			};

			return n;
		}
		// Keys which can be removed in order to combine congruent nodes
		private static string[] SacrificialKeys = new[] { "addr:unit", "level", Static.maineE911id };

		public static void HandleBlackTags(IList<OsmGeo> elements, IList<string> blackTags)
		{
			var byId = elements.ToDictionary(e => e.Tags[Static.maineE911id]);

			foreach (var tag in blackTags)
			{
				var parts = tag.Split('.', 2);
				var osmId = parts[0];
				var tagPattern = parts[1];
				if (osmId == "*") // *.name=Apt 1
				{
					parts = tagPattern.Split(new char[] { '=', '~' }, 4);
					var oldKey = parts[0];
					var tagValue = parts[1];
					var newKey = parts.Length >= 3 ? parts[2] : null;
					foreach (var element in elements)
					{
						if (element.Tags.RemoveKeyValue(new Tag(oldKey, tagValue)))
						{
							if (newKey != null) // *.name=Apt 1~addr:unit
							{
								var newValue = parts.Length == 4 ? parts[3] : tagValue; // *.name=Apt 1~addr:unit=1
								element.Tags.AddOrAppend(newKey, newValue);
								element.Tags.AddOrAppend(Static.maineE911id + ":" + newKey, "moved from: " + oldKey);
							}
							else
							{
								element.Tags.Add(Static.maineE911id + ":" + oldKey, "ommitted: " + tagValue);
							}
						}
					}
				}
				else if (byId.TryGetValue(parts[0], out OsmGeo elementById)) // 2984323.name
				{
					parts = tagPattern.Split(new char[] { '=', '~' }, 3);
					var oldKey = parts[0];
					var newKey = parts.Length >= 2 ? parts[1] : null;
					if (newKey != null) // 2984323.name~addr:unit
					{
						var newValue = parts.Length == 3 ? parts[2] : elementById.Tags[oldKey]; // 2984323.name~addr:unit=1
						elementById.Tags.RemoveKey(oldKey);
						elementById.Tags.Add(newKey, newValue);
						elementById.Tags.Add(Static.maineE911id + ":" + newKey, "moved from: " + oldKey);
					}
					else
					{
						elementById.Tags.AddOrAppend(Static.maineE911id + ":" + oldKey, "ommitted: " + elementById.Tags[oldKey]);
						elementById.Tags.RemoveKey(oldKey);
					}
				}
			}
		}

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
				var nearDuplicateSets = GroupCloseNeighbors(address, int.Parse(Static.Config["MatchDistanceMetersMin"]));

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

		public static List<List<Node>> GroupCloseNeighbors(Node[] address, double closenessMeters)
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

			//var levels = stack.SelectMany(n => n.Tags)
			//	.Where(t => t.Key == "level")
			//	.Select(t =>
			//	{
			//		int.TryParse(t.Value, out int level);
			//		return level;
			//	}).DefaultIfEmpty().Max();
			//if (levels > 0) stack[0].Tags["building:levels"] = levels;

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
				.Select(f => (int)f.Properties[KEYS.OBJECTID])
				.GroupBy(x => x)
				.Where(x => x.Skip(1).Any())
				.Select(x => x.Key)
				.ToArray();
			if (duplicateObjectIds.Any())
			{
				Log.LogError("ObjectIDs aren't unique! " + string.Join(",", duplicateObjectIds));
			}

			var reports = new List<string>();

			foreach (var f in features)
			{
				if (!StreetSUFFIX.ContainsKey(f.Properties[KEYS.SUFFIX]))
				{
					reports.Add("Bad SUFFIX" + f.Properties[KEYS.SUFFIX]);
				}
				if (!PrePostDIRs.ContainsKey(f.Properties[KEYS.PREDIR]))
				{
					reports.Add("Bad PREDIR" + f.Properties[KEYS.PREDIR]);
				}
				if (string.IsNullOrWhiteSpace((string)f.Properties[KEYS.STREETNAME]))
				{
					reports.Add("Bad STREETNAME");
				}
				if (!((string)f.Properties[KEYS.STREETNAME]).Any(char.IsLetter))
				{
					reports.Add($"Odd Road name: {f.Properties[KEYS.STREETNAME]}");
				}
				if (!PrePostDIRs.ContainsKey(f.Properties[KEYS.POSTDIR]))
				{
					reports.Add("Bad POSTDIR" + f.Properties[KEYS.POSTDIR]);
				}
				var goodFloor = f.Properties[KEYS.FLOOR] == null
					|| ((string)f.Properties[KEYS.FLOOR]).Split().All(part =>
						part.Equals("floor", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("flr", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("fl", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("ll", StringComparison.OrdinalIgnoreCase)
						|| part.Equals("bsmt", StringComparison.OrdinalIgnoreCase)
						|| part.All(char.IsNumber));
				if (!goodFloor)
				{
					reports.Add("Bad Floor: " + f.Properties[KEYS.FLOOR]);
				}
				if (!((string)f.Properties[KEYS.BUILDING]).All(char.IsNumber)
					&& !((string)f.Properties[KEYS.BUILDING]).StartsWith("Bldg", StringComparison.OrdinalIgnoreCase))
				{
					reports.Add("Bad bulding: " + (string)f.Properties[KEYS.BUILDING]);
				}
				if (!string.IsNullOrEmpty(f.Properties[KEYS.ROOM]))
				{
					reports.Add("Ignoring Room: " + (string)f.Properties[KEYS.ROOM]);
				}
				if (!string.IsNullOrEmpty(f.Properties[KEYS.SEAT]))
				{
					reports.Add("ignoring Seat: " + (string)f.Properties[KEYS.SEAT]);
				}
				if (!int.TryParse((string)f.Properties[KEYS.ZIPCODE], out var zip)
					|| zip < 03000 || zip > 04999)
				{
					reports.Add("Ignoring zip: " + (string)f.Properties[KEYS.ZIPCODE]);
				}
				if ((f.Properties[KEYS.ADDRESS_NUMBER] ?? 0) == 0)
				{
					reports.Add("Bad ADDRESS_NUMBER: " + (string)f.Properties[KEYS.ADDRESS_NUMBER].ToString());
				}
				if (f.Properties[KEYS.STATE] != "ME")
				{
					reports.Add("Bad STATE: " + (string)f.Properties[KEYS.STATE]);
				}

				if ((f.Properties[KEYS.TOWN] ?? f.Properties[KEYS.POSTCOMM]) != (f.Properties[KEYS.POSTCOMM] ?? f.Properties[KEYS.TOWN]))
				{
					reports.Add($"Ambiguous TOWN: {f.Properties[KEYS.TOWN]}, POSTCOMM: {f.Properties[KEYS.POSTCOMM]}");
				}
			}

			foreach (var report in reports.GroupBy(r => r).OrderBy(r => r.Key))
			{
				Log.LogError(report.Key + "\t\tx" + report.Count());
			}
		}

		private static string Name(string landmark, string loc, string building)
		{

			if (double.TryParse(loc, out _)) loc = ""; // Some LOCations are like "377162.5568"?
			if (landmark == loc) loc = ""; // Sometimes they just duplicate each other?
			if (landmark == building || loc == building) building = ""; // Sometimes they just duplicate each other?

			if (!string.IsNullOrWhiteSpace(building) && building?.Length > 0 && building?.Length < 4)
			{
				building = "BLDG " + building;
			}

			var name = ReplaceTokens(landmark + ' ' + building + ' ' + loc, UNITs);
			name = DeDuple(name);
			return name;
		}

		//.Replace("Building Building", "Building")
		private static string DeDuple(string name)
		{
			var parts = name.Split();
			for (int i = 1; i < parts.Length; i++)
			{
				if (parts[i] == parts[i - 1]) parts[i] = null;
			}
			return string.Join(" ", parts);
		}

		// "FLR 1", "Floor 1", "1", "bsmt", "second", "flr 1 & 2"
		// Sometimes the floor contains what should be in UNIT
		private static string Level(string floor, out bool useFloorAsUnit)
		{
			useFloorAsUnit = false;
			if (string.IsNullOrWhiteSpace(floor)) return "";
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
					StreetSUFFIX[suffix],
					PrePostDIRs[postdir]
				}.Where(p => p != "");

			var fullName = string.Join(" ", parts);
			return fullName;
		}

		private static string Zip(string zip)
		{
			if (zip == null || zip.Length != 5) return "";
			if (zip.Count(char.IsDigit) != 5) return "";
			if (zip.StartsWith("00")) return "";
			if (!zip.StartsWith("0")) return "";
			return zip;
		}

		private static string HouseNumber(long addressNumber)
		{
			return addressNumber == 0 ? "" : addressNumber.ToString();
		}

		private static string City(string postalCommunity, string town)
		{
			//if (string.IsNullOrWhiteSpace(town))
			//{
			//	return ReplaceTokens(postalCommunity, MUNICIPALITY);
			//}
			//else if (string.IsNullOrWhiteSpace(postalCommunity))
			//{
			//	return ReplaceTokens(town, MUNICIPALITY);
			//}
			//else if (Static.Municipalities.ContainsKey(postalCommunity)) // A neighboring municipality manages the mail
			//{
			//	return ReplaceTokens(town, MUNICIPALITY);
			//}
			//else
			//{
			//	return ReplaceTokens(postalCommunity, MUNICIPALITY);
			//}

			// Use TOWN
			//return string.IsNullOrWhiteSpace(town)
			//	? ReplaceTokens(postalCommunity, MUNICIPALITY)
			//	: ReplaceTokens(town, MUNICIPALITY);

			// Use POSTAL COM
			return string.IsNullOrWhiteSpace(postalCommunity)
				? ReplaceTokens(town, MUNICIPALITY)
				: ReplaceTokens(postalCommunity, MUNICIPALITY);
		}

		private static string ReplaceTokens(string input, Dictionary<string, string> translation)
		{
			if (string.IsNullOrWhiteSpace(input)) return "";
			var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			parts = parts.Select(part =>
				translation.TryGetValue(part, out string replacement)
					? replacement
					: part)
				.Distinct()
				.ToArray();

			return string.Join(' ', parts).Trim();
		}

		private static string RemoveTokens(string input, string[] removables)
		{
			if (string.IsNullOrWhiteSpace(input)) return "";
			var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Except(removables, StringComparer.OrdinalIgnoreCase)
				.ToArray();

			return string.Join(' ', parts).Trim();
		}

		public class KEYS
		{
			public const string OBJECTID = "OBJECTID";
			public const string SOURCE_OF_DATA = "SOURCE_OF_DATA";
			public const string DATEUPDATE = "DATEUPDATE";
			public const string SITE_UID = "SITE_UID";
			public const string COUNTRY = "COUNTRY";
			public const string STATE = "STATE";
			public const string COUNTY = "COUNTY";
			public const string TOWN = "TOWN";
			public const string ADDRESS_NUMBER = "ADDRESS_NUMBER";
			public const string ADDNUMSUF = "ADDNUMSUF";
			public const string PREDIR = "PREDIR";
			public const string STREETNAME = "STREETNAME";
			public const string SUFFIX = "SUFFIX";
			public const string POSTDIR = "POSTDIR";
			public const string ESN = "ESN";
			public const string MSAGCOMM = "MSAGCOMM";
			public const string POSTCOMM = "POSTCOMM";
			public const string ZIPCODE = "ZIPCODE";
			public const string ZIPPLUS4 = "ZIPPLUS4";
			public const string BUILDING = "BUILDING";
			public const string FLOOR = "FLOOR";
			public const string UNIT = "UNIT";
			public const string ROOM = "ROOM";
			public const string SEAT = "SEAT";
			public const string LOC = "LOC";
			public const string LANDMARK = "LANDMARK";
			public const string PLACE_TYPE = "PLACE_TYPE";
			public const string PLACEMENT = "PLACEMENT";
			public const string LONGITUDE = "LONGITUDE";
			public const string LATITUDE = "LATITUDE";
			public const string ELEVATION = "ELEVATION";
			public const string RDNAME = "RDNAME";
			public const string ADDRESS = "ADDRESS";
			public const string SOURCE = "SOURCE";
			public const string MAO_NAME = "MAO_NAME";
			public const string MAO_DATE = "MAO_DATE";
			public const string MAO_EXCEPTION = "MAO_EXCEPTION";
			public const string STATE_ID = "STATE_ID";
			public const string MAP_BK_LOT = "MAP_BK_LOT";
			public const string TPL = "TPL";
			public const string GPL = "GPL";
			public const string MSAGCODE = "MSAGCODE";
			public const string CONGRESS_CODE = "CONGRESS_CODE";
			public const string PSAP = "PSAP";
			public const string AMUPDDAT = "AMUPDDAT";
			public const string AMUPDORG = "AMUPDORG";
			public const string FMUPDDAT = "FMUPDDAT";
			public const string FMUPDORG = "FMUPDORG";
			public const string CREATDAT = "CREATDAT";
			public const string CREATORG = "CREATORG";
			public const string GlobalID = "GlobalID";
			public const string SHAPE = "SHAPE";
		}
	}
}
