using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OsmSharp;
using System;
using System.Linq;
using System.IO;
using BAMCIS.GeoJSON;
using OsmSharp.Tags;
using OsmPipeline.Fittings;

namespace OsmPipeline
{
	public static class Addresses
	{
		// New address
		// Similar Address node exists
		// Similar Address way exists
		// 

		public static void ValidateAddresses(ILoggerFactory loggerFactory, IConfigurationRoot config, OsmGeo scope)
		{
			string json = File.ReadAllText(@"C:\Users\Alex\Desktop\WestbrookAddressesWithNoNull.geojson");
			FeatureCollection collection = FeatureCollection.FromJson(json);
			Validate(collection.Features.ToArray());
			var nodes = collection.Features.Select(Convert).ToArray();
			Validate(nodes);

			// fetch region from osm (or overpass?)
			// conflate
			// upload

			//var osmApiEnder = new OsmApiEnder(logger, OsmApiUrl, OsmUsername, OsmPassword, changeTags);
			//var LoggingEnder = new Fittings.LoggingEnder(loggerFactory.CreateLogger("kml"));
			//var change = Edit(osm, EditGenerator, EditVersion).Result;
		}

		public static Node Convert(Feature feature)
		{
			var props = feature.Properties;
			var streetName = Streets.BuildStreetName(props["PREDIR"], props["STREETNAME"], props["POSTDIR"], props["SUFFIX"]);
			var tags = new[]
			{
				new Tag("addr:street", streetName),
				new Tag("addr:housenumber", ""+props["ADDRESS_NUMBER"]),
				new Tag("addr:city", ""+props["MUNICIPALITY"]),
				new Tag("addr:state", ""+props["STATE"]),
				new Tag("addr:postcode", "" + props["ZIPCODE"]),
				new Tag("addr:unit", "" + props["UNIT"])
			}.Where(t => t.Value != "");

			var n = new Node()
			{
				Latitude = props["Latitude"],
				Longitude = props["Longitude"],
				Tags = new TagsCollection(tags)
			};

			return n;
		}

		public static void Validate(Feature[] features)
		{
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
				string shouldBeEmpty = "" + f.Properties["BUILDING"] + f.Properties["FLOOR"] + f.Properties["UNIT"] + f.Properties["ROOM"]
					+ f.Properties["SEAT"] + f.Properties["LANDMARK"] + f.Properties["LOC"] + f.Properties["PLACE_TYPE"];
				if (shouldBeEmpty != "")
				{
					Console.WriteLine(shouldBeEmpty);
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
				if (node.Tags["addr:street"].Any(char.IsLetter)) Console.WriteLine("Nope");
				if (node.Tags["addr:housenumber"].Any(char.IsNumber)) Console.WriteLine("Nope");
				if (node.Latitude == 0 || node.Latitude == null) Console.WriteLine("Nope");
				if (node.Longitude == 0 || node.Longitude == null) Console.WriteLine("Nope");
			}
		}
	}
}
