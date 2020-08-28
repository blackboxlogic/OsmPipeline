using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using NetTopologySuite.Features;
using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Complete;
using OsmSharp.Db;
using OsmSharp.Db.Impl;
using OsmSharp.Geo;
using OsmSharp.Streams;

namespace MaineRoads
{
	public static class Reference
	{
		public static CompleteWay[] Generate(string from, string to, NetTopologySuite.Geometries.Geometry scope = null)
		{
			var osm = FileSerializer.ReadXmlCacheOrSource(to,
				() => TranslateAndDisolve(from, scope));
			JOSM.MarkAsNeverUpload(to);
			return osm.GetElements().ToComplete().OfType<CompleteWay>().ToArray();
		}

		private static Osm TranslateAndDisolve(string from, NetTopologySuite.Geometries.Geometry scope = null)
		{
			var osm = FileSerializer.ReadXml<Osm>(from);
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm.GetElements()));
			var translated = Translate(osm);
			ReverseOneWays(translated);
			var disolved = Geometry.Disolve(translated, "medot:objectid");
			var scoped = disolved.Where(e => scope == null || FeatureInterpreter.DefaultInterpreter.Interpret(e).First().Geometry.Intersects(scope)).ToArray();

			// Hide the object UIDs in User, away from the tags.
			foreach (var e in scoped)
			{
				e.UserName = e.Tags["medot:objectid"];
				e.Tags.RemoveKey("medot:objectid");
			}

			return scoped.AsOsm(index);
		}

		private static void ReverseOneWays(CompleteWay[] ways)
		{
			foreach (var way in ways.Where(w => w.Tags.Contains("oneway", "-1")))
			{
				way.Nodes = way.Nodes.Reverse().ToArray();
				way.Tags["oneway"] = "yes";
			}
		}

		private static Osm AsOsm(this IEnumerable<CompleteOsmGeo> elements, IOsmGeoSource index)
		{
			return elements.Select(e => e.ToSimple()).WithChildren(index).AsOsm();
		}

		private static CompleteWay[] Translate(Osm osm)
		{
			using (var translator = new OsmTagsTranslator.Translator(osm.GetElements()))
			{
				translator.AddLookup(@"Directions.json");
				translator.AddLookup(@"StreetSuffixes.json");
				translator.AddLookup(@"RoadClasses.json");
				translator.AddLookup(@"RoutePrefixes.json");
				var sql = File.ReadAllText(@"E911RoadsToOsmSchema.sql");
				return translator.QueryElementsWithChildren(sql)
					.ToComplete().OfType<CompleteWay>().ToArray();
			};
		}
	}
}
