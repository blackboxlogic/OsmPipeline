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
		// Stacked points? Spread them .00001 east, or merge them and remove the unit numbers
		// What to do with Loc?
		// Where should building go? unit=building 3, ref=3, name=Building 3, building=3
		// use geo lat lon, or feature property lat lon

		// New address
		// Similar Address node exists
		// Similar Address way exists

		// Spit it out as geojson and then review it and commit it in iD?
		public static void ValidateAddresses(ILoggerFactory loggerFactory, IConfigurationRoot config, OsmGeo scope)
		{
			var collection = CachedGeoJsonAPISource.FromFileOrFetch("Westbrook");
			var features = collection.Features.ToArray();

			Validate(features);
			var nodes = features.Select(Convert).ToArray();
			DeDupe(nodes);
			Validate(nodes);
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

		public static void ApplyCorrections(Feature[] features)
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
				new Tag("addr:street", streetName),
				new Tag("addr:housenumber", ((int)props["ADDRESS_NUMBER"]).ToString()),
				new Tag("addr:city", (string)props["MUNICIPALITY"]),
				new Tag("addr:state", (string)props["STATE"]),
				new Tag("addr:postcode", (string)props["ZIPCODE"]),
				new Tag("addr:unit", unit),
				new Tag("name", Name((string)props["LANDMARK"], (string)props["LOC"], (string)props["BUILDING"])),
				new Tag("level", Level((string)props["FLOOR"]))
			};

			var placeTags = GetPlaceTags((string)props["PLACE_TYPE"]);
			tags = tags.Concat(placeTags)
				.Select(t => new Tag(t.Key, t.Value.Trim()))
				.Where(t => t.Value != "")
				.ToArray();

			var n = new Node()
			{
				Latitude = props["Latitude"], //((BAMCIS.GeoJSON.Point)feature.Geometry).Coordinates.Latitude
				Longitude = props["Longitude"], //((BAMCIS.GeoJSON.Point)feature.Geometry).Coordinates.Longitude
				Tags = new TagsCollection(tags)
			};

			return n;
		}

		// stacked nodes will be spread Eastward.
		public static void DeDupe(Node[] nodes)
		{
			List<Node> results = new List<Node>();
			//double distance = .00001;
			Node[][] groups = nodes.GroupBy(n => new { n.Latitude, n.Longitude }).Select(g => g.ToArray()).ToArray();
			foreach (var group in groups)
			{
				if (group.Length == 1) // there's only one
				{
					results.Add(group[0]);
				}
				else if (group.Select(g => g.Tags).Distinct().Count() == 1) // they're all the same
				{
					results.Add(group[0]);
				}
				else if (TryFindSafeSuperSet(group, out var safeSuperSet)) // is this what I really want?
				{
					results.Add(safeSuperSet);
				}
				else // or spread them?
				{
					results.Add(IntersectTags(group)); // Is this really what I want?
				}
			}
		}

		private static Node IntersectTags(Node[] group) // SHOULD BE COMMON TO ALL?
		{
			var first = group[0];
			var allTags = group.SelectMany(n => n.Tags)
				.GroupBy(t => t.Key)
				.ToDictionary(t => t.Key, ts => ts.Select(t => t.Value).Distinct().ToArray());
			var newTags = allTags.Where(kvp => kvp.Value.Length == 1)
				.Select(kvp => new Tag(kvp.Key, kvp.Value[0]))
				.ToArray();
			var droppedTags = allTags.Where(kvp => kvp.Value.Length > 1
					&& kvp.Key != "addr:unit"
					&& kvp.Key != "addr:level")
				.ToArray();
			if (droppedTags.Any())
			{
				Console.WriteLine(string.Join("\n\t", droppedTags.Select(t => t.Key + "=" + string.Join(", ", t.Value))));
			}
			first.Tags = new TagsCollection(newTags);
			return first;
		}

		private static bool TryFindSafeSuperSet(Node[] group, out Node safeSuperSet)
		{
			var biggest = group.OrderByDescending(n => n.Tags.Count).First();
			bool found = group.All(n => biggest.Tags.All(t => n.Tags.Contains(t)));
			safeSuperSet = found ? biggest : null;
			return found;
		}

		public static void Validate(Feature[] features)
		{
			int[] duplicateObjectIds = features.Select(f => (int)f.Properties["OBJECTID"]).GroupBy(x => x).Where(x => x.Skip(1).Any()).Select(x => x.Key).ToArray();
			if (duplicateObjectIds.Any())
			{
				Console.WriteLine("ObjectIDs aren't unique");
			}

			foreach (var f in features)
			{
				if (!Streets.SuffixExpansions.ContainsKey(f.Properties["SUFFIX"]))
				{
					Console.WriteLine("Bad SUFFIX");
				}
				if (!Streets.DirectionExpansions.ContainsKey(f.Properties["PREDIR"]))
				{
					Console.WriteLine("Bad PREDIR");
				}
				if (!Streets.DirectionExpansions.ContainsKey(f.Properties["POSTDIR"]))
				{
					Console.WriteLine("Bad POSTDIR");
				}
				if (f.Properties["ELEVATION"] != 0)
				{
					Console.WriteLine("Bad ELEVATION");
				}
				var goodFloor = ((string)f.Properties["FLOOR"]).Split().All(part =>
					part.Equals("floor", StringComparison.OrdinalIgnoreCase)
					|| part.Equals("flr", StringComparison.OrdinalIgnoreCase)
					|| part.All(char.IsNumber));
				if (!goodFloor)
				{
					Console.WriteLine("Bad Floor");
				}
				var goodBuilding = ((string)f.Properties["BUILDING"]).All(char.IsNumber);
				if (!((string)f.Properties["BUILDING"]).All(char.IsNumber)
					&& !((string)f.Properties["BUILDING"]).StartsWith("Bldg", StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine("Bad bulding");
				}
				if (f.Properties["ROOM"] != "")
				{
					Console.WriteLine("Bad Room");
				}
				if (f.Properties["SEAT"] != "")
				{
					Console.WriteLine("Bad Room");
				}
			}
		}

		public static void Validate(Node[] nodes)
		{
			foreach (var node in nodes)
			{
				if (node.Tags["addr:postcode"] != "04092") Console.WriteLine(node.Tags["addr:postcode"]);
				if (node.Tags["addr:city"] != "Westbrook") Console.WriteLine("Nope");
				if (node.Tags["addr:state"] != "ME") Console.WriteLine("Nope");
				if (!node.Tags["addr:street"].All(c => char.IsLetter(c) || c == ' ')) Console.WriteLine("Nope");
				if (!node.Tags["addr:housenumber"].All(char.IsNumber)) Console.WriteLine("Nope");
				if (node.Latitude == 0 || node.Latitude == null) Console.WriteLine("Nope");
				if (node.Longitude == 0 || node.Longitude == null) Console.WriteLine("Nope");
			}
		}

		public static IEnumerable<Tag> GetPlaceTags(string placeType)
		{
			if (placeType == "") { } //6398
			else if (placeType == "Apartment") { } //2696
			else if (placeType == "Commercial") { } //284
			else if (placeType == "Other") { } //282
			else if (placeType == "Residential") { } //119
			else if (placeType == "Single Family") { } //77
			else if (placeType == "Condominium") { } //57
			else if (placeType == "Multi Family") { } //23
			else if (placeType == "Mobile Home") { } //18
			else if (placeType == "Duplex") { } //15
			else if (placeType == "Government - Municipal") { } //5
			else if (placeType == "School") { } //5
			else if (placeType == "Utility") { } //5
			else if (placeType == "Parking") { } //4
			else if (placeType == "Attraction") { } //2
			else if (placeType == "EMS Station") { } //2
			else if (placeType == "Fire Station") return new[] { new Tag("amenity", "fire_station") }; //2
			else if (placeType == "Hospital") { } //2
			else if (placeType == "Nursing Home") { } //2
			else if (placeType == "Health Care") { } //1
			else if (placeType == "Law Enforcement -Municipal") { } //1
			else if (placeType == "Office") { } //1
			else if (placeType == "PSAP") { } //1
			else if (placeType == "Retail - Enclosed Mall") { } //1
			else if (placeType == "Vacant Lot") { } //1
			else throw new Exception();
			return new Tag[0];
		}

		public static string Name(string landmark, string loc, string building)
		{
			if (building.Any() && building.All(char.IsNumber))
			{
				building = "BLDG " + building;
			}

			landmark = landmark + ' ' + building;

			landmark = string.Join(' ', landmark.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(part => UnitExpansion.TryGetValue(part, out string replacement) ? replacement : part));
			loc = loc.Trim();
			if (landmark == "") return loc;
			if (loc != "") return landmark + " - " + loc;
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
