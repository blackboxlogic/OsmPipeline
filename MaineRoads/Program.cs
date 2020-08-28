using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NetTopologySuite.Geometries;
using Newtonsoft.Json;
using OsmPipeline.Fittings;
using OsmSharp.API;
using OsmSharp.Complete;
using OsmSharp.Geo;
using OsmSharp.Streams;
using OsmSharp.IO.API;
using System.Net.Http;

namespace MaineRoads
{
	public static partial class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
	}

	class Program
	{
		static void Main(string[] args)
		{
			Static.Config = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", false, true)
				.Build();
			Environment.CurrentDirectory += Static.Config["WorkingDirectory"];
			IServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddLogging(builder => builder.AddConsole().AddFilter(level => true));
			Static.LogFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();

			try
			{
				var counties = JsonConvert.DeserializeObject<Dictionary<string, long>>(File.ReadAllText("MaineCounties.json"));
				foreach (var county in counties)
				{
					Go(county.Key, county.Value);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			Console.ReadKey(true);
		}

		private static void Go(string scopeName, long relationId)
		{
			if (!Directory.Exists(scopeName))
			{
				Directory.CreateDirectory(scopeName);
			}
			//NetTopologySuite.Algorithm.Match.HausdorffSimilarityMeasure
			//Geometry.DistanceMeters(
			//	new NetTopologySuite.Geometries.Coordinate(scope.MinLongitude.Value, scope.MinLatitude.Value),
			//	new NetTopologySuite.Geometries.Coordinate(scope.MaxLongitude.Value, scope.MaxLatitude.Value));

			var scope = scopeName == null ? null : LoadBounds(scopeName, relationId);
			var reference = Reference.Generate("ReferenceRawSimplified.osm", $"{scopeName}\\Reference.osm", scope);
			Osm subject;
			using (var stream = File.OpenRead("Subject.pbf"))
			{
				subject = new PBFOsmStreamSource(stream).AsOsm();
			}

			var missingPublic = Conflate.FindMissingRoads(subject, reference.Where(w => !w.Tags.Contains("ownership", "private")).ToArray());
			FileSerializer.WriteXml($"{scopeName}\\MissingPublic.osm", missingPublic);
			JOSM.MarkAsNeverUpload($"{scopeName}\\MissingPublic.osm");

			var missingPrivate = Conflate.FindMissingRoads(subject, reference.Where(w => w.Tags.Contains("ownership", "private")).ToArray());
			FileSerializer.WriteXml($"{scopeName}\\MissingPrivate.osm", missingPrivate);
			JOSM.MarkAsNeverUpload($"{scopeName}\\MissingPrivate.osm");

			//int i = 0;
			//var referenceSlices = BoundsExtentions.SliceRecusive(reference, 200);

			//foreach (var referenceSlice in referenceSlices)
			//{
			//	var directory = i.ToString("0000");
			//	Directory.CreateDirectory(directory);
			//	FileSerializer.WriteXml($"{directory}\\Reference.osm", referenceSlice.Value.AsOsm());
			//	JOSM.MarkAsNeverUpload($"{directory}\\Reference.osm");

			//	//var subjectSlice = subject.FilterBox(bounds.MinLongitude.Value, bounds.MaxLatitude.Value,
			//	//		bounds.MaxLongitude.Value, bounds.MinLatitude.Value, true)
			//	//	.AsOsm();
			//	//FileSerializer.WriteXml($"{i}\\Subject.osm", subjectSlice);
			//	//var missing = Conflate.FindMissingPublicRoads(subjectSlice, referenceSlice);
			//	//FileSerializer.WriteXml($"{i}\\MissingPublicRoads.osm", missing);
			//	//var session = File.ReadAllText("Session.jos");
			//	// Transform session
			//	//File.WriteAllText($"{i}\\Session.jos", session);
			//	i++;
			//}
		}

		private static NetTopologySuite.Geometries.Geometry LoadBounds(string scopeName, long relationId)
		{
			var scopeFile = $"{scopeName}\\Scope.osm";
			var osm = FileSerializer.ReadXml<Osm>(scopeFile);
			CompleteRelation relation;
			if (osm != null)
			{
				relation = osm.GetElements().ToComplete().OfType<CompleteRelation>().First();
			}
			else
			{
				NonAuthClient client = new NonAuthClient("https://www.osm.org/api/", new HttpClient());
				relation = client.GetCompleteRelation(relationId).Result;
				FileSerializer.WriteXml(scopeFile, relation.ToSimpleWithChildren().AsOsm());
			}

			var geometry = FeatureInterpreter.DefaultInterpreter.Interpret(relation).First().Geometry;
			if (geometry is LinearRing linRing)
			{
				var polygon = Polygon.DefaultFactory.CreatePolygon(linRing); // Or multipolygon?
				return polygon;
			}
			else if (geometry is MultiPolygon multi)
			{
				return multi;
				//Polygon.DefaultFactory.CreateMultiPolygon();
			}

			throw new Exception("unexpected geometry type: " + geometry.GetType().Name);
		}
	}
}
