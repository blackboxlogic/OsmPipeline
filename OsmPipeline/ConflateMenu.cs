using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
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

	// Should match by geometry move a node?
	public class ConflateMenu
	{
		Func<string, string, bool> Is = (a,b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
		private string Municipality;

		public void Main()
		{
			Static.Municipalities = FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GetMunicipalities).Result;
			Console.WriteLine(Static.Municipalities.Values.Count(m => m.ChangeSetIds.Any()) +" of " + Static.Municipalities.Count);
			Municipality = Static.Municipalities.Values.First(m => !m.ChangeSetIds.Any()).Name;
			Console.WriteLine("Starting in " + Municipality);

			while (true)
			{
				Console.Write("> ");
				var userInput = Console.ReadLine();

				if (Is(userInput, "reference"))
				{
					var Reference = References.Fetch(Municipality).Result;
					FileSerializer.WriteXml(Municipality + "/Reference.osm", Reference);
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "subject"))
				{
					var Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					var Subject = Subjects.GetElementsInBoundingBox(Reference.Bounds).Result;
					FileSerializer.WriteXml(Municipality + "/Subject.osm", Subject);
					File.Delete(Municipality + "/Conflated.osc");
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
					// Last item will appear top of JOSM.
					var args = string.Join(" ", $"\"{Municipality}/Subject.osm\"",
						$"\"{Municipality}/Conflated.Review.osm\"",
						$"\"{Municipality}/Conflated.Create.osm\"",
						//$"\"{Municipality}/Conflated.Delete.osm\"",
						$"\"{Municipality}/Conflated.Modify.osm\"");
					System.Diagnostics.Process.Start(Static.Config["JosmPath"], args);
				}
				else if (Is(userInput, "note"))
				{
					Static.Municipalities[Municipality].Notes += "/n" + userInput.Split(' ', 2)[1];
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
				}
				else if (Is(userInput, "WhiteAll"))
				{
					var review = FileSerializer.ReadXml<Osm>(Municipality + "/Conflated.Review.osm");
					var selection = review.OsmToGeos()
						.Where(e => e.Tags != null && e.Tags.ContainsKey(Static.maineE911id))
						.Select(e => e.Tags[Static.maineE911id])
						.SelectMany(id => id.Split(new char[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries))
						.Select(long.Parse)
						.Except(Static.Municipalities[Municipality].WhiteList)
						.ToArray();
					Static.Municipalities[Municipality].WhiteList.AddRange(selection);

					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Conflated.osc");
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
					File.Delete(Municipality + "/Conflated.osc");
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
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "commit"))
				{
					var Change = FileSerializer.ReadXml<OsmChange>(Municipality + "/Conflated.osc");
					var results = Subjects.UploadChange(Change, Municipality).Result;
					Static.Municipalities[Municipality].ChangeSetIds.AddRange(results);
					Static.Municipalities[Municipality].ImportDate = DateTime.UtcNow;
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					Console.WriteLine("Finished!");
				}
				else if (Is(userInput, "next"))
				{
					Municipality = Static.Municipalities.Values.First(m => !m.ChangeSetIds.Any()).Name;
					Console.Clear();
					Console.WriteLine("Switching to " + Municipality);
				}
				else if (Is(userInput, "switch"))
				{
					Municipality = ChooseMunicipality();
				}
				else if (Is(userInput, "help"))
				{
					Console.WriteLine("Options:");
					Console.WriteLine("\tSwitch");
					Console.WriteLine("\tNext");
					Console.WriteLine("\tReference");
					Console.WriteLine("\tSubject");
					Console.WriteLine("\tConflate");
					Console.WriteLine("\tReview");
					Console.WriteLine("\tNote [message]");
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
					if (Static.Municipalities[Municipality].ChangeSetIds.Any())
					{
						Console.WriteLine($"WARNING: {Municipality} already has changesets!");
					}
					return match;
				}
				var selection = Static.Municipalities.Keys.Where(m => m.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
				if (selection.Length == 1)
				{
					Console.WriteLine("Switching to " + selection[0]);
					if (Static.Municipalities[Municipality].ChangeSetIds.Any())
					{
						Console.WriteLine($"WARNING: {Municipality} already has changesets!");
					}
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
