using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Streets
	{
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
