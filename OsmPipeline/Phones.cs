using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using static OsmPipeline.Fittings.OverpassQuery;

namespace OsmPipeline
{
	public static class Phones
	{
		public static string Correct = @"^\\+1\\-[0-9]{3}\\-[0-9]{3}\\-[0-9]{4}$";
		public static string ElevenStartsWithOne = "^([^0-9]*1)([^0-9]*[0-9]){10}[^0-9]*$";
		public static string TenStartsWith207 = "^([^0-9]*2)([^0-9]*0)([^0-9]*7)([^0-9]*[0-9]){7}[^0-9]*$";
		public static string TenStartsWith800 = "^([^0-9]*8)([^0-9]*0)([^0-9]*0)([^0-9]*[0-9]){7}[^0-9]*$";
		public static string TenStartsWith888 = "^([^0-9]*8)([^0-9]*8)([^0-9]*8)([^0-9]*[0-9]){7}[^0-9]*$";
		public static string HasLetters = "[^a-zA-Z]";
		public static string HasAnything = ".*";
		public static string Tag = "phone";

		private static TagsCollection ChangeTags = new TagsCollection()
			{
				new Tag("comment", $"A script for fixing phone number formats"),
				new Tag("created_by", nameof(OsmPipeline)),
				new Tag("bot", "yes"),
				new Tag("source", @"https://wiki.openstreetmap.org/wiki/Automated_edits/blackboxlogic/MainePhone")
			};

		public static void FixPhones(ILoggerFactory loggerFactory, IConfigurationRoot config, OsmGeo scope)
		{
			var rejectLogger = loggerFactory.CreateLogger("Unfixable");
			Action<OsmGeo> rejector = a => rejectLogger.LogWarning($"Rejected {a.Type}:{a.Id} {a.Tags[Tag]}");
			var osmApi = new OsmApiEnder(loggerFactory.CreateLogger(typeof(OsmApiEnder)),
				config["OsmApiUrl"], config["OsmUsername"], config["OsmPassword"], ChangeTags);

			var badPhones = OverpassApi.Get(BadPhoneQuery(scope, Tag)).OsmToGeos();
			var betterPhones = FixPhones(badPhones, Tag, rejector).ToArray();
			var change = Translate.GeosToChange(null, betterPhones, null, nameof(OsmPipeline));

			osmApi.Upload(change);
		}

		public static string BadPhoneQuery(OsmGeo where, string tag)
		{
			var subQueries = new[] {
				Query(OverpassGeoType.Node, where,
					new Filter(tag, Comp.Like, Phones.HasAnything),
					new Filter(tag, Comp.NotLike, Phones.Correct)),
				Query(OverpassGeoType.Way, where,
					new Filter(tag, Comp.Like, Phones.HasAnything),
					new Filter(tag, Comp.NotLike, Phones.Correct)),
				Query(OverpassGeoType.Relation, where,
					new Filter(tag, Comp.Like, Phones.HasAnything),
					new Filter(tag, Comp.NotLike, Phones.Correct))
			};
			var query = Union(subQueries);
			query = AddOut(query, true);
			return query;
		}

		public static IEnumerable<OsmGeo> FixPhones(IEnumerable<OsmGeo> elements, string tag, Action<OsmGeo> Reject = null)
		{
			foreach (var element in elements)
			{
				if (!element.Tags.TryGetValue(tag, out string value))
					continue;
				// https://en.wikipedia.org/wiki/List_of_country_calling_codes
				if (Phones.TryFix(value, new[] { "207", "800", "888", "877" }, "207", out string fixedValue))
				{
					Console.WriteLine($"{element.Type}-{element.Id}.{tag}:\t{value}\t-->\t{fixedValue}");
					element.Tags[tag] = fixedValue;
					yield return element;
				}
				else
				{
					Reject?.Invoke(element);
				}
			}
		}

		public static bool TryFix(string number, string[] safeAreas, string assumedArea, out string fixedNumber)
		{
			if (number.Any(char.IsLetter))
			{
				fixedNumber = null;
				return false;
			}

			var numbers = new string(number.Where(char.IsDigit).ToArray());

			if (numbers.Length == 11 && numbers.StartsWith("1"))
			{
				fixedNumber = $"+1-{numbers.Substring(1, 3)}-{numbers.Substring(4, 3)}-{numbers.Substring(7, 4)}";
				return true;
			}
			else if (numbers.Length == 10 && safeAreas.Contains(numbers.Substring(0, 3)))
			{
				fixedNumber = $"+1-{numbers.Substring(0, 3)}-{numbers.Substring(3, 3)}-{numbers.Substring(6, 4)}";
				return true;
			}
			else if (numbers.Length == 7 && assumedArea != null)
			{
				fixedNumber = $"+1-{assumedArea}-{numbers.Substring(0, 3)}-{numbers.Substring(3, 4)}";
				return true;
			}
			else
			{
				fixedNumber = null;
				return false;
			}
		}

		public static bool TryFix(string number, out string fixedNumber)
		{
			var numbers = new string(number.Where(char.IsDigit).ToArray());

			if (numbers.Length == 11 && numbers.StartsWith("1"))
			{
				fixedNumber = $"+{numbers[0]}-{numbers.Substring(1, 3)}-{numbers.Substring(4, 3)}-{numbers.Substring(7, 4)}";
				return true;
			}
			else
			{
				fixedNumber = null;
				Console.WriteLine($"Couldn't fix {number}");
				return false;
			}
		}
	}
}
