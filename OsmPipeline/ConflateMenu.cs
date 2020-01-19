using OsmPipeline.Fittings;
using OsmSharp.Changesets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using static OsmPipeline.Fittings.GeoJsonAPISource;

namespace OsmPipeline
{
	public static partial class Static
	{
		public const string maineE911id = "maineE911id";
		public static Dictionary<string, Municipality> Municipalities;
		public static HttpClient HttpClient = new HttpClient();
	}

	// Test: Split subject fetch and split change commit
	// Fast subject fetches might need to be slower to conform to API rules
	public class ConflateMenu
	{
		Func<string, string, bool> Is = (a,b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
		private string Municipality;

		public void Main()
		{
			Static.Municipalities = FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GetMunicipalities).Result;
			Municipality = ChooseMunicipality();

			while (true)
			{
				Console.Write("> ");
				var userInput = Console.ReadLine();

				if (Is(userInput, "reference"))
				{
					var Reference = References.Fetch(Municipality).Result;
					FileSerializer.WriteXml(Municipality + "/Reference.osm", Reference);
				}
				else if (Is(userInput, "subject"))
				{
					var Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					var Subject = Subjects.GetElementsInBoundingBox(Reference.Bounds).Result;
					FileSerializer.WriteXml(Municipality + "/Subject.osm", Subject);
				}
				else if (Is(userInput, "conflate"))
				{
					var Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					var Subject = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Subject.osm",
						() => Subjects.GetElementsInBoundingBox(Reference.Bounds)).Result;
					var Change = Conflate.Merge(Reference, Subject, Municipality);
					FileSerializer.WriteXml(Municipality + "/Conflated.osc", Change);
				}
				else if (Is(userInput, "review"))
				{
					// Open JOSM with review layers
				}
				else if (Is(userInput, "white"))
				{
					var selection = userInput.Split(' ', 2)[1]
						.Split(new char[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries)
						.Where(c => long.TryParse(c, out _))
						.Select(long.Parse)
						.Except(Static.Municipalities[Municipality].WhiteList)
						.ToArray();
					Static.Municipalities[Municipality].WhiteList.AddRange(selection);

					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
				}
				else if (Is(userInput, "black"))
				{
					var selection = userInput.Split(' ', 2)[1]
						.Split(new char[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries)
						.Where(c => long.TryParse(c, out _))
						.Select(long.Parse)
						.Except(Static.Municipalities[Municipality].BlackList)
						.ToArray();
					Static.Municipalities[Municipality].BlackList.AddRange(selection);
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Reference.osm");
				}
				else if (Is(userInput, "commit"))
				{
					// This is commented out so I don't accidentally commit changes to OSM.
					var Change = FileSerializer.ReadXml<OsmChange>(Municipality + "/Conflated.osc");
					//var results = Subjects.UploadChange(Change, Municipality).Result;
					//Static.Municipalities[Municipality].ChangeSetIds.AddRange(results);
					//Static.Municipalities[Municipality].ImportDate = DateTime.UtcNow;
					//FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					//Console.WriteLine("Finished!");
				}
				else if (Is(userInput, "switch"))
				{
					ChooseMunicipality();
				}
				else if (Is(userInput, "help"))
				{
					Console.WriteLine("Options:");
					Console.WriteLine("\tSwitch");
					Console.WriteLine("\tReference");
					Console.WriteLine("\tSubject");
					Console.WriteLine("\tConflate");
					Console.WriteLine("\tReview");
					Console.WriteLine("\tWhitelist [###]<,[###]...>");
					Console.WriteLine("\tBlacklist [###]<,[###]...>");
					Console.WriteLine("\tCommit");
				}
				else Console.WriteLine("What?");

				Console.WriteLine("Done");
			}
		}

		private string ChooseMunicipality()
		{
			do
			{
				Console.WriteLine($"Which municipality?");
				var input = Console.ReadLine();
				var match = Static.Municipalities.Keys.FirstOrDefault(s => s.Equals(input, StringComparison.OrdinalIgnoreCase));
				if (match != null)
				{
					Console.WriteLine("Switching to " + match);
					return match;
				}
				var selection = Static.Municipalities.Keys.Where(m => m.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
				if (selection.Length == 1)
				{
					Console.WriteLine("Switching to " + selection[0]);
					return selection[0];
				}
				else
				{
					Console.WriteLine(string.Join("\n", selection));
				}
			} while (true);
		}
	}
}
