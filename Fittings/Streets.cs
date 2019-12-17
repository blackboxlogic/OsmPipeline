using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Streets
	{
		public static string BuildStreetName(string predir, string name, string postdir, string suffix)
		{
			var parts = new string[] {
					DirectionExpansions[predir],
					name,
					DirectionExpansions[postdir],
					SuffixExpansions[suffix]
				}.Where(p => p != "");

			var fullName = string.Join(" ", parts);
			return fullName;
		}

		public static Dictionary<string, string> DirectionExpansions =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{{ "N" , "North" },
{ "NE" , "North East" },
{ "E" , "East" },
{ "SE" , "South East" },
{ "S" , "South" },
{ "SW" , "South West" },
{ "W" , "West" },
{ "NW" , "North West" },
{ "" , "" },
	};

		// From: https://pe.usps.com/text/pub28/28apc_002.htm
		public static Dictionary<string, string> SuffixExpansions =
			new Dictionary<string, string>(
				FileSerializer.ReadJson<Dictionary<string, string>>(@"Resources/StreetSUFFIX.json"),
				StringComparer.OrdinalIgnoreCase);

		// OneWay... Only 2158 in Maine, 0 in Westbrook
		public enum RDCLASS
		{
			Local,        //86570
			Private,      //35649
			Secondary,    //22796
			Primary,      //872
			Ramp,         //650
			// Probably stop here
			Paper_Street, //242
			Gated,        //174
			Crossover,    //89
			Other,        //67
			Alley,        //17
			Walkway,      //15
			Trail,        //11
			Service,      //10
			Vehicular_Trail, //7
		}
	}
}
