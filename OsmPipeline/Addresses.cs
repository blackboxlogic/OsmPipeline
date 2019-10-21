using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OsmSharp;
using System;
using System.Linq;
using BAMCIS.GeoJSON;
using OsmSharp.Tags;
using OsmPipeline.Fittings;
using System.Collections.Generic;
using OsmSharp.API;

namespace OsmPipeline
{
	public static class Addresses
	{
		// Split by zip instead of municipality?
		// New address
		// Similar Address node exists
		// Similar Address way exists

		// Spit it out as geojson and then review it and commit it in iD?
		private static ILogger Log;

		public static void ValidateAddresses(ILoggerFactory loggerFactory, IConfigurationRoot config, OsmGeo scope)
		{
			Log = loggerFactory.CreateLogger(typeof(Addresses));

			var collection = CachedGeoJsonAPISource.FromFileOrFetch("Westbrook");
			var features = collection.Features.ToArray();

			Validate(features);
			var nodes = features.Select(Convert).ToArray();

			var translated = new Osm() { Nodes = nodes, Version = .6 };
			FileSerializer.Write("Translated.osm", translated);

			nodes = HandleStacks(nodes);
			Validate(nodes);

			var filtered = new Osm() { Nodes = nodes, Version = .6 };
			FileSerializer.Write("Filtered.osm", filtered);

			var query = OverpassQuery.Query(OverpassQuery.OverpassGeoType.NWR, Locations.Westbrook);
			query = OverpassQuery.AddOut(query, true);
			// fetch region from osm (or overpass?) osmSharp the pbf file
			var existing = FileSerializer.ReadCacheOrSource<Osm>("WestbrookOverpass.json", () => OverpassApi.Get(query));
			// conflate
			// upload

			//var osmApiEnder = new OsmApiEnder(logger, OsmApiUrl, OsmUsername, OsmPassword, changeTags);
			//var LoggingEnder = new Fittings.LoggingEnder(loggerFactory.CreateLogger("kml"));
			//var change = Edit(osm, EditGenerator, EditVersion).Result;
		}

		public static void ManualCorrections(Feature[] features)
		{
			var unitInLocField = features.First(f => f.Properties["OBJECTID"] == 1619128);
			unitInLocField.Properties["UNIT"] = unitInLocField.Properties["LOC"];
			unitInLocField.Properties["LOC"] = "";
			var OddNote = features.First(f => f.Properties["OBJECTID"] == 1882985);
			OddNote.Properties["LOC"] = ""; // from "address changed back to 1399 Bridgton Rd"
			var OddLandmark = features.First(f => f.Properties["OBJECTID"] == 1697489);
			if (((string)OddLandmark.Properties["LANDMARK"]).StartsWith("Westbrook Middle School"))
			{
				// from "Westbrook Middle School                           \t465\tWestbrook\t5240\t471 Stroudwater ST\t4092\t-70.34804893\t43.6639395"
				OddLandmark.Properties["LANDMARK"] = "Westbrook Middle School";
			}

		}

		public static Node Convert(Feature feature)
		{
			var props = feature.Properties;
			var streetName = Streets.BuildStreetName((string)props["PREDIR"],
				(string)props["STREETNAME"], (string)props["POSTDIR"], (string)props["SUFFIX"]);
			var unit = Unit((string)props["UNIT"]);
			var tags = new[]
			{
				new Tag("name", Name((string)props["LANDMARK"], (string)props["LOC"], (string)props["BUILDING"])),
				new Tag("addr:housenumber", ((int)props["ADDRESS_NUMBER"]).ToString()),
				new Tag("addr:unit", unit),
				new Tag("addr:street", streetName),
				new Tag("addr:city", (string)props["MUNICIPALITY"]),
				new Tag("addr:state", (string)props["STATE"]),
				new Tag("addr:postcode", (string)props["ZIPCODE"]),
				new Tag("level", Level((string)props["FLOOR"])),
				new Tag("maineE911id", ((int)props["OBJECTID"]).ToString())
			};

			var placeTags = GetPlaceTags((string)props["PLACE_TYPE"]);
			tags = tags.Concat(placeTags)
				.Select(t => new Tag(t.Key, t.Value.Trim()))
				.Where(t => t.Value != "")
				.ToArray();

			var n = new Node()
			{
				// Geometry lat-lon are higher precision than feature's lat-lon properties
				Latitude = (feature.Geometry as Point).Coordinates.Latitude,
				Longitude = (feature.Geometry as Point).Coordinates.Longitude,
				Tags = new TagsCollection(tags),
				Id = (int)props["OBJECTID"],
				Version = 1
			};

			return n;
		}

		// Keys which can be removed in order to combine congruent nodes
		private static string[] SacrificialKeys = new[] { "addr:unit", "level", "condo", "maineE911id"};

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
		public static Node[] HandleStacks(Node[] nodes)
		{
			List<Node> results = new List<Node>();
			Node[][] addresses = nodes.GroupBy(GetBaseAddress)
				.Select(g => g.ToArray())
				.ToArray();
			foreach (var address in addresses)
			{
				var nearDuplicateSets = GroupCloseNeighbors(address, 3);

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
							groupResult.Add(IntersectTags(mergable));
						}

						results.AddRange(RemoveLessSpecific(groupResult));
					}
				}
			}

			var stacks = results.GroupBy(n => new { n.Latitude, n.Longitude } )
				.Select(g => g.ToArray())
				.Where(s => s.Length > 1)
				.ToArray();
			foreach (var stack in stacks)
			{
				Nudge(stack, 0, .00001);
			}

			Log.LogInformation($"{nodes.Length - results.Count} nodes have been removed from {nodes.Length} (de-duped or combined)");

			return results.ToArray();
		}

		private static TagsCollectionBase GetBaseAddress(Node node)
		{
			var addressTags = new[] {"addr:housenumber", "addr:street", "addr:city", "addr:state" };
			return node.Tags.KeepKeysOf(addressTags);
		}

		private static Position AsLocation(Node node)
		{
			return new Position(node.Longitude.Value, node.Latitude.Value);
		}

		private static List<List<Node>> GroupCloseNeighbors(Node[] address, double closenessMeters)
		{
			var stacks = address.GroupBy(AsLocation)
				.Select(stack => new {
					positions = new List<Position> { stack.Key },
					nodes = stack.ToList() })
				.ToList();
			// Combine groups when everything in them is close to everything in another group.
			for (int i = 0; i < stacks.Count - 1; i++)
			{
				for (int j = i + 1; j < stacks.Count; j++)
				{
					var maybeMergeable = stacks[i].positions.SelectMany(l => stacks[j].positions, DistanceMeters)
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

		private static double DistanceMeters(Position left, Position right)
		{
			var averageLat = (left.Latitude + right.Latitude) / 2;
			var degPerRad = 180 / 3.14;
			var dLonKm = (left.Longitude - right.Longitude) * 111000 * Math.Cos(averageLat / degPerRad);
			var dLatKm = (left.Latitude - right.Latitude) * 110000;

			var distance = Math.Sqrt(dLatKm * dLatKm + dLonKm * dLonKm);
			return distance;
		}

		public static Node[] HandleDuplicates(Node[] nodes)
		{
			var asList = nodes.ToList();
			var duplicates = nodes.GroupBy(n => new { n.Tags })
				.Select(g => g.ToArray())
				.Where(stack => stack.Length > 1)
				.ToArray();
			foreach (var duplicate in duplicates)
			{
				// find radius of bounding circle
				//var distance = duplicate.
			}
			return null;
		}

		private static void Nudge(Node[] stack, double north, double east)
		{
			int i = 1;
			foreach (var node in stack.Skip(1))
			{
				node.Longitude += i * north;
				node.Latitude += i * east;
				i++;
				Log.LogDebug("Nudged node[{0}] to unstack", node.Id);
			}
		}

		private static Node IntersectTags(Node[] stack)
		{
			var ids = string.Join(";", stack.SelectMany(n => n.Tags).Where(t => t.Key == "maineE911id").Select(t => t.Value));
			var tags = stack[0].Tags.ToList();
			foreach (var node in stack.Skip(1))
			{
				tags = tags.Intersect(node.Tags).ToList();
			}
			tags.Add(new Tag("maineE911id", ids));
			stack[0].Tags = new TagsCollection(tags);

			var levels = stack.SelectMany(n => n.Tags)
				.Where(t => t.Key == "level")
				.Select(t =>
				{
					int.TryParse(t.Value, out int level);
					return level;
				}).DefaultIfEmpty().Max();

			if (levels >= 1)
			{
				stack[0].Tags["building:levels"] = levels.ToString();
				stack[0].Tags.RemoveKey("level");
			}
			
			stack[0].Latitude = stack.Average(n => n.Latitude);
			stack[0].Longitude = stack.Average(n => n.Longitude);
			Log.LogInformation("intersected " + string.Join(",", stack.Select(n => n.Id)));

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
			for(int i = 0; i < stack.Count; i++)
			{
				var node = stack[i];
				string building = null;
				var bigger = stack.FirstOrDefault(other => node != other
					&& node.Tags.All(t => other.Tags.Contains(t)
						|| t.Key == "maineE911id"
						|| (t.Key == "building" && OtherBuildingIsMoreGeneral(t.Value, other, out building))));
				if (bigger != null)
				{
					if (building != null) bigger.Tags["building"] = building;
					bigger.Tags["maineE911id"] += ";" + node.Tags["maineE911id"];
					stack.RemoveAt(i);
					i--;
				}
			}

			return stack;
		}

		// [building: apartments, detached, duplex => residential]
		private static string[] ResidentialBuildings = new string[]
		{ "apartments", "detached", "duplex", "residential" };
		private static bool OtherBuildingIsMoreGeneral(string myBuilding, Node other, out string newBuilding)
		{
			if (other.Tags.TryGetValue("building", out string otherBuilding)
				&& ResidentialBuildings.Contains(myBuilding)
				&& ResidentialBuildings.Contains(otherBuilding))
			{
				newBuilding = "residential";
				return true;
			}

			newBuilding = null;
			return false;
		}

		public static void Validate(Feature[] features)
		{
			int[] duplicateObjectIds = features.Select(f => (int)f.Properties["OBJECTID"]).GroupBy(x => x).Where(x => x.Skip(1).Any()).Select(x => x.Key).ToArray();
			if (duplicateObjectIds.Any())
			{
				Log.LogError("ObjectIDs aren't unique");
			}

			foreach (var f in features)
			{
				if (!Streets.SuffixExpansions.ContainsKey(f.Properties["SUFFIX"]))
				{
					Log.LogError("Bad SUFFIX");
				}
				if (!Streets.DirectionExpansions.ContainsKey(f.Properties["PREDIR"]))
				{
					Log.LogError("Bad PREDIR");
				}
				if (!Streets.DirectionExpansions.ContainsKey(f.Properties["POSTDIR"]))
				{
					Log.LogError("Bad POSTDIR");
				}
				if (f.Properties["ELEVATION"] != 0)
				{
					Log.LogError("Bad ELEVATION");
				}
				var goodFloor = ((string)f.Properties["FLOOR"]).Split().All(part =>
					part.Equals("floor", StringComparison.OrdinalIgnoreCase)
					|| part.Equals("flr", StringComparison.OrdinalIgnoreCase)
					|| part.All(char.IsNumber));
				if (!goodFloor)
				{
					Log.LogError("Bad Floor");
				}
				var goodBuilding = ((string)f.Properties["BUILDING"]).All(char.IsNumber);
				if (!((string)f.Properties["BUILDING"]).All(char.IsNumber)
					&& !((string)f.Properties["BUILDING"]).StartsWith("Bldg", StringComparison.OrdinalIgnoreCase))
				{
					Log.LogError("Bad bulding");
				}
				if (f.Properties["ROOM"] != "")
				{
					Log.LogError("Bad Room");
				}
				if (f.Properties["SEAT"] != "")
				{
					Log.LogError("Bad Room");
				}
				if (((string)f.Properties["ZIPCODE"]).Count(char.IsNumber) != 5)
				{
					Log.LogError("Bad Zipcode");
				}
			}
		}

		public static void Validate(Node[] nodes)
		{
			if (nodes.Length > 10000) Log.LogWarning($"{nodes.Length} Nodes is too big for one changest");

			var duplicates = nodes.GroupBy(n => new { n.Tags })
				.Select(g => g.ToArray())
				.Where(s => s.Length > 1)
				.ToArray();

			if (duplicates.Any())
			{
				Log.LogWarning(duplicates.Length + " Duplicate Node Tag sets (different lat-lon) have been detected and not corrected.");
			}

			foreach (var node in nodes)
			{
				//if (node.Tags["addr:postcode"] != "04092") Log.LogError(node.Tags["addr:postcode"]);
				if (node.Tags["addr:city"] != "Westbrook") Log.LogError("Nope");
				if (node.Tags["addr:state"] != "ME") Log.LogError("Nope");
				if (!node.Tags["addr:street"].All(c => char.IsLetter(c) || c == ' ')) Log.LogError("Nope");
				if (!node.Tags["addr:housenumber"].All(char.IsNumber)) Log.LogError("Nope");
				if (node.Latitude == 0 || node.Latitude == null) Log.LogError("Nope");
				if (node.Longitude == 0 || node.Longitude == null) Log.LogError("Nope");
			}
		}

		public static IEnumerable<Tag> GetPlaceTags(string placeType)
		{
			if (placeType == "") return new Tag[0]; //6398
			else if (placeType == "Apartment") return new[] { new Tag("building", "apartments") }; //2696
			else if (placeType == "Commercial") return new Tag[0]; //284
			//else if (placeType == "Commercial") return new[] { new Tag("shop", "yes") }; //284
			else if (placeType == "Other") return new Tag[0]; //282
			else if (placeType == "Residential") return new[] { new Tag("building", "residential") }; //119
			else if (placeType == "Single Family") return new[] { new Tag("building", "detached") }; //77
			else if (placeType == "Condominium") return new[] { new Tag("building", "apartments"), new Tag("condo", "yes") }; //57
			else if (placeType == "Multi Family") return new[] { new Tag("building", "apartments") }; //23
			else if (placeType == "Mobile Home") return new[] { new Tag("building", "static_caravan") }; //18
			else if (placeType == "Duplex") return new[] { new Tag("building", "duplex") }; //15
			else if (placeType == "Government - Municipal") return new[] { new Tag("office", "government") }; //5
			else if (placeType == "School") return new[] { new Tag("amenity", "school") }; //5
			else if (placeType == "Utility") return new Tag[0]; //5
			else if (placeType == "Parking") return new[] { new Tag("amenity", "parking") }; //4
			else if (placeType == "Attraction") return new[] { new Tag("tourism", "attraction") }; //2
			else if (placeType == "EMS Station") return new[] { new Tag("emergency", "ambulance_station") }; //2
			else if (placeType == "Fire Station") return new[] { new Tag("amenity", "fire_station") };//2
			else if (placeType == "Hospital") return new[] { new Tag("amenity", "hospital") }; //2
			else if (placeType == "Nursing Home") return new[] { new Tag("amenity", "nursing_home") }; //2
			else if (placeType == "Health Care") return new[] { new Tag("amenity", "healthcare") }; //1  
			else if (placeType == "Law Enforcement -Municipal") return new[] { new Tag("amenity", "police") }; //1
			else if (placeType == "Office") return new[] { new Tag("office", "yes") }; //1
			else if (placeType == "PSAP") return new[] { new Tag("emergency", "psap") }; //1
			else if (placeType == "Retail - Enclosed Mall") return new[] { new Tag("shop", "mall") }; //1
			else if (placeType == "Vacant Lot") return new[] { new Tag("vacant", "yes") }; //1
			else
			{
				Log.LogError("Unrecognized PLACE_TYPE '{0}'", placeType);
				throw new ArgumentOutOfRangeException("PLACE_TYPE=" + placeType);
			}
		}

		public static string Name(string landmark, string loc, string building)
		{
			if (building.Any() && building.All(char.IsNumber))
			{
				building = "BLDG " + building;
			}

			landmark = landmark + ' ' + building + ' ' + loc;
			landmark = string.Join(' ', landmark.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(part => UnitExpansion.TryGetValue(part, out string replacement) ? replacement : part));

			return landmark;
		}

		// "FLR 1", "Floor 1", "1"
		public static string Level(string floor)
		{
			return new string(floor.Where(char.IsNumber).ToArray());
		}

		// unit is: [prefix/pre-breviation] [AlphaNumeric ID]
		public static string Unit(string unit)
		{
			var parts = unit.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			parts = parts.Select(part => UnitExpansion.TryGetValue(part, out string replacement) ? replacement : part).ToArray();

			return string.Join(' ',  parts);
		}

		// https://pe.usps.com/text/pub28/28apc_003.htm
		public static Dictionary<string, string> UnitAbbreviation =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{ {"Apartment","APT"},
			{"Basement","BSMT"},
			{"",""},
			{"Building","BLDG"},
			{"Department","DEPT"},
			{"Floor","FL"},
			{"Front","FRNT"},
			{"Hanger","HNGR"},
			{"Key","KEY"},
			{"Lobby","LBBY"},
			{"Lot","LOT"},
			{"Lower","LOWR"},
			{"Office","OFC"},
			{"Penthouse","PH"},
			{"Pier","PIER"},
			{"Rear","REAR"},
			{"Room","RM"},
			{"Side","SIDE"},
			{"Slip","SLIP"},
			{"Space","SPC"},
			{"Stop","STOP"},
			{"Suite","STE"},
			{"Trailer","TRLR"},
			{"Unit","UNIT"},
			{"Upper","UPPR"} };

		public static Dictionary<string, string> UnitExpansion =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{"&","and"},
				{"APT","Apartment"},
				{"Apartment","Apartment"},
				{"BSMT","Basement"},
				{"Basement","Basement"},
				{"BLDG","Building"},
				{"Building","Building"},
				{"DEPT","Department"},
				{"Department","Department"},
				{"FL","Floor"},
				{"Floor","Floor"},
				{"FRNT","Front"},
				{"Front","Front"},
				{"HNGR","Hanger"},
				{"Hanger","Hanger"},
				{"KEY","Key"},
				{"LBBY","Lobby"},
				{"Lobby","Lobby"},
				{"LOT","Lot"},
				{"LOWR","Lower"},
				{"Lower","Lower"},
				{"OFC","Office"},
				{"Office","Office"},
				{"PH","Penthouse"},
				{"Penthouse","Penthouse"},
				{"PIER","Pier"},
				{"REAR","Rear"},
				{"RM","Room"}, // Consider tagging as "room="
				{"Room","Room"},
				{"SIDE","Side"},
				{"SLIP","Slip"},
				{"SPC","Space"},
				{"Space","Space"},
				{"STOP","Stop"},
				{"STE","Suite"},
				{"Suite","Suite"},
				{"TRLR","Trailer"},
				{"Trailer","Trailer"},
				{"UNIT","Unit"},
				{"UPPR","Upper"},
				{"Upper","Upper"}
			};
	}
}
